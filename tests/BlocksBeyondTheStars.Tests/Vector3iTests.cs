using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class Vector3iTests
{
    [Fact]
    public void Zero_HasAllCoordinatesEqualToZero()
    {
        Assert.Equal(new Vector3i(0, 0, 0), Vector3i.Zero);
    }

    [Fact]
    public void One_HasAllCoordinatesEqualToOne()
    {
        Assert.Equal(new Vector3i(1, 1, 1), Vector3i.One);
    }

    [Theory]
    [InlineData(1, 2, 3, 4, 5, 6, 5, 7, 9)]
    [InlineData(10, 20, 30, 1, 2, 3, 11, 22, 33)]
    [InlineData(-5, 10, -20, 2, -3, 4, -3, 7, -16)]
    public void Addition_IsComponentWise(
        int ax, int ay, int az,
        int bx, int by, int bz,
        int expectedX, int expectedY, int expectedZ)
    {
        var result = new Vector3i(ax, ay, az) + new Vector3i(bx, by, bz);

        Assert.Equal(new Vector3i(expectedX, expectedY, expectedZ), result);
    }

    [Theory]
    [InlineData(10, 20, 30, 1, 2, 3, 9, 18, 27)]
    [InlineData(1, 2, 3, 4, 5, 6, -3, -3, -3)]
    [InlineData(-5, 10, -20, 2, -3, 4, -7, 13, -24)]
    public void Subtraction_IsComponentWise(
        int ax, int ay, int az,
        int bx, int by, int bz,
        int expectedX, int expectedY, int expectedZ)
    {
        var result = new Vector3i(ax, ay, az) - new Vector3i(bx, by, bz);

        Assert.Equal(new Vector3i(expectedX, expectedY, expectedZ), result);
    }

    [Theory]
    [InlineData(1, 2, 3, 2, 2, 4, 6)]
    [InlineData(-5, 10, -20, 3, -15, 30, -60)]
    [InlineData(0, 0, 0, 100, 0, 0, 0)]
    public void Multiplication_ScalesEveryComponent(
        int x, int y, int z,
        int scalar,
        int expectedX, int expectedY, int expectedZ)
    {
        var result = new Vector3i(x, y, z) * scalar;

        Assert.Equal(new Vector3i(expectedX, expectedY, expectedZ), result);
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 2, 3)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void EqualityOperator_ReturnsTrueForEqualVectors(
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        var a = new Vector3i(ax, ay, az);
        var b = new Vector3i(bx, by, bz);

        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 2, 4)]
    [InlineData(0, 0, 0, 1, 0, 0)]
    public void EqualityOperator_ReturnsFalseForDifferentVectors(
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        var a = new Vector3i(ax, ay, az);
        var b = new Vector3i(bx, by, bz);

        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Theory]
    [InlineData(1, 2, 3, 4, 6, 3, 25)]
    [InlineData(0, 0, 0, 10, 20, 30, 1400)]
    [InlineData(-1, -2, -3, 2, 4, 6, 126)]
    public void DistanceSquared_IsSymmetricAndCorrect(
        int ax, int ay, int az,
        int bx, int by, int bz,
        int expected)
    {
        var a = new Vector3i(ax, ay, az);
        var b = new Vector3i(bx, by, bz);

        Assert.Equal(expected, a.DistanceSquared(b));
        Assert.Equal(a.DistanceSquared(b), b.DistanceSquared(a));
    }

    [Theory]
    [InlineData(1, 2, 3)]
    [InlineData(0, 0, 0)]
    [InlineData(-5, 10, -20)]
    public void EqualVectors_AreEqualAndHaveSameHashCode(int x, int y, int z)
    {
        var a = new Vector3i(x, y, z);
        var b = new Vector3i(x, y, z);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [InlineData(1, 2, 3, 1, 2, 4)]
    [InlineData(0, 0, 0, 1, 0, 0)]
    [InlineData(-5, 10, -20, -5, 11, -20)]
    public void DifferentVectors_AreNotEqual(
        int ax, int ay, int az,
        int bx, int by, int bz)
    {
        var a = new Vector3i(ax, ay, az);
        var b = new Vector3i(bx, by, bz);

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(1, 2, 3, "(1, 2, 3)")]
    [InlineData(-5, 0, 10, "(-5, 0, 10)")]
    public void ToString_FormatsCoordinates(
        int x, int y, int z, string expected)
    {
        Assert.Equal(expected, new Vector3i(x, y, z).ToString());
    }
}
