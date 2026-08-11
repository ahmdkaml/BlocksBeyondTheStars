// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class ChunkCoordTests
{
    [Theory]
    [InlineData(1, 2, 3, 4, 6, 3, 25)]
    [InlineData(0, 0, 0, 10, 20, 30, 1400)]
    [InlineData(-1, -2, -3, 2, 4, 6, 126)]
    public void DistanceSquared_IsSymmetricAndCorrect(
        int ax, int ay, int az,
        int bx, int by, int bz,
        int expected)
    {
        var a = new ChunkCoord(ax, ay, az);
        var b = new ChunkCoord(bx, by, bz);

        Assert.Equal(expected, a.DistanceSquared(b));
        Assert.Equal(a.DistanceSquared(b), b.DistanceSquared(a));
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(-5, 10, -20)]
    public void EqualCoordinates_AreEqualAndHaveSameHashCode(
        int x, int y, int z)
    {
        var a = new ChunkCoord(x, y, z);
        var b = new ChunkCoord(x, y, z);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 2, 4)]
    [InlineData(0, 0, 0, 1, 0, 0)]
    [InlineData(-5, 10, -20, -5, 11, -20)]
    public void DifferentCoordinates_AreNotEqual(
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        var a = new ChunkCoord(ax, ay, az);
        var b = new ChunkCoord(bx, by, bz);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_ReturnsFalseForNull()
    {
        var coord = new ChunkCoord(1, 2, 3);

        Assert.False(coord.Equals(null));
    }

    [Fact]
    public void Equals_ReturnsFalseForDifferentType()
    {
        var coord = new ChunkCoord(1, 2, 3);

        Assert.False(coord.Equals("not a chunk coordinate"));
    }

    [Theory]
    [InlineData(1, 2, 3, "Chunk(1, 2, 3)")]
    [InlineData(-5, 0, 10, "Chunk(-5, 0, 10)")]
    public void ToString_FormatsCoordinates(
        int x, int y, int z, string expected)
    {
        Assert.Equal(expected, new ChunkCoord(x, y, z).ToString());
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 2, 3)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void EqualityOperator_ReturnsTrueForEqualCoordinates(
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        var a = new ChunkCoord(ax, ay, az);
        var b = new ChunkCoord(bx, by, bz);

        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 2, 4)]
    [InlineData(0, 0, 0, 1, 0, 0)]
    public void EqualityOperator_ReturnsFalseForDifferentCoordinates(
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        var a = new ChunkCoord(ax, ay, az);
        var b = new ChunkCoord(bx, by, bz);

        Assert.False(a == b);
        Assert.True(a != b);
    }
}
