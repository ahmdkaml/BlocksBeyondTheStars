// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

public sealed record AccountRecord(string Id, string Name, bool IsDeveloper = false);

public sealed record WorldRecord(
    string Id,
    string OwnerAccountId,
    string DisplayName,
    string JoinSecret,
    int HostPort,
    string Status,
    string ContainerId,
    long CreatedUnix,
    long LastStartedUnix)
{
    /// <summary>The public routing label: <c>w-&lt;id&gt;.&lt;BaseDomain&gt;</c> resolves to this world's instance.</summary>
    public string Subdomain => "w-" + Id;
}

/// <summary>World lifecycle states tracked in the registry.</summary>
public static class WorldStatus
{
    public const string Stopped = "stopped";
    public const string Starting = "starting";
    public const string Running = "running";
}

/// <summary>
/// The control plane's registry — accounts, bearer sessions and worlds in one SQLite file. Deliberately
/// privacy-minimal for the kid-facing free tier: an account is a display name + password hash, no email,
/// no personal data (the plan's account MVP). Every mutation is serialized on one connection behind a
/// lock, mirroring the game's SqliteWorldRepository pattern; the write volume here is tiny.
/// </summary>
public sealed class HostRegistry : IDisposable
{
    // Account names double as visible player identity; same cap as in-game names (24) and a conservative
    // character set so they are safe in URLs, logs and docker args without escaping anywhere.
    private static readonly Regex AccountNameRx = new("^[A-Za-z0-9_-]{3,24}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex WorldIdRx = new("^[a-f0-9]{12}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly object _gate = new();
    private readonly SqliteConnection _db;
    private readonly WorldHostConfig _config;

    public HostRegistry(WorldHostConfig config, string? databasePath = null)
    {
        _config = config;
        string path = databasePath ?? Path.Combine(config.DataDir, "worldhost.db");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS account(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                password_hash TEXT NOT NULL,
                is_developer INTEGER NOT NULL DEFAULT 0,
                created_unix INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS session(
                token_hash TEXT PRIMARY KEY,
                account_id TEXT NOT NULL,
                expires_unix INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS world(
                id TEXT PRIMARY KEY,
                owner_account_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                join_secret TEXT NOT NULL,
                host_port INTEGER NOT NULL UNIQUE,
                status TEXT NOT NULL,
                container_id TEXT NOT NULL DEFAULT '',
                created_unix INTEGER NOT NULL,
                last_started_unix INTEGER NOT NULL DEFAULT 0);
            """);

        // Tolerant upgrade for registries created before the developer flag existed (pre-deployment dev
        // databases only); SQLite has no ADD COLUMN IF NOT EXISTS.
        try
        {
            Exec("ALTER TABLE account ADD COLUMN is_developer INTEGER NOT NULL DEFAULT 0;");
        }
        catch (SqliteException)
        {
            // column already exists
        }
    }

    // ---------------- Reserved names ----------------

    /// <summary>True when a name collides with a developer-reserved name. Both sides are normalized —
    /// lowercased with spaces/'-'/'_' stripped — so padding or separator tricks ("ju ju", "J_ustus")
    /// don't slip past the reservation.</summary>
    public bool IsReservedName(string? name)
    {
        string normalized = NormalizeName(name);
        return normalized.Length > 0 && _config.ReservedNames.Any(r => NormalizeName(r) == normalized);
    }

    private static string NormalizeName(string? name)
        => new((name ?? string.Empty).ToLowerInvariant().Where(c => c is not (' ' or '-' or '_')).ToArray());

    public static bool IsValidWorldId(string id) => WorldIdRx.IsMatch(id);

    // ---------------- Accounts & sessions ----------------

    /// <summary>Creates an account and returns a fresh session token. Fails on invalid/taken/reserved
    /// names or a too-short password. A developer registering a reserved name presents the operator's
    /// claim code, which permanently flags the account as a developer account. The error string is safe
    /// to show to the player.</summary>
    public (bool Ok, string Error, string AccountId, string SessionToken) CreateAccount(string name, string password, string? claimCode = null)
    {
        if (!AccountNameRx.IsMatch(name ?? string.Empty))
        {
            return (false, "Name must be 3-24 characters: letters, digits, '-' or '_'.", string.Empty, string.Empty);
        }

        if ((password ?? string.Empty).Length < 8)
        {
            return (false, "Password must be at least 8 characters.", string.Empty, string.Empty);
        }

        bool isDeveloper = false;
        if (IsReservedName(name))
        {
            // With no claim code configured, reserved names are simply unclaimable — the safe default.
            if (string.IsNullOrEmpty(_config.ReservedClaimCode) || !FixedTimeEquals(claimCode ?? string.Empty, _config.ReservedClaimCode))
            {
                return (false, "This name is reserved.", string.Empty, string.Empty);
            }

            isDeveloper = true;
        }

        lock (_gate)
        {
            using (var check = Cmd("SELECT 1 FROM account WHERE name = $n"))
            {
                check.Parameters.AddWithValue("$n", name);
                if (check.ExecuteScalar() != null)
                {
                    return (false, "This name is already taken.", string.Empty, string.Empty);
                }
            }

            string id = "acc-" + RandomHex(12);
            using (var ins = Cmd("INSERT INTO account(id, name, password_hash, is_developer, created_unix) VALUES($i, $n, $p, $d, $c)"))
            {
                ins.Parameters.AddWithValue("$i", id);
                ins.Parameters.AddWithValue("$n", name);
                ins.Parameters.AddWithValue("$p", PasswordHasher.Hash(password!));
                ins.Parameters.AddWithValue("$d", isDeveloper ? 1 : 0);
                ins.Parameters.AddWithValue("$c", NowUnix());
                ins.ExecuteNonQuery();
            }

            return (true, string.Empty, id, CreateSessionLocked(id));
        }
    }

    /// <summary>Verifies credentials and returns a fresh session token, or null. One generic failure —
    /// it never reveals whether the name exists.</summary>
    public (string AccountId, string SessionToken)? Login(string name, string password)
    {
        lock (_gate)
        {
            using var cmd = Cmd("SELECT id, password_hash FROM account WHERE name = $n");
            cmd.Parameters.AddWithValue("$n", name ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read() || !PasswordHasher.Verify(password ?? string.Empty, reader.GetString(1)))
            {
                return null;
            }

            string id = reader.GetString(0);
            reader.Close();
            return (id, CreateSessionLocked(id));
        }
    }

    /// <summary>Resolves a bearer token to its account, or null when unknown/expired.</summary>
    public AccountRecord? ResolveSession(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_gate)
        {
            using var cmd = Cmd("""
                SELECT a.id, a.name, a.is_developer FROM session s JOIN account a ON a.id = s.account_id
                WHERE s.token_hash = $t AND s.expires_unix >= $now
                """);
            cmd.Parameters.AddWithValue("$t", Sha256Hex(token));
            cmd.Parameters.AddWithValue("$now", NowUnix());
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? new AccountRecord(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0) : null;
        }
    }

    private string CreateSessionLocked(string accountId)
    {
        string token = RandomHex(32);
        using var cmd = Cmd("INSERT INTO session(token_hash, account_id, expires_unix) VALUES($t, $a, $e)");
        cmd.Parameters.AddWithValue("$t", Sha256Hex(token));
        cmd.Parameters.AddWithValue("$a", accountId);
        cmd.Parameters.AddWithValue("$e", NowUnix() + (long)_config.SessionDays * 86400);
        cmd.ExecuteNonQuery();
        return token;
    }

    // ---------------- Worlds ----------------

    /// <summary>Creates a world for an account: enforces the per-account quota, allocates the world id,
    /// per-world join secret and a stable host port from the configured range.</summary>
    public (bool Ok, string Error, WorldRecord? World) CreateWorld(string ownerAccountId, string displayName)
    {
        displayName = (displayName ?? string.Empty).Trim();
        if (displayName.Length is < 1 or > 40 || displayName.Any(char.IsControl))
        {
            return (false, "World name must be 1-40 printable characters.", null);
        }

        lock (_gate)
        {
            using (var count = Cmd("SELECT COUNT(*) FROM world WHERE owner_account_id = $o"))
            {
                count.Parameters.AddWithValue("$o", ownerAccountId);
                if (Convert.ToInt32(count.ExecuteScalar()) >= _config.MaxWorldsPerAccount)
                {
                    return (false, $"World limit reached ({_config.MaxWorldsPerAccount} per account).", null);
                }
            }

            int? port = NextFreePortLocked();
            if (port is null)
            {
                return (false, "No capacity available right now — please try again later.", null);
            }

            var world = new WorldRecord(
                Id: RandomHex(6), // 6 random bytes = the 12 hex chars WorldIdRx/subdomains are built on
                OwnerAccountId: ownerAccountId,
                DisplayName: displayName,
                JoinSecret: RandomHex(32),
                HostPort: port.Value,
                Status: WorldStatus.Stopped,
                ContainerId: string.Empty,
                CreatedUnix: NowUnix(),
                LastStartedUnix: 0);

            using var ins = Cmd("""
                INSERT INTO world(id, owner_account_id, display_name, join_secret, host_port, status, container_id, created_unix, last_started_unix)
                VALUES($i, $o, $d, $s, $p, $st, '', $c, 0)
                """);
            ins.Parameters.AddWithValue("$i", world.Id);
            ins.Parameters.AddWithValue("$o", world.OwnerAccountId);
            ins.Parameters.AddWithValue("$d", world.DisplayName);
            ins.Parameters.AddWithValue("$s", world.JoinSecret);
            ins.Parameters.AddWithValue("$p", world.HostPort);
            ins.Parameters.AddWithValue("$st", world.Status);
            ins.Parameters.AddWithValue("$c", world.CreatedUnix);
            ins.ExecuteNonQuery();

            return (true, string.Empty, world);
        }
    }

    public IReadOnlyList<WorldRecord> ListWorlds(string ownerAccountId)
    {
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE owner_account_id = $o ORDER BY created_unix");
            cmd.Parameters.AddWithValue("$o", ownerAccountId);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    public WorldRecord? GetWorld(string worldId)
    {
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", worldId ?? string.Empty);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadWorld(reader) : null;
        }
    }

    /// <summary>Worlds currently marked running/starting — the reaper reconciles these against Docker.</summary>
    public IReadOnlyList<WorldRecord> ListActiveWorlds()
    {
        lock (_gate)
        {
            using var cmd = Cmd(SelectWorld + " WHERE status != $st");
            cmd.Parameters.AddWithValue("$st", WorldStatus.Stopped);
            using var reader = cmd.ExecuteReader();
            var list = new List<WorldRecord>();
            while (reader.Read())
            {
                list.Add(ReadWorld(reader));
            }

            return list;
        }
    }

    /// <summary>Resolves a routing subdomain ("w-&lt;id&gt;") to its world — Caddy's on-demand-TLS "ask"
    /// endpoint uses this to only ever issue certificates for subdomains that really exist.</summary>
    public WorldRecord? FindBySubdomain(string subdomain)
    {
        if (subdomain is null || !subdomain.StartsWith("w-", StringComparison.Ordinal))
        {
            return null;
        }

        string id = subdomain.Substring(2);
        return IsValidWorldId(id) ? GetWorld(id) : null;
    }

    public void SetWorldStatus(string worldId, string status, string containerId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("""
                UPDATE world SET status = $st, container_id = $c,
                    last_started_unix = CASE WHEN $st = 'starting' THEN $now ELSE last_started_unix END
                WHERE id = $i
                """);
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$c", containerId);
            cmd.Parameters.AddWithValue("$now", NowUnix());
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteWorld(string worldId)
    {
        lock (_gate)
        {
            using var cmd = Cmd("DELETE FROM world WHERE id = $i");
            cmd.Parameters.AddWithValue("$i", worldId);
            cmd.ExecuteNonQuery();
        }
    }

    // ---------------- Internals ----------------

    private const string SelectWorld =
        "SELECT id, owner_account_id, display_name, join_secret, host_port, status, container_id, created_unix, last_started_unix FROM world";

    private static WorldRecord ReadWorld(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
        r.GetString(5), r.GetString(6), r.GetInt64(7), r.GetInt64(8));

    /// <summary>Smallest unused port in the configured range. Ports stay allocated for a world's lifetime
    /// (they are its stable native-UDP endpoint), so a deleted world's port returns to the pool.</summary>
    private int? NextFreePortLocked()
    {
        var used = new HashSet<int>();
        using (var cmd = Cmd("SELECT host_port FROM world"))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                used.Add(reader.GetInt32(0));
            }
        }

        for (int p = _config.PortRangeStart; p < _config.PortRangeStart + _config.PortRangeSize; p++)
        {
            if (!used.Contains(p))
            {
                return p;
            }
        }

        return null;
    }

    private SqliteCommand Cmd(string sql)
    {
        var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private void Exec(string sql)
    {
        using var cmd = Cmd(sql);
        cmd.ExecuteNonQuery();
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string RandomHex(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    /// <summary>Constant-time string equality — used for the reserved-name claim code so a wrong code
    /// can't be probed character by character through timing.</summary>
    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));

    public void Dispose() => _db.Dispose();
}
