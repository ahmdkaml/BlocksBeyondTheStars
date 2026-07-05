// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.WorldHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// WorldHost portal pages: full DE/EN localization (German default, ?lang=en — issue #253), the
/// Play-button browser deep-link + the join-grant rendering order that made Play look like a no-op
/// (issue #252), the game-logo branding (issue #254), and the /play WebGL serving policy helpers.
/// </summary>
public sealed class WorldHostPortalPagesTests
{
    private static readonly WorldHostConfig Config = new();

    // ---------------- Localization (#253) ----------------

    [Theory]
    [InlineData(null, "de")]
    [InlineData("", "de")]
    [InlineData("de", "de")]
    [InlineData("en", "en")]
    [InlineData("fr", "de")] // anything unknown falls back to the German default
    [InlineData("EN", "de")] // deliberate exact match — no case folding surprises
    public void NormalizeLang_DefaultsToGerman(string? input, string expected)
        => Assert.Equal(expected, WorldHostPortalPages.NormalizeLang(input));

    [Fact]
    public void Landing_German_HasNoMixedEnglish()
    {
        string html = WorldHostPortalPages.Landing(Config);
        Assert.Contains("lang='de'", html);
        Assert.Contains("Konto erstellen", html);
        Assert.Contains("Anmelden", html);
        Assert.DoesNotContain("Create account", html);
        Assert.DoesNotContain("Sign in", html);
    }

    [Fact]
    public void Landing_English_HasNoMixedGerman()
    {
        string html = WorldHostPortalPages.Landing(Config, "en");
        Assert.Contains("lang='en'", html);
        Assert.Contains("Create account", html);
        Assert.Contains("Sign in", html);
        Assert.DoesNotContain("Konto erstellen", html);
        Assert.DoesNotContain("Anmelden", html);
        // JS navigations must keep the explicit language choice.
        Assert.Contains("const LQ = '?lang=en'", html);
    }

    [Fact]
    public void Worlds_IsFullyLocalized_PerLanguage()
    {
        string de = WorldHostPortalPages.Worlds(Config);
        Assert.Contains("Neue Welt", de);
        Assert.Contains("Spieler melden", de);
        Assert.DoesNotContain("New world", de);
        Assert.DoesNotContain("Report a player", de);
        Assert.Contains("Welt wird gestartet…", de); // JS strings localize too (injected L map)

        string en = WorldHostPortalPages.Worlds(Config, "en");
        Assert.Contains("New world", en);
        Assert.Contains("Report a player", en);
        Assert.DoesNotContain("Neue Welt", en);
        Assert.Contains("Waking the world…", en);
    }

    [Fact]
    public void Rules_ShowsOnlyTheSelectedLanguage()
    {
        string de = WorldHostPortalPages.Rules(Config);
        Assert.Contains("Sei freundlich", de);
        Assert.DoesNotContain("Be friendly", de);

        string en = WorldHostPortalPages.Rules(Config, "en");
        Assert.Contains("Be friendly", en);
        Assert.DoesNotContain("Sei freundlich", en);
    }

    [Fact]
    public void Shell_CarriesTheLanguageSwitcher_AndErrorLanguageFlag()
    {
        string de = WorldHostPortalPages.Landing(Config);
        Assert.Contains("?lang=en'>English</a>", de);
        Assert.Contains("var de = true;", de);

        string en = WorldHostPortalPages.Landing(Config, "en");
        Assert.Contains("?lang=de'>Deutsch</a>", en);
        Assert.Contains("var de = false;", en);
    }

    [Fact]
    public void Privacy_EnglishPutsTheSummaryFirst_GermanTextStaysAuthoritative()
    {
        string en = WorldHostPortalPages.Privacy(Config, "en");
        Assert.True(en.IndexOf("English summary", StringComparison.Ordinal)
            < en.IndexOf("Verantwortlicher", StringComparison.Ordinal));

        string de = WorldHostPortalPages.Privacy(Config);
        Assert.True(de.IndexOf("Verantwortlicher", StringComparison.Ordinal)
            < de.IndexOf("English summary", StringComparison.Ordinal));
    }

    // ---------------- Play button: deep-link + grant rendering order (#252) ----------------

    [Fact]
    public void Worlds_PlayButton_DeepLinksIntoTheBrowserClient()
    {
        string html = WorldHostPortalPages.Worlds(Config);
        Assert.Contains("/play/?auto_join=1", html);
        Assert.Contains("hosted_token=", html);
        Assert.Contains("world_id=", html);
        Assert.Contains("server_host=", html);
        Assert.Contains("class='playnow'", html);
    }

    [Fact]
    public void Worlds_JoinFlow_RefreshesTheListBeforeRenderingTheGrant()
    {
        // Regression (#252): joinWorld once rendered the grant info and THEN called load(), which
        // rebuilds every card with an empty grant div — wiping the info milliseconds after it appeared.
        string html = WorldHostPortalPages.Worlds(Config);
        int refresh = html.IndexOf("await load();", StringComparison.Ordinal);
        int grant = html.IndexOf("getElementById('g-'+id).innerHTML", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && grant >= 0 && refresh < grant,
            "joinWorld() must await load() BEFORE rendering the grant block");
    }

    [Fact]
    public void Worlds_StatusMessages_RenderAboveTheFold()
    {
        // The #msg div (progress + errors) must come before the world list — at the old bottom-of-page
        // position, "Waking the world…" and join errors were invisible without scrolling.
        string html = WorldHostPortalPages.Worlds(Config);
        Assert.True(html.IndexOf("id='msg'", StringComparison.Ordinal)
            < html.IndexOf("id='list'", StringComparison.Ordinal));
    }

    // ---------------- Branding (#254) ----------------

    [Fact]
    public void Shell_ShowsTheGameLogo_AndTheWebsiteFavicon()
    {
        string html = WorldHostPortalPages.Landing(Config);
        Assert.Contains("href='/favicon.ico'", html);
        Assert.Contains("class='brand'", html);
        Assert.Contains("<b>Blocks</b> Beyond the Stars", html);
        Assert.Contains("<svg class='mark'", html);
    }

    [Fact]
    public void Favicon_IsAValidEmbeddedIco()
    {
        // .ico magic: reserved 0x0000, type 0x0001, then a nonzero image count.
        byte[] ico = PortalFavicon.Bytes;
        Assert.True(ico.Length > 1000);
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        Assert.True(BitConverter.ToUInt16(ico, 4) >= 1);
    }

    // ---------------- /play WebGL serving policy ----------------

    [Fact]
    public void PlayPage_StampsAssetUrls_WithTheNewestBuildTimestamp()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_play_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Build"));
            File.WriteAllText(Path.Combine(root, "index.html"), "<html>var buildStamp = \"\";</html>");
            File.WriteAllText(Path.Combine(root, "Build", "WebGL.wasm.br"), "x");

            string? html = PlayPage.StampIndexHtml(root);
            Assert.NotNull(html);
            Assert.Contains("var buildStamp = \"?v=", html);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PlayPage_WithoutABuild_ServesALocalizedFriendlyPage()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_play_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(PlayPage.StampIndexHtml(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Assert.Contains("noch nicht installiert", PlayPage.NotInstalledHtml("de"));
        Assert.Contains("not installed", PlayPage.NotInstalledHtml("en"));
        Assert.Contains("noch nicht installiert", PlayPage.NotInstalledHtml("whatever")); // German default
    }

    [Theory]
    [InlineData("WebGL.wasm.br", "br", "application/wasm")]
    [InlineData("WebGL.framework.js.br", "br", "application/javascript")]
    [InlineData("WebGL.data.br", "br", "application/octet-stream")]
    [InlineData("WebGL.data.gz", "gzip", null)]
    [InlineData("WebGL.wasm", null, null)]
    public void PlayPage_AnnouncesUnityPrecompressedEncodings(string file, string? encoding, string? contentType)
    {
        var (enc, type) = PlayPage.EncodingFor(file);
        Assert.Equal(encoding, enc);
        Assert.Equal(contentType, type);
    }

    [Fact]
    public void PlayPage_OnlyVersionStampedAssets_MayCacheLongTerm()
    {
        // Unity's build file names are stable, not content-addressed — blanket immutable caching once
        // mixed old/new wasm+data pairs across rebuilds and crashed the engine.
        Assert.Equal("public, max-age=31536000, immutable", PlayPage.CacheControlFor("WebGL.wasm.br", hasVersionQuery: true));
        Assert.Equal("no-cache", PlayPage.CacheControlFor("WebGL.wasm.br", hasVersionQuery: false));
        Assert.Equal("no-cache", PlayPage.CacheControlFor("index.html", hasVersionQuery: true));
    }
}
