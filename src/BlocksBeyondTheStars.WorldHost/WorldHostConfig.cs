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

    /// <summary>Base directory for per-world save data, bind-mounted into each instance at /app/saves —
    /// a bind mount (not a named volume) so THIS process can implement save upload/export directly.</summary>
    public string WorldsDir { get; set; } = Path.Combine("worldhost", "worlds");

    /// <summary>Version of the community rules text. Bump when the rules change: accounts that accepted an
    /// older version are asked to re-accept before they can create/join worlds.</summary>
    public int TermsVersion { get; set; } = 1;

    /// <summary>Upload size cap for a world.db save (bytes). Saves are block-edit deltas, so even large
    /// builds stay small; the cap mainly bounds abuse.</summary>
    public long UploadMaxBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>Operator token for the admin endpoints (report review, account bans). Empty (default)
    /// disables the admin API entirely.</summary>
    public string AdminToken { get; set; } = string.Empty;

    // --- Phase 3: lifecycle & abuse hardening (all operator policy) ---

    /// <summary>Months of inactivity after which a stopped world is archived: its saves move to the
    /// archive folder and its instance claim ends. Joining an archived world transparently restores it
    /// (it just takes a moment longer to wake). 0 = never archive.</summary>
    public int ArchiveAfterMonths { get; set; } = 6;

    /// <summary>Words that may not appear in account names, world names or in-game player names —
    /// matched against the same normalization as reserved names (lowercase, separators stripped), so
    /// "H-i-t-l-e-r" is caught too. Kid-facing service: better safe. <c>BBS_WH_BLOCKED_WORDS</c>
    /// (comma-separated) EXTENDS this list. Deliberately short and unambiguous to avoid Scunthorpe-style
    /// false positives.</summary>
    public List<string> BlockedNameWords { get; set; } = new()
    {
        "hitler", "nazi", "nigger", "neger", "fuck", "bitch", "hurensohn", "fotze", "wichser", "arschloch",
    };

    // Rate limits (fixed windows). Signup/login key on the caller IP, uploads/reports on the account —
    // they exist to blunt scripted abuse, not to inconvenience players.

    public int SignupPerHourPerIp { get; set; } = 5;

    public int LoginPerMinutePerIp { get; set; } = 10;

    public int UploadsPerHourPerAccount { get; set; } = 6;

    public int ReportsPerHourPerAccount { get; set; } = 10;

    // --- Public aggregate stats (GET /api/stats): four numbers for the website/client. Public and
    // unauthenticated, therefore doubly guarded: a cached single-flight snapshot plus a per-IP limit. ---

    /// <summary>Per-IP request limit for /api/stats (BBS_WH_STATS_PER_MINUTE); non-positive disables the limiter.</summary>
    public int StatsPerMinutePerIp { get; set; } = 30;

    /// <summary>Seconds the /api/stats snapshot is served from cache (BBS_WH_STATS_CACHE_SECONDS) —
    /// the instance /status probes behind the online-player count never run more often than this.</summary>
    public int StatsCacheSeconds { get; set; } = 30;

    // --- Legal pages (§5 DDG Impressum + DSGVO privacy). Operator-set on purpose: a SELF-HOSTED
    // WorldHost must carry ITS operator's data, never the project authors' — empty values make the
    // pages render a clear "not configured" notice instead of wrong legal information. ---

    /// <summary>Legal operator name shown on /impressum and /datenschutz (BBS_WH_LEGAL_NAME).</summary>
    public string LegalName { get; set; } = string.Empty;

    /// <summary>Postal address, comma-separated lines (BBS_WH_LEGAL_ADDRESS).</summary>
    public string LegalAddress { get; set; } = string.Empty;

    /// <summary>Contact email — §5 DDG requires one (BBS_WH_LEGAL_EMAIL).</summary>
    public string LegalEmail { get; set; } = string.Empty;

    // --- Fleet AI texts (optional). When AiBackendUrl is set, every world instance receives it as
    // BBS_AI_BACKEND_URL + BBS_AI_LEVEL, enabling LLM-authored NPC lines/mission flavour. The game
    // degrades gracefully either way (instant static line, async LLM upgrade), so this is pure opt-in. ---

    /// <summary>AI-backend URL passed to world instances (BBS_WH_AI_BACKEND_URL) — on the fleet the
    /// internal-only sibling container, e.g. <c>http://ai:8077</c>. Empty (default) = AI off.</summary>
    public string AiBackendUrl { get; set; } = string.Empty;

    /// <summary>AI level passed to world instances (BBS_WH_AI_LEVEL). TextOnly = NPC lines + board
    /// flavour text but no auto-published AI missions — the right fleet default.</summary>
    public string AiLevel { get; set; } = "TextOnly";

    // --- Per-instance resource limits. One runaway world must never take down the host: each world
    // container gets a hard memory cap (which .NET's cgroup-aware GC also uses to apply pressure
    // BEFORE the OOM kill), a CPU ceiling and a pids cap. The capacity gate bounds the SUM. ---

    /// <summary>Hard memory cap per world container, docker syntax (BBS_WH_INSTANCE_MEMORY). The same
    /// value is set as --memory-swap, so a capped instance cannot push the host into swap thrash.
    /// Empty = no limit (dev). An OOM-killed world is simply marked stopped by the reaper; the next
    /// join wakes it fresh.</summary>
    public string InstanceMemory { get; set; } = "768m";

    /// <summary>CPU ceiling per world container (BBS_WH_INSTANCE_CPUS, docker --cpus syntax). Empty = no limit.</summary>
    public string InstanceCpus { get; set; } = "2";

    /// <summary>Maximum world instances awake at the same time (BBS_WH_MAX_ACTIVE); wake requests beyond
    /// it get the friendly no-capacity error. Sized so MaxActive × InstanceMemory fits the host
    /// (default: 10 × 768m ≈ 7.5 GB on the 8 GB VPS). 0 = unlimited.</summary>
    public int MaxActiveInstances { get; set; } = 10;

    /// <summary>How the orchestrator probes an instance's /status: false (default, dev) = host loopback
    /// (127.0.0.1:hostPort); true (BBS_WH_PROBE_VIA_NETWORK, REQUIRED when WorldHost itself runs in a
    /// container) = the world container's name on the shared docker network — a containerized WorldHost's
    /// loopback can never reach host-published ports.</summary>
    public bool ProbeViaDockerNetwork { get; set; }

    // --- Admin web UI (Basic Auth, /admin). Separate from AdminToken (the script/API credential):
    // browsers can't send custom headers. Empty user or password (default) = admin UI off. ---

    /// <summary>Admin UI user (BBS_WH_ADMIN_USER).</summary>
    public string AdminUser { get; set; } = string.Empty;

    /// <summary>Admin UI password (BBS_WH_ADMIN_PASSWORD).</summary>
    public string AdminPassword { get; set; } = string.Empty;

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
        if (Env("BBS_WH_WORLDS_DIR") is { } worldsDir) { c.WorldsDir = worldsDir; }
        if (Env("BBS_WH_TERMS_VERSION") is { } tvStr && int.TryParse(tvStr, out var tv)) { c.TermsVersion = tv; }
        if (Env("BBS_WH_UPLOAD_MAX_BYTES") is { } upStr && long.TryParse(upStr, out var up)) { c.UploadMaxBytes = up; }
        if (Env("BBS_WH_ADMIN_TOKEN") is { } adminToken) { c.AdminToken = adminToken; }
        if (Env("BBS_WH_ARCHIVE_MONTHS") is { } amStr && int.TryParse(amStr, out var am)) { c.ArchiveAfterMonths = am; }
        if (Env("BBS_WH_BLOCKED_WORDS") is { } blocked)
        {
            c.BlockedNameWords.AddRange(blocked.Split(',').Select(w => w.Trim()).Where(w => w.Length > 0));
        }

        if (Env("BBS_WH_SIGNUPS_PER_HOUR") is { } suStr && int.TryParse(suStr, out var su)) { c.SignupPerHourPerIp = su; }
        if (Env("BBS_WH_LOGINS_PER_MINUTE") is { } liStr && int.TryParse(liStr, out var li)) { c.LoginPerMinutePerIp = li; }
        if (Env("BBS_WH_UPLOADS_PER_HOUR") is { } ulStr && int.TryParse(ulStr, out var ul)) { c.UploadsPerHourPerAccount = ul; }
        if (Env("BBS_WH_REPORTS_PER_HOUR") is { } rpStr && int.TryParse(rpStr, out var rp)) { c.ReportsPerHourPerAccount = rp; }
        if (Env("BBS_WH_STATS_PER_MINUTE") is { } spStr && int.TryParse(spStr, out var sp)) { c.StatsPerMinutePerIp = sp; }
        if (Env("BBS_WH_STATS_CACHE_SECONDS") is { } scStr && int.TryParse(scStr, out var sc)) { c.StatsCacheSeconds = sc; }
        if (Env("BBS_WH_LEGAL_NAME") is { } legalName) { c.LegalName = legalName; }
        if (Env("BBS_WH_LEGAL_ADDRESS") is { } legalAddress) { c.LegalAddress = legalAddress; }
        if (Env("BBS_WH_LEGAL_EMAIL") is { } legalEmail) { c.LegalEmail = legalEmail; }
        if (Env("BBS_WH_AI_BACKEND_URL") is { } aiUrl) { c.AiBackendUrl = aiUrl; }
        if (Env("BBS_WH_AI_LEVEL") is { } aiLevel) { c.AiLevel = aiLevel; }
        if (Env("BBS_WH_INSTANCE_MEMORY") is { } mem) { c.InstanceMemory = mem == "none" ? string.Empty : mem; }
        if (Env("BBS_WH_INSTANCE_CPUS") is { } cpus) { c.InstanceCpus = cpus == "none" ? string.Empty : cpus; }
        if (Env("BBS_WH_MAX_ACTIVE") is { } maStr && int.TryParse(maStr, out var ma)) { c.MaxActiveInstances = ma; }
        if (Env("BBS_WH_PROBE_VIA_NETWORK") is { } pvnStr && bool.TryParse(pvnStr, out var pvn)) { c.ProbeViaDockerNetwork = pvn; }
        if (Env("BBS_WH_ADMIN_USER") is { } adminUser) { c.AdminUser = adminUser; }
        if (Env("BBS_WH_ADMIN_PASSWORD") is { } adminPassword) { c.AdminPassword = adminPassword; }

        return c;
    }
}
