// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class LocalizerTests
{
    private static Localizer CreateLocalizer(
        Dictionary<string, string>? active = null,
        Dictionary<string, string>? fallback = null)
    {
        return new Localizer(
            GameLocale.English,
            active ?? new Dictionary<string, string>(),
            fallback ?? new Dictionary<string, string>());
    }

    [Fact]
    public void Get_ReturnsBracketedKeyWhenUnknown()
    {
        var localizer = CreateLocalizer();

        Assert.Equal("[missing.key]", localizer.Get("missing.key"));
    }

    [Fact]
    public void Get_ReturnsEmptyStringForEmptyKey()
    {
        var localizer = CreateLocalizer();

        Assert.Equal(string.Empty, localizer.Get(string.Empty));
    }

    [Fact]
    public void Has_ReturnsTrueWhenKeyExistsOnlyInFallback()
    {
        var localizer = CreateLocalizer(
            fallback: new Dictionary<string, string>
            {
                ["fallback.key"] = "English text",
            });

        Assert.True(localizer.Has("fallback.key"));
    }

    [Fact]
    public void Has_ReturnsFalseWhenKeyDoesNotExistInEitherTable()
    {
        var localizer = CreateLocalizer();

        Assert.False(localizer.Has("missing.key"));
    }
}
