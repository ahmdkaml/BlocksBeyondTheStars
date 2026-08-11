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

        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                Levels[i].Probability() >= Levels[i - 1].Probability(),
                $"{Levels[i]} should not have a lower probability than {Levels[i - 1]}.");
        }
    }

    [Fact]
    public void FloraFactor_IsMonotoneAndNormalIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Normal.FloraFactor());
        Assert.Equal(0.0, Frequency.Off.FloraFactor());

        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                Levels[i].FloraFactor() >= Levels[i - 1].FloraFactor(),
                $"{Levels[i]} should not have a lower flora factor than {Levels[i - 1]}.");
        }
    }

    [Fact]
    public void OreFactor_IsMonotoneAndRareIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Rare.OreFactor());

        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                Levels[i].OreFactor() >= Levels[i - 1].OreFactor(),
                $"{Levels[i]} should not have a lower ore factor than {Levels[i - 1]}.");
        }
    }

    [Fact]
    public void StructureFactor_IsMonotoneAndNormalIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Normal.StructureFactor());
        Assert.Equal(0.0, Frequency.Off.StructureFactor());

        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                Levels[i].StructureFactor() >= Levels[i - 1].StructureFactor(),
                $"{Levels[i]} should not have a lower structure factor than {Levels[i - 1]}.");
        }
    }

    [Fact]
    public void DangerFactor_IsMonotoneAndNormalIsUnchanged()
    {
        Assert.Equal(1.0, Frequency.Normal.DangerFactor());
        Assert.Equal(0.0, Frequency.Off.DangerFactor());

        for (var i = 1; i < Levels.Length; i++)
        {
            Assert.True(
                Levels[i].DangerFactor() >= Levels[i - 1].DangerFactor(),
                $"{Levels[i]} should not have a lower danger factor than {Levels[i - 1]}.");
        }
    }
}
