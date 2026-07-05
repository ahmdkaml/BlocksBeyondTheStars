// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text.Json;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>One row of <c>docker stats</c>: a container's CPU percentage and memory used/limit.</summary>
public sealed record ContainerStat(string Name, double CpuPercent, long MemUsedBytes, long MemLimitBytes);

/// <summary>Host utilization snapshot. Fields are null where the platform doesn't provide them
/// (no <c>/proc</c> on Windows dev boxes) — consumers render "n/a" instead of failing.</summary>
public sealed record HostUtilization(
    double? Load1, double? Load5, double? Load15, int Cores,
    long? MemTotalKb, long? MemAvailableKb,
    long? DiskTotalBytes, long? DiskFreeBytes);

/// <summary>
/// Host-utilization readings for the admin page. Inside a container <c>/proc/meminfo</c> and
/// <c>/proc/loadavg</c> report HOST-wide values (procfs is not namespaced for them), and the worlds
/// bind mount resolves to the host filesystem — so WorldHost can show real host numbers without any
/// extra agent or privilege. Parsers are pure statics so they are unit-testable with captured samples.
/// </summary>
public static class HostStats
{
    /// <summary>Reads the full snapshot; every part is best-effort (null on unsupported platforms).</summary>
    public static HostUtilization Read(string worldsDir)
    {
        var load = TryRead("/proc/loadavg") is { } loadText ? ParseLoadavg(loadText) : null;
        var mem = TryRead("/proc/meminfo") is { } memText ? ParseMeminfo(memText) : null;
        var disk = DiskFor(worldsDir);
        return new HostUtilization(
            load?.Load1, load?.Load5, load?.Load15, Environment.ProcessorCount,
            mem?.TotalKb, mem?.AvailableKb,
            disk?.TotalBytes, disk?.FreeBytes);
    }

    /// <summary>MemTotal/MemAvailable from <c>/proc/meminfo</c> content; null when either is missing.</summary>
    public static (long TotalKb, long AvailableKb)? ParseMeminfo(string text)
    {
        long? total = null, available = null;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                total = ParseMeminfoKb(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                available = ParseMeminfoKb(line);
            }
        }

        return total is { } t && available is { } a ? (t, a) : null;
    }

    private static long? ParseMeminfoKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long kb) ? kb : null;
    }

    /// <summary>The three load averages from <c>/proc/loadavg</c> ("0.42 0.30 0.19 1/123 456").</summary>
    public static (double Load1, double Load5, double Load15)? ParseLoadavg(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 3
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double l1)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double l5)
               && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double l15)
            ? (l1, l5, l15)
            : null;
    }

    /// <summary>Total/free bytes of the filesystem holding <paramref name="dir"/> — resolved via the
    /// longest matching mount point, because <c>DriveInfo(path)</c> only accepts drive roots. Pointing
    /// this at the worlds bind mount yields HOST disk numbers from inside the container.</summary>
    public static (long TotalBytes, long FreeBytes)? DiskFor(string dir)
    {
        try
        {
            string full = Path.GetFullPath(dir);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            DriveInfo? best = null;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                string root = drive.RootDirectory.FullName;
                if (full.StartsWith(root, comparison)
                    && (best is null || root.Length > best.RootDirectory.FullName.Length))
                {
                    best = drive;
                }
            }

            return best is null ? null : (best.TotalSize, best.AvailableFreeSpace);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Parses <c>docker stats --no-stream --format '{{json .}}'</c> output (one JSON object
    /// per line, e.g. <c>{"Name":"bbs-caddy","CPUPerc":"0.12%","MemUsage":"115.7MiB / 7.696GiB"}</c>).
    /// Unparseable lines are skipped — a half-broken docker answer degrades to fewer rows, not an error.</summary>
    public static IReadOnlyList<ContainerStat> ParseDockerStats(string output)
    {
        var result = new List<ContainerStat>();
        foreach (var line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                string name = root.GetProperty("Name").GetString() ?? string.Empty;
                string cpuText = root.GetProperty("CPUPerc").GetString() ?? string.Empty;
                string memText = root.GetProperty("MemUsage").GetString() ?? string.Empty;

                if (!double.TryParse(cpuText.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double cpu))
                {
                    continue;
                }

                var memParts = memText.Split('/', 2);
                if (memParts.Length != 2
                    || ParseSizeToBytes(memParts[0]) is not { } used
                    || ParseSizeToBytes(memParts[1]) is not { } limit)
                {
                    continue;
                }

                result.Add(new ContainerStat(name, cpu, used, limit));
            }
            catch (JsonException)
            {
                // not a stats row (warning line, partial write) — skip
            }
        }

        return result;
    }

    // docker (go-units) prints binary units for memory ("MiB") but decimal ones appear in other fields;
    // accept both. Longest suffixes first so "MiB" wins over "B".
    private static readonly (string Suffix, long Factor)[] SizeSuffixes =
    {
        ("KiB", 1024L), ("MiB", 1024L * 1024), ("GiB", 1024L * 1024 * 1024), ("TiB", 1024L * 1024 * 1024 * 1024),
        ("kB", 1000L), ("MB", 1000_000L), ("GB", 1000_000_000L), ("TB", 1000_000_000_000L),
        ("B", 1L),
    };

    /// <summary>"115.7MiB" → bytes; null when the value has no recognizable size suffix.</summary>
    public static long? ParseSizeToBytes(string value)
    {
        string trimmed = value.Trim();
        foreach (var (suffix, factor) in SizeSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                string number = trimmed.Substring(0, trimmed.Length - suffix.Length).Trim();
                return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
                    ? (long)(n * factor)
                    : null;
            }
        }

        return null;
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// TTL-cached JSON string with single-flight rebuild — the guard that makes a PUBLIC stats endpoint
/// safe: no matter how many requests arrive, the (comparatively expensive) snapshot builder runs at
/// most once per TTL window, and concurrent callers during a rebuild get the previous value instantly.
/// The clock is injectable so tests don't sleep.
/// </summary>
public sealed class CachedJson : IDisposable
{
    private sealed record Entry(string Json, long BuiltUnix);

    private readonly long _ttlSeconds;
    private readonly Func<Task<string>> _rebuild;
    private readonly Func<long> _nowUnix;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _entry;

    public CachedJson(TimeSpan ttl, Func<Task<string>> rebuild, Func<long>? nowUnix = null)
    {
        _ttlSeconds = Math.Max(1, (long)ttl.TotalSeconds);
        _rebuild = rebuild;
        _nowUnix = nowUnix ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public async Task<string> GetAsync()
    {
        var entry = _entry;
        if (entry != null && !Expired(entry))
        {
            return entry.Json;
        }

        if (await _gate.WaitAsync(0).ConfigureAwait(false))
        {
            try
            {
                return await RebuildLockedAsync().ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        // A rebuild is in flight. Serving the stale snapshot beats queueing up callers …
        if (entry != null)
        {
            return entry.Json;
        }

        // … but with no snapshot at all (first request storm), wait for the builder.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await RebuildLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> RebuildLockedAsync()
    {
        var entry = _entry;
        if (entry != null && !Expired(entry))
        {
            return entry.Json;
        }

        string built = await _rebuild().ConfigureAwait(false);
        _entry = new Entry(built, _nowUnix());
        return built;
    }

    private bool Expired(Entry entry) => _nowUnix() - entry.BuiltUnix >= _ttlSeconds;

    public void Dispose() => _gate.Dispose();
}
