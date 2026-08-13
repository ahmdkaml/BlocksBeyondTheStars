// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class BlockIdTests
{
    [Fact]
    public void EqualIds_AreEqualAndHaveEqualHashCodes()
    {
        var first = new BlockId(42);
        var second = new BlockId(42);

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void DifferentIds_AreNotEqual()
    {
        var first = new BlockId(42);
        var second = new BlockId(43);

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Fact]
    public void Air_HasZeroValueAndIsAir()
    {
        Assert.Equal(BlockId.AirValue, BlockId.Air.Value);
        Assert.True(BlockId.Air.IsAir);
        Assert.True(new BlockId(BlockId.AirValue).IsAir);
        Assert.False(new BlockId(1).IsAir);
    }

}
