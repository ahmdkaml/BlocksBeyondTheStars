// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Text;

namespace BlocksBeyondTheStars.Client.Portal
{
    /// <summary>
    /// Pure logic behind the in-game <c>/report &lt;player&gt; [note]</c> chat command (official hosted
    /// worlds): argument parsing plus composing the report message with the reported player's recent
    /// chat lines quoted as evidence. Unity-free so the headless test suite covers it; the Unity side
    /// (ChatUi) only gathers the inputs and ships the result via <see cref="PortalClient.Report"/>.
    /// </summary>
    public static class ReportChatCommand
    {
        /// <summary>How many of the reported player's most recent chat lines are quoted as evidence.</summary>
        public const int MaxQuotedLines = 10;

        /// <summary>The WorldHost caps report messages at this length; composing respects it up front.</summary>
        public const int MaxMessageLength = 500;

        /// <summary>
        /// Parses <c>/report &lt;player&gt; [free-text note]</c>. Returns false when the text is not a
        /// /report command at all (so it falls through to normal chat). A bare <c>/report</c> returns
        /// true with an empty <paramref name="name"/> — the caller shows the usage line.
        /// </summary>
        public static bool TryParse(string? text, out string name, out string note)
        {
            name = string.Empty;
            note = string.Empty;

            string t = (text ?? string.Empty).Trim();
            const string keyword = "/report";
            if (!t.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rest = t.Substring(keyword.Length);
            if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
            {
                return false; // "/reportx ..." is some other (unknown) command, not ours
            }

            rest = rest.Trim();
            if (rest.Length == 0)
            {
                return true; // "/report" with no arguments → usage help
            }

            int space = rest.IndexOfAny(new[] { ' ', '\t' });
            if (space < 0)
            {
                name = rest;
            }
            else
            {
                name = rest.Substring(0, space);
                note = rest.Substring(space + 1).Trim();
            }

            name = name.TrimStart('@'); // tolerate the @Name habit from other games
            return true;
        }

        /// <summary>
        /// Builds the report message: the reporter's note (or a stock line) plus the reported player's
        /// most recent chat lines — the evidence the operators review. Capped to the server limit.
        /// </summary>
        public static string ComposeMessage(string? note, IReadOnlyList<(string Sender, string Text)>? recentChat, string reportedName)
        {
            var sb = new StringBuilder();
            sb.Append(string.IsNullOrWhiteSpace(note) ? "reported via /report in chat" : note!.Trim());

            if (recentChat != null)
            {
                var quoted = new List<string>();
                foreach (var (sender, text) in recentChat)
                {
                    if (string.Equals(sender, reportedName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(text))
                    {
                        quoted.Add(text.Trim());
                    }
                }

                int from = Math.Max(0, quoted.Count - MaxQuotedLines);
                if (from < quoted.Count)
                {
                    sb.Append(" | chat: ");
                    for (int i = from; i < quoted.Count; i++)
                    {
                        if (i > from)
                        {
                            sb.Append(" / ");
                        }

                        sb.Append('"').Append(quoted[i]).Append('"');
                    }
                }
            }

            return sb.Length <= MaxMessageLength ? sb.ToString() : sb.ToString(0, MaxMessageLength);
        }
    }
}
