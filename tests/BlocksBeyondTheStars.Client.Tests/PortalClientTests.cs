// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Portal;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Response parsing of the worlds-portal client (Official Worlds menu): the exact JSON shapes the
/// WorldHost API emits, plus the error paths (server error body, unauthorized, offline).
/// </summary>
public sealed class PortalClientTests
{
    [Fact]
    public void ParseLogin_ReadsSessionAndTermsFlag()
    {
        var ok = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"termsOutdated\":false}");
        Assert.True(ok.Ok);
        Assert.Equal("acc-1", ok.AccountId);
        Assert.Equal("tok", ok.SessionToken);
        Assert.False(ok.TermsOutdated);

        var outdated = PortalClient.ParseLogin(200, "{\"accountId\":\"acc-1\",\"sessionToken\":\"tok\",\"termsOutdated\":true}");
        Assert.True(outdated.Ok);
        Assert.True(outdated.TermsOutdated);
    }

    [Fact]
    public void ParseLogin_FailurePaths()
    {
        Assert.Equal("unauthorized", PortalClient.ParseLogin(401, "").Error);
        Assert.Equal("offline", PortalClient.ParseLogin(0, "").Error);
        Assert.Equal("http_502", PortalClient.ParseLogin(502, "<html>bad gateway</html>").Error); // non-JSON proxy page
        Assert.False(PortalClient.ParseLogin(401, "").Ok);
    }

    [Fact]
    public void ParseWorlds_ReadsTheList()
    {
        var r = PortalClient.ParseWorlds(200,
            "{\"worlds\":[{\"id\":\"aabbccddee11\",\"name\":\"My World\",\"status\":\"stopped\",\"subdomain\":\"w-aabbccddee11\"}]}");
        Assert.True(r.Ok);
        var world = Assert.Single(r.Worlds);
        Assert.Equal("aabbccddee11", world.Id);
        Assert.Equal("My World", world.Name);
        Assert.Equal("stopped", world.Status);
    }

    [Fact]
    public void ParseJoin_ReadsTheGrant_AndSurfacesPlayerSafeErrors()
    {
        var r = PortalClient.ParseJoin(200,
            "{\"worldId\":\"aabbccddee11\",\"displayName\":\"My World\",\"wssUrl\":\"wss://w-aabbccddee11.play.example.de\"," +
            "\"nativeHost\":\"play.example.de\",\"nativePort\":32000,\"joinToken\":\"v1.a.b.1.C\",\"tokenExpiresUnix\":1}");
        Assert.True(r.Ok);
        Assert.Equal("play.example.de", r.NativeHost);
        Assert.Equal(32000, r.NativePort);
        Assert.Equal("v1.a.b.1.C", r.JoinToken);
        Assert.StartsWith("wss://", r.WssUrl, System.StringComparison.Ordinal);

        // The wake-failed path: WorldHost answers 503 with a player-safe error text.
        var failed = PortalClient.ParseJoin(503, "{\"error\":\"The world did not come up in time — please try again.\"}");
        Assert.False(failed.Ok);
        Assert.Contains("did not come up", failed.Error, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ParseSimple_CoversReportOutcomes()
    {
        Assert.True(PortalClient.ParseSimple(200, "").Ok);
        var bad = PortalClient.ParseSimple(400, "{\"error\":\"Unknown report category.\"}");
        Assert.False(bad.Ok);
        Assert.Equal("Unknown report category.", bad.Error);
    }
}
