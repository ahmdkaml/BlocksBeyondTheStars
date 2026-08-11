// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class MissionValidatorTests
{
    private static GameContent Load() =>
        ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    [Fact]
    public void Validate_RejectsEmptyMissionId()
    {
        var mission = new MissionDefinition
        {
            Id = "",
            Objectives =
            {
                new MissionObjective
                {
                    Type = MissionObjectiveType.Collect,
                    Target = "missing-item",
                    Required = 1,
                },
            },
        };

        var problems = MissionValidator.Validate(mission, Load());

        Assert.Contains(problems, problem => problem.Contains("Mission id is empty."));
    }

    [Fact]
    public void Validate_RejectsMissionWithoutObjectives()
    {
        var mission = new MissionDefinition
        {
            Id = "test-mission",
        };

        var problems = MissionValidator.Validate(mission, Load());

        Assert.Contains(problems, problem => problem.Contains("Mission has no objectives."));
    }

    [Fact]
    public void Validate_RejectsUnsupportedObjectiveType()
    {
        var mission = new MissionDefinition
        {
            Id = "test-mission",
            Objectives =
            {
                new MissionObjective
                {
                    Type = MissionObjectiveType.Travel,
                    Target = "somewhere",
                    Required = 1,
                },
            },
        };

        var problems = MissionValidator.Validate(mission, Load());

        Assert.Contains(problems, problem =>
            problem.Contains("is not supported yet."));
    }

    [Fact]
    public void Validate_RejectsNonPositiveObjectiveCount()
    {
        var content = Load();
        var item = content.Items.Keys.First();

        var mission = new MissionDefinition
        {
            Id = "test-mission",
            Objectives =
            {
                new MissionObjective
                {
                    Type = MissionObjectiveType.Collect,
                    Target = item,
                    Required = 0,
                },
            },
        };

        var problems = MissionValidator.Validate(mission, content);

        Assert.Contains(problems, problem =>
            problem.Contains("has a non-positive required count."));
    }

    [Fact]
    public void Validate_RejectsUnknownObjectiveTarget()
    {
        var mission = new MissionDefinition
        {
            Id = "test-mission",
            Objectives =
            {
                new MissionObjective
                {
                    Type = MissionObjectiveType.Collect,
                    Target = "definitely-not-a-real-item",
                    Required = 1,
                },
            },
        };

        var problems = MissionValidator.Validate(mission, Load());

        Assert.Contains(problems, problem =>
            problem.Contains("Objective references unknown target"));
    }

    [Fact]
    public void Validate_RejectsUnknownRewardItem()
    {
        var mission = new MissionDefinition
        {
            Id = "test-mission",
            Rewards =
            {
                new ItemAmount("definitely-not-a-real-item", 1),
            },
        };

        var problems = MissionValidator.Validate(mission, Load());

        Assert.Contains(problems, problem =>
            problem.Contains("Reward references unknown item"));
    }

    [Fact]
    public void Validate_RejectsNonPositiveRewardCount()
    {
        var content = Load();
        var item = content.Items.Keys.First();

        var mission = new MissionDefinition
        {
            Id = "test-mission",
            Rewards =
            {
                new ItemAmount(item, 0),
            },
        };

        var problems = MissionValidator.Validate(mission, content);

        Assert.Contains(problems, problem =>
            problem.Contains("has a non-positive count."));
    }

    [Fact]
    public void Validate_AcceptsKnownGoodMission()
    {
        var content = Load();
        var item = content.Items.Keys.First();

        var mission = new MissionDefinition
        {
            Id = "valid-mission",
            Objectives =
            {
                new MissionObjective
                {
                    Type = MissionObjectiveType.Collect,
                    Target = item,
                    Required = 1,
                },
            },
            Rewards =
            {
                new ItemAmount(item, 1),
            },
        };

        var problems = MissionValidator.Validate(mission, content);

        Assert.Empty(problems);
        Assert.True(MissionValidator.IsValid(mission, content));
    }
    [Fact]
    public void Validate_AcceptsMineObjectiveWithKnownBlock()
    {
        var content = Load();
        var block = content.Blocks.Keys.First();

        var mission = new MissionDefinition
        {
            Id = "test-mission",
            Objectives =
            {
                new MissionObjective
                {
                    Type = MissionObjectiveType.Mine,
                    Target = block,
                    Required = 1,
                },
            },
        };

        var problems = MissionValidator.Validate(mission, content);

        Assert.DoesNotContain(
            problems,
            problem => problem.Contains("Objective references unknown target"));
    }
}
