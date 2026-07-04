// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Operator configuration for the hosted-worlds control plane. Everything here is set by the OPERATOR
/// through <c>BBS_WH_*</c> environment variables — none of it is ever exposed to or changeable by players
/// (the quota values in particular are policy, not preferences). Defaults are development-friendly
/// (localhost, local image); a real deployment sets the domain, image and public host.
/// </summary>
public sealed class WorldHostConfig
{
    /// <summary>Bind address for the WorldHost API; loopback by default so it is not public — in the
    /// intended deployment Caddy proxies the public portal/API domain onto it.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 31417;

    /// <summary>Base domain under which every world gets its subdomain (<c>w-&lt;id&gt;.&lt;BaseDomain&gt;</c>),
    /// e.g. <c>play.blocksbeyondthestars.de</c>. "localhost" keeps development self-contained.</summary>
    public string BaseDomain { get; set; } = "localhost";

    /// <summary>Public hostname/IP native (UDP) clients connect to. Browser clients go through the
    /// per-world subdomain + Caddy; native UDP bypasses the proxy and needs the machine itself.</summary>
    public string PublicHost { get; set; } = "localhost";

    /// <summary>Dedicated-server image each world instance runs (one container per world).</summary>
    public string ServerImage { get; set; } = "blocks-beyond-the-stars-server:local";

    /// <summary>Docker network shared with the caddy-docker-proxy container so the proxy can reach the
    /// per-world WebSocket gateways by container name.</summary>
    public string DockerNetwork { get; set; } = "bbs-hosted";

    /// <summary>First host port handed to world instances. Each world gets ONE stable port from this range,
    /// published as both udp (native gameplay) and tcp (WS gateway → /status health probe).</summary>
    public int PortRangeStart { get; set; } = 32000;

    public int PortRangeSize { get; set; } = 1000;

    // --- Quotas (operator policy; see the hosted-worlds plan: free tier with tight limits) ---

    public int MaxWorldsPerAccount { get; set; } = 2;

    public int MaxPlayersPerWorld { get; set; } = 12;

    /// <summary>Idle minutes passed to each instance (BBS_IDLE_SHUTDOWN_MINUTES) — a world with no players
    /// saves and exits after this long; the reaper then marks it stopped in the registry.</summary>
    public int IdleShutdownMinutes { get; set; } = 20;

    /// <summary>How long a join request waits for a woken instance to answer its /status probe.</summary>
    public int WakeTimeoutSeconds { get; set; } = 90;

    public int SessionDays { get; set; } = 30;

    /// <summary>Names reserved for the developers — nobody else may register them as an account name or
    /// use them as an in-game player name on hosted worlds. Matched normalized (case-insensitive, with
    /// spaces/'-'/'_' stripped), so "ju ju" or "J_ustus" are caught too. Operator-extendable via
    /// <c>BBS_WH_RESERVED_NAMES</c> (comma-separated, replaces the default list).</summary>
    public List<string> ReservedNames { get; set; } = new()
    {
        "Marcel", "Justus", "Verena", "juju", "JuMaVe Games", "FlashMiner", "JustusJulius", "BloddyMary",
    };

    /// <summary>Claim code (BBS_WH_RESERVED_CLAIM_CODE) a developer presents once at signup to register a
    /// reserved name; the account is then permanently flagged as a developer account (which also unlocks
    /// reserved in-game names). Empty (default) = reserved names cannot be claimed at all.</summary>
    public string ReservedClaimCode { get; set; } = string.Empty;

    /// <summary>Directory holding the registry database (worldhost.db).</summary>
    public string DataDir { get; set; } = "worldhost";

    /// <summary>Loads config from BBS_WH_* environment variables over the defaults.</summary>
    public static WorldHostConfig FromEnvironment()
    {
        var c = new WorldHostConfig();

        static string? Env(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(v) ? null : v;
        }

        if (Env("BBS_WH_BIND") is { } bind) { c.BindAddress = bind; }
        if (Env("BBS_WH_PORT") is { } portStr && int.TryParse(portStr, out var port)) { c.Port = port; }
        if (Env("BBS_WH_BASE_DOMAIN") is { } domain) { c.BaseDomain = domain; }
        if (Env("BBS_WH_PUBLIC_HOST") is { } publicHost) { c.PublicHost = publicHost; }
        if (Env("BBS_WH_SERVER_IMAGE") is { } image) { c.ServerImage = image; }
        if (Env("BBS_WH_DOCKER_NETWORK") is { } network) { c.DockerNetwork = network; }
        if (Env("BBS_WH_PORT_RANGE_START") is { } rsStr && int.TryParse(rsStr, out var rs)) { c.PortRangeStart = rs; }
        if (Env("BBS_WH_PORT_RANGE_SIZE") is { } rzStr && int.TryParse(rzStr, out var rz)) { c.PortRangeSize = rz; }
        if (Env("BBS_WH_MAX_WORLDS_PER_ACCOUNT") is { } mwStr && int.TryParse(mwStr, out var mw)) { c.MaxWorldsPerAccount = mw; }
        if (Env("BBS_WH_MAX_PLAYERS") is { } mpStr && int.TryParse(mpStr, out var mp)) { c.MaxPlayersPerWorld = mp; }
        if (Env("BBS_WH_IDLE_MINUTES") is { } idleStr && int.TryParse(idleStr, out var idle)) { c.IdleShutdownMinutes = idle; }
        if (Env("BBS_WH_WAKE_TIMEOUT_SECONDS") is { } wtStr && int.TryParse(wtStr, out var wt)) { c.WakeTimeoutSeconds = wt; }
        if (Env("BBS_WH_SESSION_DAYS") is { } sdStr && int.TryParse(sdStr, out var sd)) { c.SessionDays = sd; }
        if (Env("BBS_WH_RESERVED_NAMES") is { } reserved)
        {
            c.ReservedNames = reserved.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
        }

        if (Env("BBS_WH_RESERVED_CLAIM_CODE") is { } claimCode) { c.ReservedClaimCode = claimCode; }
        if (Env("BBS_WH_DATA_DIR") is { } dataDir) { c.DataDir = dataDir; }

        return c;
    }
}
