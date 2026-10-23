// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;
using BlocksBeyondTheStars.Persistence;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class BlockPaletteMigrationTests
{
    [Fact]
    public void BuildBlockColumnRemapSql_EmptyRemap_ReturnsNull()
    {
        var remap = new Dictionary<ushort, ushort>();
        var sql = BlockPaletteMigration.BuildBlockColumnRemapSql("block_edit", remap);
        Assert.Null(sql);
    }

    [Fact]
    public void BuildBlockColumnRemapSql_SinglePair_ReturnsExactSql()
    {
        var remap = new Dictionary<ushort, ushort> { { 5, 7 } };
        var sql = BlockPaletteMigration.BuildBlockColumnRemapSql("block_edit", remap);

        Assert.Equal("UPDATE block_edit SET block = CASE block WHEN 5 THEN 7 ELSE block END WHERE block IN (5);", sql);
    }

    [Fact]
    public void BuildBlockColumnRemapSql_MultiplePairs_ProducesSortedDeterministicSql()
    {
        var remap = new Dictionary<ushort, ushort> { { 12, 20 }, { 3, 8 } };
        var sql = BlockPaletteMigration.BuildBlockColumnRemapSql("structure_edit", remap);

        var expected = "UPDATE structure_edit SET block = CASE block WHEN 3 THEN 8 WHEN 12 THEN 20 ELSE block END WHERE block IN (3,12);";
        Assert.Equal(expected, sql);
    }
}
