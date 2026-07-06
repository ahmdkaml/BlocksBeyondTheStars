// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Start-planet selection: a new world begins on a hospitable surface (breathable air, food plants,
/// the common ores) and the chosen start body always agrees with the terrain that actually generates —
/// even when the configured type is missing from the galaxy or misspelled.
/// </summary>
public sealed class StartPlanetTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public StartPlanetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_startplanet_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(ServerConfig config, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, config.WorldName));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void NewWorld_DefaultsToABreathableStartPlanet()
    {
        // The out-of-the-box start experience: air to breathe (no suit-oxygen drain on the surface),
        // plants that can grow (food loop) — not the old toxic "rocky" survival-pressure start.
        var config = new ServerConfig { WorldName = "sp_default", Seed = 21, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = Started(config, out var repo);
        using (repo)
        {
            Assert.Equal("varied", server.Metadata.DefaultPlanetType);
            Assert.True(server.AtmosphereBreathable);

            var def = _content.GetPlanet(server.Metadata.DefaultPlanetType)!;
            Assert.True(def.FloraDensity > 0, "the default start planet must grow flora (food)");

            // Map/terrain consistency: the resolved start body carries the same type the surface uses.
            var body = server.Galaxy.FindBody(server.Metadata.ActiveLocationId);
            Assert.NotNull(body);
            Assert.Equal("varied", body!.PlanetType);
        }
    }

    [Fact]
    public void ConfiguredType_AbsentFromGalaxy_RetypesTheStartBody()
    {
        // A galaxy of only ice worlds, but the start planet is configured "varied": the terrain is
        // always generated from the configured type, so the chosen start body must be RETYPED to it —
        // otherwise travelling away and back would regenerate the same body as a different world.
        var config = new ServerConfig { WorldName = "sp_retype", Seed = 22, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.World.PlanetTypeFrequencies["ice"] = Frequency.Normal;
        var server = Started(config, out var repo);
        using (repo)
        {
            Assert.Equal("varied", server.Metadata.DefaultPlanetType);
            var body = server.Galaxy.FindBody(server.Metadata.ActiveLocationId);
            Assert.NotNull(body);
            Assert.Equal("varied", body!.PlanetType);
            Assert.True(server.AtmosphereBreathable);
        }
    }

    [Fact]
    public void UnknownStartPlanet_FallsBackToABreathableFloraWorld()
    {
        // A typo'd --start-planet used to crash LoadWorld ("Unknown planet type"). Now the server
        // adopts the first breathable planet that grows flora, keeping the start criteria intact.
        var config = new ServerConfig { WorldName = "sp_unknown", Seed = 23, StartPlanet = "no_such_type", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = Started(config, out var repo);
        using (repo)
        {
            var def = _content.GetPlanet(server.Metadata.DefaultPlanetType);
            Assert.NotNull(def);
            Assert.Equal("breathable", def!.Atmosphere, ignoreCase: true);
            Assert.True(def.FloraDensity > 0);
            Assert.True(server.AtmosphereBreathable);

            var body = server.Galaxy.FindBody(server.Metadata.ActiveLocationId);
            Assert.NotNull(body);
            Assert.Equal(server.Metadata.DefaultPlanetType, body!.PlanetType);
        }
    }

    [Fact]
    public void VariedStartPlanet_CarriesTheEarlyGameOres()
    {
        // The start planet must supply the early crafting chain: iron/copper (tools, plates), silicate
        // (glass, cables) and carbon (medpack, carbon composite → energy cells) without leaving the planet.
        var def = _content.GetPlanet("varied")!;
        foreach (var needed in new[] { "iron_ore", "copper_ore", "silicate", "carbon" })
        {
            Assert.Contains(def.Ores, o => o.Block == needed);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
