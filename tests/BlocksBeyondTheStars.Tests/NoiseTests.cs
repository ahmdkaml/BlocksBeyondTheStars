// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class NoiseTests
{
    [Fact]
    public void Hash_IsDeterministic()
    {
        var first = Noise.Hash(12345, 10, 20, 30);
        var second = Noise.Hash(12345, 10, 20, 30);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Value01_IsDeterministicAndInRange()
    {
        var first = Noise.Value01(12345, 10, 20, 30);
        var second = Noise.Value01(12345, 10, 20, 30);

        Assert.Equal(first, second);
        Assert.True(first >= 0.0 && first < 1.0);
    }

    [Fact]
    public void Value2D_IsDeterministicAndInRange()
    {
        var first = Noise.Value2D(12345, 10.25, 20.75);
        var second = Noise.Value2D(12345, 10.25, 20.75);

        Assert.Equal(first, second);
        Assert.True(first >= 0.0 && first < 1.0);
    }

    [Fact]
    public void Value3D_IsDeterministicAndInRange()
    {
        var first = Noise.Value3D(12345, 10.25, 20.5, 30.75);
        var second = Noise.Value3D(12345, 10.25, 20.5, 30.75);

        Assert.Equal(first, second);
        Assert.True(first >= 0.0 && first < 1.0);
    }

    [Fact]
    public void Fbm2D_IsDeterministicAndInRange()
    {
        var first = Noise.Fbm2D(12345, 10.25, 20.75, 4);
        var second = Noise.Fbm2D(12345, 10.25, 20.75, 4);

        Assert.Equal(first, second);
        Assert.True(first >= 0.0 && first < 1.0);
    }

    [Fact]
    public void Value4D_IsDeterministicAndInRange()
    {
        var first = Noise.Value4D(12345, 10.25, 20.5, 30.75, 40.125);
        var second = Noise.Value4D(12345, 10.25, 20.5, 30.75, 40.125);

        Assert.Equal(first, second);
        Assert.True(first >= 0.0 && first < 1.0);
    }

    [Fact]
    public void FbmCylX_IsPeriodicAcrossCircumference()
    {
        const double circumference = 100.0;

        var atStart = Noise.FbmCylX(12345, 0.0, 20.0,
            circumference, 1.0, 4);

        var atEnd = Noise.FbmCylX(12345, circumference, 20.0,
            circumference, 1.0, 4);

        Assert.Equal(atStart, atEnd, precision: 12);
    }

    [Fact]
    public void ValueCylX_IsPeriodicAcrossCircumference()
    {
        const double circumference = 100.0;

        var atStart = Noise.ValueCylX(12345, 0.0, 20.0, 30.0,
            circumference, 1.0, 1.0, 1.0);

        var atEnd = Noise.ValueCylX(12345, circumference, 20.0, 30.0,
            circumference, 1.0, 1.0, 1.0);

        Assert.Equal(atStart, atEnd, precision: 12);
    }

    [Fact]
    public void Value5D_IsDeterministicAndInRange()
    {
        var first = Noise.Value5D(12345, 10.25, 20.5, 30.75,
            40.125, 50.625);

        var second = Noise.Value5D(12345, 10.25, 20.5, 30.75,
            40.125, 50.625);

        Assert.Equal(first, second);
        Assert.True(first >= 0.0 && first < 1.0);
    }

    [Fact]
    public void FbmTorus_IsPeriodicAcrossBothCircumferences()
    {
        const double circumferenceX = 100.0;
        const double circumferenceZ = 80.0;

        var origin = Noise.FbmTorus(12345, 10.0, 20.0,
            circumferenceX, circumferenceZ, 1.0, 4);

        var wrappedX = Noise.FbmTorus(12345, 10.0 + circumferenceX,
            20.0, circumferenceX, circumferenceZ, 1.0, 4);

        var wrappedZ = Noise.FbmTorus(12345, 10.0,
            20.0 + circumferenceZ, circumferenceX,
            circumferenceZ, 1.0, 4);

        Assert.Equal(origin, wrappedX, precision: 12);
        Assert.Equal(origin, wrappedZ, precision: 12);
    }

    [Fact]
    public void ValueTorus_IsPeriodicAcrossBothCircumferences()
    {
        const double circumferenceX = 100.0;
        const double circumferenceZ = 80.0;

        var origin = Noise.ValueTorus(12345, 10.0, 20.0, 30.0,
            circumferenceX, circumferenceZ, 1.0, 1.0, 1.0);

        var wrappedX = Noise.ValueTorus(12345, 10.0 + circumferenceX,
            20.0, 30.0, circumferenceX, circumferenceZ,
            1.0, 1.0, 1.0);

        var wrappedZ = Noise.ValueTorus(12345, 10.0, 20.0,
            30.0 + circumferenceZ, circumferenceX,
            circumferenceZ, 1.0, 1.0, 1.0);

        Assert.Equal(origin, wrappedX, precision: 12);
        Assert.Equal(origin, wrappedZ, precision: 12);
    }
}
