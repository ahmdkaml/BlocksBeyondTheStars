// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using BlocksBeyondTheStars.Shared.Security;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>Everything a client needs to enter a hosted world: the wss endpoint for browsers, the
/// host:port for native UDP clients, and a short-lived join token proving the control plane vouched
/// for this player.</summary>
public sealed record JoinGrant(
    string WorldId,
    string DisplayName,
    string WssUrl,
    string NativeHost,
    int NativePort,
    string JoinToken,
    long TokenExpiresUnix);

/// <summary>
/// The allocation core of the control plane: "give me world X" — route to the running instance, or wake
/// it (start container, wait for its /status to answer) and then route. Per-world locking serializes
/// concurrent wakes of the same world; different worlds wake in parallel. The instance does the rest
/// itself (idle shutdown, join-token enforcement, owner bootstrap — the Phase-0 server features).
/// </summary>
public sealed class WorldOrchestrator
{
    /// <summary>Join tokens are one-shot handshake material: issued, passed to the game server, verified —
    /// all within seconds. A short life keeps a leaked token near-useless.</summary>
    private const int JoinTokenTtlSeconds = 120;

    private readonly WorldHostConfig _config;
    private readonly HostRegistry _registry;
    private readonly IInstanceLauncher _launcher;
    private readonly Func<WorldRecord, Task<bool>> _healthProbe;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _wakeLocks = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public WorldOrchestrator(
        WorldHostConfig config,
        HostRegistry registry,
        IInstanceLauncher launcher,
        Func<WorldRecord, Task<bool>>? healthProbe = null)
    {
        _config = config;
        _registry = registry;
        _launcher = launcher;
        _healthProbe = healthProbe ?? DefaultProbeAsync;
    }

    /// <summary>Default probe: the instance's WS gateway answers /status on the world's (loopback-bound)
    /// tcp host port once the server is up and accepting players.</summary>
    private static async Task<bool> DefaultProbeAsync(WorldRecord world)
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{world.HostPort}/status").ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ensures the world's instance is up (waking it if needed) and returns the join grant for
    /// this player, or a player-safe error.</summary>
    public async Task<(JoinGrant? Grant, string Error)> JoinAsync(string worldId, AccountRecord account, string playerName)
    {
        playerName = (playerName ?? string.Empty).Trim();
        if (playerName.Length is < 1 or > 24 || playerName.Any(char.IsControl))
        {
            return (null, "Player name must be 1-24 printable characters.");
        }

        // Developer-reserved names are protected as IN-GAME identities too, not only as account names —
        // otherwise any account could impersonate "Justus" inside a world. Developer accounts (claimed
        // with the operator's code at signup) may use them freely.
        if (!account.IsDeveloper && _registry.IsReservedName(playerName))
        {
            return (null, "This player name is reserved.");
        }

        var (world, error) = await EnsureRunningAsync(worldId).ConfigureAwait(false);
        if (world is null)
        {
            return (null, error);
        }

        long expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + JoinTokenTtlSeconds;
        string token = HostedJoinToken.Create(world.JoinSecret, world.Id, account.Id, playerName, expires);
        return (new JoinGrant(
            WorldId: world.Id,
            DisplayName: world.DisplayName,
            WssUrl: $"wss://{world.Subdomain}.{_config.BaseDomain}",
            NativeHost: _config.PublicHost,
            NativePort: world.HostPort,
            JoinToken: token,
            TokenExpiresUnix: expires), string.Empty);
    }

    /// <summary>Route-or-wake: the running instance is reused; a stopped (or crashed-out) one is started
    /// and awaited until its /status probe answers or the wake timeout expires.</summary>
    public async Task<(WorldRecord? World, string Error)> EnsureRunningAsync(string worldId)
    {
        if (_registry.GetWorld(worldId) is not { } world)
        {
            return (null, "World not found.");
        }

        var gate = _wakeLocks.GetOrAdd(world.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            world = _registry.GetWorld(world.Id)!; // re-read under the lock: a parallel join may have woken it

            if (world.Status is WorldStatus.Running or WorldStatus.Starting && _launcher.IsRunning(world.ContainerId))
            {
                if (world.Status == WorldStatus.Starting)
                {
                    return await AwaitHealthyAsync(world).ConfigureAwait(false);
                }

                return (world, string.Empty);
            }

            // Registry says active but the container is gone (idle shutdown, crash, host reboot) — reconcile,
            // then fall through to a fresh start.
            if (world.Status != WorldStatus.Stopped)
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
            }

            string containerId;
            try
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Starting, string.Empty);
                containerId = _launcher.Start(world);
                _registry.SetWorldStatus(world.Id, WorldStatus.Starting, containerId);
            }
            catch (Exception)
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
                return (null, "The world could not be started — please try again in a moment.");
            }

            return await AwaitHealthyAsync(_registry.GetWorld(world.Id)!).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(WorldRecord? World, string Error)> AwaitHealthyAsync(WorldRecord world)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_config.WakeTimeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await _healthProbe(world).ConfigureAwait(false))
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Running, world.ContainerId);
                return (_registry.GetWorld(world.Id), string.Empty);
            }

            if (!_launcher.IsRunning(world.ContainerId))
            {
                break; // died during boot — no point waiting out the timeout
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        _launcher.Stop(world.ContainerId);
        _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
        return (null, "The world did not come up in time — please try again.");
    }

    /// <summary>Stops a world's instance on request (the owner's "stop now"; the usual path is the
    /// instance's own idle shutdown).</summary>
    public void StopWorld(WorldRecord world)
    {
        _launcher.Stop(world.ContainerId);
        _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
    }

    /// <summary>Reconciles registry state with reality: a world marked active whose container has exited
    /// (idle shutdown is the normal case) is marked stopped, so joins wake it cleanly and world lists tell
    /// the truth. Called periodically by the host's background loop.</summary>
    public int Reap()
    {
        int reaped = 0;
        foreach (var world in _registry.ListActiveWorlds())
        {
            if (!_launcher.IsRunning(world.ContainerId))
            {
                _registry.SetWorldStatus(world.Id, WorldStatus.Stopped, string.Empty);
                reaped++;
            }
        }

        return reaped;
    }
}
