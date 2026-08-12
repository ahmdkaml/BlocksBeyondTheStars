// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class ChunkDataTests
{
    [Fact]
    public void SetModifier_GetModifier_RoundTrips()
    {
        var chunk = new ChunkData(new ChunkCoord(0, 0, 0));

        chunk.SetModifier(1, 2, 3, 0x123456, 0xABCDEF);

        Assert.Equal((0x123456, 0xABCDEF), chunk.GetModifier(1, 2, 3));
    }

    [Fact]
    public void SetBlockToAir_ClearsModifier()
    {
        var chunk = new ChunkData(new ChunkCoord(0, 0, 0));

        chunk.SetModifier(1, 2, 3, 0x123456, 0xABCDEF);
        chunk.Set(1, 2, 3, new BlockId(BlockId.AirValue));

        Assert.Equal((0, 0), chunk.GetModifier(1, 2, 3));
    }
}
