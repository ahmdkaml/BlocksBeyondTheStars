// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// The canonical community-rules text, single-sourced so the portal's /rules page and the
/// <c>GET /api/terms</c> endpoint (the desktop client renders the rules in-game before signup /
/// re-acceptance) can never drift apart: the API's plain text is DERIVED from the page's HTML card.
/// </summary>
public static class CommunityRules
{
    /// <summary>The rules card as shown on the portal's /rules page and inside the signup flow.</summary>
    public static string HtmlCard(string lang) => WorldHostPortalPages.NormalizeLang(lang) == "en"
        ? @"
<div class='card'>
 <p><b>Blocks Beyond the Stars is a family and community project.</b> Kids and grown-ups play here
 together — be kind to each other!</p>
 <ul>
  <li>🧒 Please ask your parents first if it is okay for you to play this game.</li>
  <li>Be friendly and help other players.</li>
  <li>Build, explore and invent — don't destroy on purpose what others have built.</li>
  <li><b>No hate speech, no bullying, no racism, no insults</b> — not in chat, names or builds. Such
   violations lead to an <b>immediate ban</b>, no warning.</li>
  <li>Never share personal data (real name, address, school …) and don't ask others for theirs.</li>
  <li>Saw something bad? Report it right in the game: type <b><code>/report &lt;name&gt;</code></b> in
   chat or use the report button in the player list (ship → alliance) — or the form on the worlds
   page. We review every report.</li>
 </ul>
 <p class='beta'>⚠ <b>Beta notice:</b> the game and its hosted worlds are a beta. Worlds and saves can
 break or disappear at any time. Download a backup of your world regularly if it matters to you!</p>
</div>"
        : @"
<div class='card'>
 <p><b>Blocks Beyond the Stars ist ein Familien- und Community-Projekt.</b> Hier spielen Kinder und
 Erwachsene zusammen — seid nett zueinander!</p>
 <ul>
  <li>🧒 Frag bitte zuerst deine Eltern, ob es okay ist, dieses Spiel zu spielen.</li>
  <li>Sei freundlich und hilf anderen Spielern.</li>
  <li>Baue, entdecke und erfinde — zerstöre nicht absichtlich, was andere gebaut haben.</li>
  <li><b>Keine Hetze, kein Mobbing, kein Rassismus, keine Beleidigungen</b> — weder im Chat noch in Namen
   oder Bauwerken. Solche Verstöße führen zum <b>sofortigen Bann</b>, ohne Vorwarnung.</li>
  <li>Gib keine persönlichen Daten weiter (echter Name, Adresse, Schule …) und frage andere nicht danach.</li>
  <li>Etwas Schlimmes gesehen? Melde es direkt im Spiel: Tippe <b><code>/report &lt;Name&gt;</code></b>
   in den Chat oder nutze den Melden-Knopf in der Spielerliste (Schiff → Allianz) — oder das Formular
   auf der Welten-Seite. Wir schauen uns jede Meldung an.</li>
 </ul>
 <p class='beta'>⚠ <b>Beta-Hinweis:</b> Das Spiel und die gehosteten Welten sind eine Beta. Welten und
 Spielstände können jederzeit kaputtgehen oder verloren gehen. Lade dir regelmäßig eine Sicherung deiner
 Welt herunter, wenn sie dir wichtig ist!</p>
</div>";

    /// <summary>Plain-text rendering of <see cref="HtmlCard"/> for the game client's rules screen
    /// (a Unity <c>Text</c> can't show HTML): bullets become "• " lines, all other tags are stripped
    /// and entities decoded. Derived, never hand-written — so it always matches the page.</summary>
    public static string PlainText(string lang)
    {
        string text = StripTags(HtmlCard(lang)
            .Replace("<li>", "• ")
            .Replace("</li>", "\n")
            .Replace("</p>", "\n\n"));
        // Decode AFTER stripping, so the literal "&lt;Name&gt;" of the /report example survives as <Name>.
        text = System.Net.WebUtility.HtmlDecode(text);

        // Collapse the HTML source's hard-wrapped indentation into clean single-space prose lines.
        var lines = text.Split('\n');
        var sb = new System.Text.StringBuilder();
        var paragraph = new System.Text.StringBuilder();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            if (line.StartsWith("• ", StringComparison.Ordinal))
            {
                FlushParagraph();
            }

            paragraph.Append(paragraph.Length > 0 ? " " : string.Empty).Append(line);
        }

        FlushParagraph();
        return sb.ToString().TrimEnd() + "\n";

        void FlushParagraph()
        {
            if (paragraph.Length > 0)
            {
                sb.Append(paragraph).Append('\n');
                paragraph.Clear();
            }
        }
    }

    /// <summary>Removes HTML tags by a plain scan (no regex — MA0009). Entities pass through untouched.</summary>
    private static string StripTags(string html)
    {
        var sb = new System.Text.StringBuilder(html.Length);
        bool inTag = false;
        foreach (char c in html)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>' && inTag)
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
