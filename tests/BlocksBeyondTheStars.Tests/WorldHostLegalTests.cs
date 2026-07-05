// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Legal / account-lifecycle additions: DSGVO account self-deletion (cascade over sessions, reports and
/// worlds incl. on-disk saves) and the legal-config plumbing that drives the Impressum/Datenschutz pages.
/// </summary>
public sealed class WorldHostLegalTests : IDisposable
{
    private readonly string _root;
    private readonly List<HostRegistry> _registries = new();

    public WorldHostLegalTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_whl_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    private HostRegistry NewRegistry(WorldHostConfig config)
    {
        var registry = new HostRegistry(config, System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db"));
        _registries.Add(registry);
        return registry;
    }

    [Fact]
    public void DeleteAccount_RemovesAccount_Sessions_AndReports()
    {
        var config = new WorldHostConfig();
        var registry = NewRegistry(config);

        var (_, _, accountId, session) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var (_, _, otherId, _) = registry.CreateAccount("Someone", "super-secret-1", acceptedTermsVersion: 1);
        registry.CreateReport(accountId, "w1", "Meanie", "chat", "was mean");
        registry.CreateReport(otherId, "w1", "Owner", "chat", "counter-report");

        registry.DeleteAccount(accountId);

        Assert.Null(registry.ResolveSession(session));                 // session gone
        Assert.Null(registry.Login("Owner", "super-secret-1"));        // account gone (name reusable)
        var open = registry.ListOpenReports();
        Assert.Single(open);                                           // only the OTHER account's report survives
        Assert.Equal("Owner", open[0].ReportedName);
    }

    [Fact]
    public void DeleteWorldData_ErasesLiveAndArchivedSaves()
    {
        var config = new WorldHostConfig { WorldsDir = System.IO.Path.Combine(_root, "worlds") };
        var registry = NewRegistry(config);
        var (_, _, accountId, _) = registry.CreateAccount("Owner", "super-secret-1", acceptedTermsVersion: 1);
        var world = registry.CreateWorld(accountId, "My World").World!;

        // Simulate both a live save and an archived copy on disk.
        foreach (var dir in new[] { SavePaths.HostSavesDir(config, world.Id), SavePaths.ArchivedSavesDir(config, world.Id) })
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "world.db"), "bytes");
        }

        SavePaths.DeleteWorldData(config, world.Id);

        Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(config.WorldsDir, world.Id)));
        Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(config.WorldsDir, "_archive", world.Id)));
    }

    [Fact]
    public void LegalConfig_LoadsFromEnvironment()
    {
        // The Impressum/Datenschutz pages read these operator-set values; verify the env plumbing.
        Environment.SetEnvironmentVariable("BBS_WH_LEGAL_NAME", "Marcel Dütscher");
        Environment.SetEnvironmentVariable("BBS_WH_LEGAL_ADDRESS", "Bresslauer Str. 20, 54295 Trier");
        Environment.SetEnvironmentVariable("BBS_WH_LEGAL_EMAIL", "info@blocksbeyondthestars.de");
        try
        {
            var c = WorldHostConfig.FromEnvironment();
            Assert.Equal("Marcel Dütscher", c.LegalName);
            Assert.Equal("Bresslauer Str. 20, 54295 Trier", c.LegalAddress);
            Assert.Equal("info@blocksbeyondthestars.de", c.LegalEmail);

            // The pages render without throwing and carry the configured data / no placeholder.
            // (Non-ASCII like "ü" is HTML-encoded, so assert on ASCII-safe tokens.)
            string impressum = WorldHostPortalPages.Impressum(c);
            Assert.Contains("Marcel", impressum, StringComparison.Ordinal);
            Assert.Contains("Bresslauer Str. 20", impressum, StringComparison.Ordinal);
            Assert.Contains("info@blocksbeyondthestars.de", impressum, StringComparison.Ordinal);
            Assert.DoesNotContain("noch nicht", impressum, StringComparison.Ordinal); // no unconfigured notice
            Assert.Contains("§ 5 DDG", impressum, StringComparison.Ordinal);

            string privacy = WorldHostPortalPages.Privacy(c);
            Assert.Contains("Datenschutz", privacy, StringComparison.Ordinal);
            Assert.Contains("kein Tracking", privacy, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BBS_WH_LEGAL_NAME", null);
            Environment.SetEnvironmentVariable("BBS_WH_LEGAL_ADDRESS", null);
            Environment.SetEnvironmentVariable("BBS_WH_LEGAL_EMAIL", null);
        }
    }

    [Fact]
    public void Impressum_UnconfiguredOperator_ShowsNotice_NotWrongData()
    {
        // A self-hosted WorldHost with no legal config must NOT serve the project authors' identity.
        string impressum = WorldHostPortalPages.Impressum(new WorldHostConfig());
        Assert.Contains("noch nicht", impressum, StringComparison.Ordinal); // the "not configured" notice
        Assert.DoesNotContain("Breslauer", impressum, StringComparison.Ordinal);
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
