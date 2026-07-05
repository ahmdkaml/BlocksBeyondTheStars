// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Portal;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The pure logic behind the in-game "/report &lt;player&gt; [note]" chat command: argument parsing
/// and composing the report message with the reported player's recent chat lines as evidence.
/// </summary>
public sealed class ReportChatCommandTests
{
    [Theory]
    [InlineData("/report Meanie", "Meanie", "")]
    [InlineData("/report Meanie he was rude", "Meanie", "he was rude")]
    [InlineData("/REPORT Meanie", "Meanie", "")] // commands are case-insensitive, like /bump
    [InlineData("  /report   @Meanie   spam  ", "Meanie", "spam")] // tolerates extra spaces + the @Name habit
    public void TryParse_AcceptsReportCommands(string text, string expectedName, string expectedNote)
    {
        Assert.True(ReportChatCommand.TryParse(text, out string name, out string note));
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedNote, note);
    }

    [Fact]
    public void TryParse_BareReport_ReturnsEmptyName_ForUsageHelp()
    {
        Assert.True(ReportChatCommand.TryParse("/report", out string name, out _));
        Assert.Equal(string.Empty, name);
    }

    [Theory]
    [InlineData("hello there")]
    [InlineData("/reporter Meanie")] // an unknown command must fall through to normal chat
    [InlineData("/bump broken thing")]
    [InlineData("")]
    public void TryParse_RejectsEverythingElse(string text)
    {
        Assert.False(ReportChatCommand.TryParse(text, out _, out _));
    }

    [Fact]
    public void ComposeMessage_QuotesOnlyTheReportedPlayersLines()
    {
        var chat = new List<(string Sender, string Text)>
        {
            ("Meanie", "you are all stupid"),
            ("Justus", "please stop"),
            ("meanie", "no lol"), // sender match is case-insensitive
        };

        string msg = ReportChatCommand.ComposeMessage("he insults everyone", chat, "Meanie");
        Assert.StartsWith("he insults everyone | chat: ", msg, System.StringComparison.Ordinal);
        Assert.Contains("\"you are all stupid\"", msg, System.StringComparison.Ordinal);
        Assert.Contains("\"no lol\"", msg, System.StringComparison.Ordinal);
        Assert.DoesNotContain("please stop", msg, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ComposeMessage_KeepsOnlyTheMostRecentLines_AndTheServerLengthCap()
    {
        var chat = new List<(string Sender, string Text)>();
        for (int i = 0; i < 30; i++)
        {
            chat.Add(("Meanie", $"line-{i:D2} padding padding padding padding padding"));
        }

        string msg = ReportChatCommand.ComposeMessage(null, chat, "Meanie");
        Assert.DoesNotContain("line-19", msg, System.StringComparison.Ordinal); // older than the newest 10
        Assert.Contains("line-20", msg, System.StringComparison.Ordinal);       // the quoting window starts here
        Assert.True(msg.Length <= ReportChatCommand.MaxMessageLength);
    }

    [Fact]
    public void ComposeMessage_WithoutNoteOrMatchingLines_StillSaysSomething()
    {
        string msg = ReportChatCommand.ComposeMessage("", new List<(string, string)>(), "Ghost");
        Assert.False(string.IsNullOrWhiteSpace(msg)); // the server rejects nothing here, but reviewers need context
    }
}
