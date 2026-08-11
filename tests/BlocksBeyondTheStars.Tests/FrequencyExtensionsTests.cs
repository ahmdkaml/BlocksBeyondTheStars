// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class FrequencyExtensionsTests
{
    private static readonly Frequency[] Levels =
    {
        Frequency.Off,
        Frequency.VeryRare,
        Frequency.Rare,
        Frequency.Normal,
        Frequency.Frequent
    };

    private static void AssertMonotone(
        Func<Frequency, double> selector,
        string? message = null)
    {
        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                selector(Levels[i]) >= selector(Levels[i - 1]),
                message ?? $"{Levels[i]} should not produce a lower value than {Levels[i - 1]}.");
        }
    }

    [Fact]
    public void Weight_IsMonotoneAndOffMeansZero()
    {
        Assert.Equal(0, Frequency.Off.Weight());

        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                Levels[i].Weight() >= Levels[i - 1].Weight(),
                $"{Levels[i]} should not have a lower weight than {Levels[i - 1]}.");
        }
    }

    [Fact]
    public void Probability_IsWithinValidRange()
    {
        foreach (var level in Levels)
        {
            Assert.InRange(level.Probability(), 0.0, 1.0);
        }
    }

    [Fact]
    public void Probability_IsMonotoneAndOffMeansZero()
    {
        Assert.Equal(0.0, Frequency.Off.Probability());

        AssertMonotone(f => f.Probability());
    }

    [Fact]
    public void FloraFactor_IsMonotoneAndNormalIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Normal.FloraFactor());
        Assert.Equal(0.0, Frequency.Off.FloraFactor());

        AssertMonotone(f => f.FloraFactor());
    }

    [Fact]
    public void OreFactor_IsMonotoneAndRareIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Rare.OreFactor());

        AssertMonotone(f => f.OreFactor());
    }

    [Fact]
    public void StructureFactor_IsMonotoneAndNormalIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Normal.StructureFactor());
        Assert.Equal(0.0, Frequency.Off.StructureFactor());

        AssertMonotone(f => f.StructureFactor());
    }

    [Fact]
    public void DangerFactor_IsMonotoneAndNormalIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Normal.DangerFactor());
        Assert.Equal(0.0, Frequency.Off.DangerFactor());

        AssertMonotone(f => f.DangerFactor());
    }
}
