// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>One live instance row on the admin page: the world plus its owner and, when running, the
/// live joined-player count from the instance's /status endpoint (null = unreachable/not running).</summary>
public sealed record AdminWorldRow(WorldRecord World, string OwnerName, int? JoinedPlayers);

/// <summary>
/// Operator admin UI (Basic Auth, /admin): the fleet instance overview with stop/wake, the open
/// player-report queue and account ban management — the browser front-end to what the X-Admin-Token
/// API exposes for scripts. Server-rendered like the portal pages; operator-facing, so English-only.
/// </summary>
public static class WorldHostAdminPages
{
    private static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Ago(long unix)
    {
        if (unix <= 0)
        {
            return "never";
        }

        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays} d ago"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h ago"
            : $"{Math.Max(0, (int)span.TotalMinutes)} min ago";
    }

    public static string Index(
        WorldHostConfig config,
        IReadOnlyList<AdminWorldRow> worlds,
        IReadOnlyList<ReportRecord> openReports,
        IReadOnlyList<AccountRecord> banned,
        AccountRecord? lookedUp,
        string? lookupQuery)
    {
        int active = worlds.Count(w => w.World.Status is WorldStatus.Running or WorldStatus.Starting);
        var sb = new StringBuilder();

        sb.Append($"<h1>Fleet <span class='o'>admin</span> <span class='sub'>· {E(config.BaseDomain)}</span></h1>");
        sb.Append($"<p class='hint'>{worlds.Count} worlds · <b>{active}</b>/{(config.MaxActiveInstances > 0 ? config.MaxActiveInstances.ToString() : "∞")} instances awake · " +
                  $"{openReports.Count} open report(s) · {banned.Count} banned account(s)</p>");

        // ---- Instances ----
        sb.Append("<div class='card'><h2>Instances</h2>");
        if (worlds.Count == 0)
        {
            sb.Append("<p class='hint'>No worlds yet.</p>");
        }
        else
        {
            sb.Append("<table><tr><th>World</th><th>Owner</th><th>Status</th><th>Players</th><th>Port</th><th>Last started</th><th></th></tr>");
            foreach (var row in worlds)
            {
                var w = row.World;
                string players = w.Status == WorldStatus.Running
                    ? (row.JoinedPlayers is { } n ? $"{n}/{config.MaxPlayersPerWorld}" : "?")
                    : "—";
                string action = w.Status is WorldStatus.Running or WorldStatus.Starting
                    ? $"<form method='post' action='/admin/worlds/{w.Id}/stop'><button>stop</button></form>"
                    : $"<form method='post' action='/admin/worlds/{w.Id}/wake'><button>wake</button></form>";
                sb.Append($"<tr><td><b>{E(w.DisplayName)}</b><br><code>{w.Id}</code></td><td>{E(row.OwnerName)}</td>" +
                          $"<td><span class='st {E(w.Status)}'>{E(w.Status)}</span></td><td>{players}</td>" +
                          $"<td>{w.HostPort}</td><td>{Ago(w.LastStartedUnix)}</td><td>{action}</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("</div>");

        // ---- Open player reports ----
        sb.Append("<div class='card'><h2>Open player reports</h2>");
        if (openReports.Count == 0)
        {
            sb.Append("<p class='hint'>Nothing to review. 🎉</p>");
        }
        else
        {
            sb.Append("<table><tr><th>#</th><th>Filed</th><th>World</th><th>Reported name</th><th>Category</th><th>Message</th><th></th></tr>");
            foreach (var r in openReports)
            {
                sb.Append($"<tr><td>{r.Id}</td><td>{Ago(r.CreatedUnix)}</td><td><code>{E(r.WorldId)}</code></td>" +
                          $"<td><a href='/admin?acct={Uri.EscapeDataString(r.ReportedName)}'>{E(r.ReportedName)}</a></td>" +
                          $"<td>{E(r.Category)}</td><td>{E(r.Message)}</td><td>" +
                          $"<form method='post' action='/admin/reports/{r.Id}/close' style='display:inline'><input type='hidden' name='status' value='reviewed'><button>reviewed</button></form>" +
                          $"<form method='post' action='/admin/reports/{r.Id}/close' style='display:inline'><input type='hidden' name='status' value='dismissed'><button>dismiss</button></form>" +
                          "</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("<p class='hint'>The reported name links to the account lookup below (names match only when the player used their account name in-game).</p></div>");

        // ---- Ban management ----
        sb.Append("<div class='card'><h2>Accounts &amp; bans</h2>");
        sb.Append($"<form method='get' action='/admin'><input name='acct' placeholder='account name' value='{E(lookupQuery)}'><button>look up</button></form>");
        if (!string.IsNullOrEmpty(lookupQuery))
        {
            if (lookedUp is null)
            {
                sb.Append($"<p class='hint'>No account named “{E(lookupQuery)}”.</p>");
            }
            else
            {
                string state = lookedUp.IsBanned ? $"BANNED ({E(lookedUp.BanReason)})" : "active";
                sb.Append($"<p><b>{E(lookedUp.Name)}</b> — {state}{(lookedUp.IsDeveloper ? " · developer" : string.Empty)}</p>");
                sb.Append($"<form method='post' action='/admin/ban'><input type='hidden' name='accountId' value='{E(lookedUp.Id)}'>" +
                          $"<input type='hidden' name='banned' value='{(lookedUp.IsBanned ? "false" : "true")}'>" +
                          (lookedUp.IsBanned
                              ? "<button>unban</button>"
                              : "<input name='reason' placeholder='reason (shown to the player)'><button class='danger'>ban account</button>") +
                          "</form>");
            }
        }

        if (banned.Count > 0)
        {
            sb.Append("<h2>Currently banned</h2><table><tr><th>Name</th><th>Reason</th><th></th></tr>");
            foreach (var a in banned)
            {
                sb.Append($"<tr><td>{E(a.Name)}</td><td>{E(a.BanReason)}</td><td>" +
                          $"<form method='post' action='/admin/ban'><input type='hidden' name='accountId' value='{E(a.Id)}'>" +
                          "<input type='hidden' name='banned' value='false'><button>unban</button></form></td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append("</div>");
        sb.Append("<p><a href='/'>← Portal</a></p>");
        sb.Append("<style>table{width:100%;border-collapse:collapse} th,td{padding:6px 8px;text-align:left;border-bottom:1px solid var(--line);vertical-align:top} form{margin:0}</style>");

        return WorldHostPortalPages.Shell("Fleet admin — Blocks Beyond the Stars", sb.ToString());
    }
}
