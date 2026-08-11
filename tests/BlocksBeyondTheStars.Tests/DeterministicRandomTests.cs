// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameUInt64Sequence()
    {
        var first = new DeterministicRandom(12345);
        var second = new DeterministicRandom(12345);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(first.NextUInt64(), second.NextUInt64());
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentUInt64Sequences()
    {
        var first = new DeterministicRandom(12345);
        var second = new DeterministicRandom(54321);

        var firstSequence = Enumerable.Range(0, 10)
            .Select(_ => first.NextUInt64())
            .ToArray();

        var secondSequence = Enumerable.Range(0, 10)
            .Select(_ => second.NextUInt64())
            .ToArray();

        Assert.NotEqual(firstSequence, secondSequence);
    }

    [Fact]
    public void NextDouble_AlwaysReturnsValueInZeroToOneRange()
    {
        var random = new DeterministicRandom(12345);

        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextDouble();

            Assert.True(value >= 0.0 && value < 1.0);
        }
    }

    [Fact]
    public void NextFloat_AlwaysReturnsValueInZeroToOneRange()
    {
        var random = new DeterministicRandom(12345);

        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextFloat();

            Assert.True(value >= 0.0f && value < 1.0f);
        }
    }

    [Fact]
    public void Range_ReturnsValueWithinInclusiveBounds()
    {
        var random = new DeterministicRandom(12345);

        for (var i = 0; i < 1000; i++)
        {
            Assert.InRange(random.Range(10, 20), 10, 20);
        }
    }

    [Fact]
    public void Range_WhenBoundsAreEqual_ReturnsThatValue()
    {
        var random = new DeterministicRandom(12345);

        Assert.Equal(10, random.Range(10, 10));
    }

    [Fact]
    public void Range_WhenMaximumIsLessThanMinimum_ReturnsMinimum()
    {
        var random = new DeterministicRandom(12345);

        Assert.Equal(10, random.Range(10, 5));
    }

    [Fact]
    public void ZeroSeed_ProducesNonZeroSequence()
    {
        var random = new DeterministicRandom(0);

        for (var i = 0; i < 10; i++)
        {
            Assert.NotEqual(0UL, random.NextUInt64());
        }
    }
}
