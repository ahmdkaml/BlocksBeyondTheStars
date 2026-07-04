// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Shared.Security;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Hosted-worlds control plane (fleet phase 1): accounts (privacy-minimal, PBKDF2), the world registry
/// with its operator quotas and stable port allocation, and the orchestrator's route-or-wake allocation —
/// driven against a fake launcher, so the logic is covered without Docker.
/// </summary>
public sealed class WorldHostTests : IDisposable
{
    private readonly string _root;

    public WorldHostTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_wh_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    private HostRegistry NewRegistry(WorldHostConfig? config = null)
    {
        var registry = new HostRegistry(
            config ?? new WorldHostConfig(),
            System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    private readonly List<HostRegistry> _registries = new();

    /// <summary>In-memory stand-in for Docker: containers "run" until stopped (or told to die).</summary>
    private sealed class FakeLauncher : IInstanceLauncher
    {
        public int StartCount;
        public bool FailStart;
        public readonly HashSet<string> Running = new(StringComparer.Ordinal);

        public string Start(WorldRecord world)
        {
            if (FailStart)
            {
                throw new InvalidOperationException("docker run failed (simulated)");
            }

            StartCount++;
            string id = "container-" + StartCount;
            Running.Add(id);
            return id;
        }

        public void Stop(string containerId) => Running.Remove(containerId);

        public bool IsRunning(string containerId) => containerId != null && Running.Contains(containerId);
    }

    /// <summary>Orchestrator whose "instance is healthy" probe is simply "its fake container runs".</summary>
    private static WorldOrchestrator NewOrchestrator(HostRegistry registry, FakeLauncher launcher, WorldHostConfig config)
        => new(config, registry, launcher, w => Task.FromResult(launcher.IsRunning(w.ContainerId)));

    // ---------------- Password hashing ----------------

    [Fact]
    public void PasswordHash_Verifies_AndRejectsWrongPassword()
    {
        string stored = PasswordHasher.Hash("correct horse battery");
        Assert.True(PasswordHasher.Verify("correct horse battery", stored));
        Assert.False(PasswordHasher.Verify("wrong", stored));
        Assert.False(PasswordHasher.Verify("correct horse battery", "garbage-record"));
    }

    // ---------------- Accounts & sessions ----------------

    [Fact]
    public void Signup_Login_And_SessionResolution_Work()
    {
        var registry = NewRegistry();

        var (ok, _, accountId, session) = registry.CreateAccount("Pilot", "super-secret-1");
        Assert.True(ok);
        Assert.Equal("Pilot", registry.ResolveSession(session)!.Name);

        var login = registry.Login("Pilot", "super-secret-1");
        Assert.NotNull(login);
        Assert.Equal(accountId, login!.Value.AccountId);
        Assert.Equal(accountId, registry.ResolveSession(login.Value.SessionToken)!.Id);

        Assert.Null(registry.Login("Pilot", "wrong-password"));
        Assert.Null(registry.Login("Nobody", "super-secret-1"));
        Assert.Null(registry.ResolveSession("not-a-token"));
        Assert.Null(registry.ResolveSession(null));
    }

    [Fact]
    public void Signup_Rejects_TakenNames_CaseInsensitive_AndInvalidInput()
    {
        var registry = NewRegistry();
        Assert.True(registry.CreateAccount("Pilot", "super-secret-1").Ok);

        Assert.False(registry.CreateAccount("pilot", "super-secret-1").Ok);    // taken (NOCASE)
        Assert.False(registry.CreateAccount("ab", "super-secret-1").Ok);       // too short
        Assert.False(registry.CreateAccount("has space", "super-secret-1").Ok); // bad charset
        Assert.False(registry.CreateAccount("Fine", "short").Ok);              // weak password
    }

    // ---------------- World registry ----------------

    [Fact]
    public void CreateWorld_EnforcesQuota_AndAllocatesUniqueStablePorts()
    {
        var config = new WorldHostConfig { MaxWorldsPerAccount = 2, PortRangeStart = 32000 };
        var registry = NewRegistry(config);
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1");

        var w1 = registry.CreateWorld(accountId, "First World");
        var w2 = registry.CreateWorld(accountId, "Second World");
        Assert.True(w1.Ok && w2.Ok);
        Assert.NotEqual(w1.World!.Id, w2.World!.Id);
        Assert.Equal(new[] { 32000, 32001 }, new[] { w1.World.HostPort, w2.World.HostPort });
        Assert.Equal(WorldStatus.Stopped, w1.World.Status);

        var w3 = registry.CreateWorld(accountId, "One Too Many");
        Assert.False(w3.Ok); // quota (2) reached
        Assert.Contains("limit", w3.Error, StringComparison.OrdinalIgnoreCase);

        // A deleted world's port returns to the pool (it is the world's stable native endpoint otherwise).
        registry.DeleteWorld(w1.World.Id);
        var w4 = registry.CreateWorld(accountId, "Replacement");
        Assert.True(w4.Ok);
        Assert.Equal(32000, w4.World!.HostPort);
    }

    [Fact]
    public void CreateWorld_ValidatesDisplayName()
    {
        var registry = NewRegistry();
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1");

        Assert.False(registry.CreateWorld(accountId, "").Ok);
        Assert.False(registry.CreateWorld(accountId, "   ").Ok);
        Assert.False(registry.CreateWorld(accountId, new string('x', 41)).Ok);
        Assert.False(registry.CreateWorld(accountId, "evil\nname").Ok);
        Assert.True(registry.CreateWorld(accountId, "Justus' Welt 🚀").Ok); // spaces/unicode are fine — it's only an env VALUE
    }

    [Fact]
    public void FindBySubdomain_ResolvesRealWorlds_AndRejectsGarbage()
    {
        var registry = NewRegistry();
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1");
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.Equal(world.Id, registry.FindBySubdomain(world.Subdomain)!.Id);
        Assert.Null(registry.FindBySubdomain("w-000000000000"));      // well-formed but unknown
        Assert.Null(registry.FindBySubdomain("evil"));                // no prefix
        Assert.Null(registry.FindBySubdomain("w-NOTHEX!"));           // invalid id
    }

    // ---------------- Orchestrator: route-or-wake ----------------

    private static (string AccountId, AccountRecord Account) NewAccount(HostRegistry registry, string name = "Owner")
    {
        var (ok, error, accountId, session) = registry.CreateAccount(name, "super-secret-1");
        Assert.True(ok, error);
        return (accountId, registry.ResolveSession(session)!);
    }

    [Fact]
    public async Task Join_WakesAStoppedWorld_AndIssuesAValidToken()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5, BaseDomain = "play.example.de", PublicHost = "play.example.de" };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "Pilot");

        Assert.Equal(string.Empty, error);
        Assert.NotNull(grant);
        Assert.Equal(1, launcher.StartCount);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
        Assert.Equal($"wss://w-{world.Id}.play.example.de", grant!.WssUrl);
        Assert.Equal(world.HostPort, grant.NativePort);

        // The grant's token must satisfy exactly the check the game server runs (Phase 0):
        Assert.True(HostedJoinToken.TryValidate(world.JoinSecret, world.Id, grant.JoinToken,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out var tokenAccount, out var tokenPlayer, out _));
        Assert.Equal(accountId, tokenAccount);
        Assert.Equal("Pilot", tokenPlayer);
    }

    [Fact]
    public async Task Join_ReusesTheRunningInstance()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "P1")).Grant);
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "P2")).Grant);

        Assert.Equal(1, launcher.StartCount); // second join routed to the live instance, no second container
    }

    [Fact]
    public async Task Reap_MarksIdleExitedWorldsStopped_AndNextJoinRewakes()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher();
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        await orchestrator.JoinAsync(world.Id, account, "P1");
        string containerId = registry.GetWorld(world.Id)!.ContainerId;

        // The instance idle-shuts-down (Phase 0) — its container exits on its own.
        launcher.Stop(containerId);
        Assert.Equal(1, orchestrator.Reap());
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);

        // The next join wakes a fresh container.
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, account, "P1")).Grant);
        Assert.Equal(2, launcher.StartCount);
        Assert.Equal(WorldStatus.Running, registry.GetWorld(world.Id)!.Status);
    }

    [Fact]
    public async Task Join_FailedStart_LeavesTheWorldStopped_WithAPlayerSafeError()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 1 };
        var registry = NewRegistry(config);
        var launcher = new FakeLauncher { FailStart = true };
        var orchestrator = NewOrchestrator(registry, launcher, config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "P1");

        Assert.Null(grant);
        Assert.NotEqual(string.Empty, error);
        Assert.DoesNotContain("docker", error, StringComparison.OrdinalIgnoreCase); // no internals leak to players
        Assert.Equal(WorldStatus.Stopped, registry.GetWorld(world.Id)!.Status);
    }

    [Fact]
    public async Task Join_RejectsUnknownWorlds_AndBadPlayerNames()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 1 };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);
        var (accountId, account) = NewAccount(registry);
        var world = registry.CreateWorld(accountId, "My World").World!;

        Assert.Null((await orchestrator.JoinAsync("000000000000", account, "P1")).Grant);
        Assert.Null((await orchestrator.JoinAsync(world.Id, account, "")).Grant);
        Assert.Null((await orchestrator.JoinAsync(world.Id, account, new string('x', 25))).Grant);
    }

    // ---------------- Reserved developer names ----------------

    [Theory]
    [InlineData("Justus")]      // exact
    [InlineData("justus")]      // case
    [InlineData("Flash-Miner")] // separator trick vs "FlashMiner"
    [InlineData("Ju_Ju")]       // separator trick vs "juju"
    [InlineData("JuMaVeGames")] // "JuMaVe Games" (space is stripped in normalization)
    public void Signup_RejectsReservedNames_WhenNoClaimCodeIsConfigured(string name)
    {
        var registry = NewRegistry(); // default config: claim code empty ⇒ reserved names unclaimable
        var result = registry.CreateAccount(name, "super-secret-1");
        Assert.False(result.Ok);
        Assert.Contains("reserved", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Signup_WithTheClaimCode_CreatesADeveloperAccount()
    {
        var config = new WorldHostConfig { ReservedClaimCode = "dev-code-123" };
        var registry = NewRegistry(config);

        Assert.False(registry.CreateAccount("Justus", "super-secret-1", "wrong-code").Ok);

        var (ok, error, _, session) = registry.CreateAccount("Justus", "super-secret-1", "dev-code-123");
        Assert.True(ok, error);
        Assert.True(registry.ResolveSession(session)!.IsDeveloper);

        // Ordinary names don't need (and don't get) developer status, code or not.
        var (okPlain, _, _, plainSession) = registry.CreateAccount("Pilot", "super-secret-1");
        Assert.True(okPlain);
        Assert.False(registry.ResolveSession(plainSession)!.IsDeveloper);
    }

    [Fact]
    public async Task Join_ReservedInGameName_OnlyForDeveloperAccounts()
    {
        var config = new WorldHostConfig { WakeTimeoutSeconds = 5, ReservedClaimCode = "dev-code-123" };
        var registry = NewRegistry(config);
        var orchestrator = NewOrchestrator(registry, new FakeLauncher(), config);

        var (accountId, account) = NewAccount(registry, "Pilot");
        var world = registry.CreateWorld(accountId, "My World").World!;

        // A normal account cannot impersonate a developer in-game — on ANY hosted world.
        var (grant, error) = await orchestrator.JoinAsync(world.Id, account, "Justus");
        Assert.Null(grant);
        Assert.Contains("reserved", error, StringComparison.OrdinalIgnoreCase);

        // The claimed developer account may play under the reserved name.
        var (_, _, _, devSession) = registry.CreateAccount("FlashMiner", "super-secret-1", "dev-code-123");
        var dev = registry.ResolveSession(devSession)!;
        Assert.NotNull((await orchestrator.JoinAsync(world.Id, dev, "Justus")).Grant);
    }

    public void Dispose()
    {
        try
        {
            foreach (var r in _registries)
            {
                r.Dispose();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
