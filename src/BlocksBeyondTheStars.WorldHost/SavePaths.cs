// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Microsoft.Data.Sqlite;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Where a hosted world's save lives on the host, plus the validation an UPLOADED save must pass before
/// it is allowed anywhere near an instance. The instance's /app/saves is a bind mount of
/// <c>&lt;WorldsDir&gt;/&lt;worldId&gt;/saves</c>, and the server stores a world named X at
/// <c>saves/X/world.db</c> (SaveGamePaths) — hosted instances run with BBS_WORLD=&lt;worldId&gt;.
/// </summary>
public static class SavePaths
{
    public static string HostSavesDir(WorldHostConfig config, string worldId)
        => Path.GetFullPath(Path.Combine(config.WorldsDir, worldId, "saves"));

    public static string WorldDbPath(WorldHostConfig config, string worldId)
        => Path.Combine(HostSavesDir(config, worldId), worldId, "world.db");

    /// <summary>Where an archived world's saves rest (<c>_archive</c> can't collide with world ids —
    /// they are pure hex). Same volume as the live dir, so archiving is a rename, not a copy.</summary>
    public static string ArchivedSavesDir(WorldHostConfig config, string worldId)
        => Path.GetFullPath(Path.Combine(config.WorldsDir, "_archive", worldId, "saves"));

    /// <summary>Moves a world's saves into the archive. True when something was moved; false when there
    /// was nothing to move (a world that was never started has no saves — archiving it is just the
    /// status flip).</summary>
    public static bool MoveToArchive(WorldHostConfig config, string worldId)
        => MoveDir(HostSavesDir(config, worldId), ArchivedSavesDir(config, worldId));

    /// <summary>Restores an archived world's saves so the instance can be started again.</summary>
    public static bool RestoreFromArchive(WorldHostConfig config, string worldId)
        => MoveDir(ArchivedSavesDir(config, worldId), HostSavesDir(config, worldId));

    private static bool MoveDir(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(to))
        {
            Directory.Delete(to, recursive: true); // stale leftover from an interrupted earlier move
        }

        Directory.Move(from, to);
        return true;
    }

    /// <summary>
    /// Validates an uploaded candidate file as a usable world save: real SQLite (magic header), passes
    /// <c>PRAGMA quick_check</c>, and carries the game's schema (the <c>world_meta</c> table — the anchor
    /// every SqliteWorldRepository save has). Returns a player-safe error; internals stay in the logs.
    /// </summary>
    public static (bool Ok, string Error) ValidateUploadedSave(string path)
    {
        try
        {
            // SQLite magic: the 16 header bytes "SQLite format 3\0" — rejects arbitrary uploads cheaply
            // before SQLite parses anything.
            using (var fs = File.OpenRead(path))
            {
                var header = new byte[16];
                if (fs.Read(header, 0, 16) != 16
                    || !System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0").AsSpan().SequenceEqual(header))
                {
                    return (false, "This file is not a Blocks Beyond the Stars save (world.db).");
                }
            }

            using var db = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            db.Open();

            using (var check = db.CreateCommand())
            {
                check.CommandText = "PRAGMA quick_check;";
                if (check.ExecuteScalar() as string != "ok")
                {
                    return (false, "The save file is damaged (integrity check failed).");
                }
            }

            using (var schema = db.CreateCommand())
            {
                schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'world_meta'";
                if (Convert.ToInt32(schema.ExecuteScalar()) != 1)
                {
                    return (false, "This database is not a Blocks Beyond the Stars world save.");
                }
            }

            return (true, string.Empty);
        }
        catch (Exception)
        {
            return (false, "The save file could not be read.");
        }
        finally
        {
            SqliteConnection.ClearAllPools(); // release the read-only handle so the temp file can be moved/deleted
        }
    }
}
