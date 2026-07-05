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

        // ---- Server health (filled by JS from /admin/stats.json AFTER the page renders — the
        // docker-stats sample behind it takes ~1-2 s and must not stall the page) ----
        sb.Append("<div class='card'><h2>Server health</h2><div id='sh' class='hint'>loading…</div></div>");

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

        // Server-health card renderer. Thresholds mirror the ops alerting levels (<70 % green,
        // <85 % amber, else red). Values interpolated into innerHTML are numbers plus docker container
        // names, whose charset docker itself restricts — nothing player-controlled reaches this card.
        sb.Append(@"<script>
(function () {
  var el = document.getElementById('sh');
  function bar(label, frac, text) {
    var pct = Math.max(0, Math.min(100, Math.round(frac * 100)));
    var color = pct < 70 ? '#7dff9e' : pct < 85 ? '#ff8c26' : '#e05c5c';
    return ""<div style='margin:6px 0'>"" + label + "" <span class='sub'>"" + text + ""</span>"" +
      ""<div style='height:8px;border:1px solid var(--line);border-radius:4px;overflow:hidden'>"" +
      ""<div style='height:100%;width:"" + pct + ""%;background:"" + color + ""'></div></div></div>"";
  }
  fetch('/admin/stats.json').then(function (r) { return r.json(); }).then(function (s) {
    var h = s.host || {}, html = '';
    if (h.load1 != null) { html += bar('CPU load', h.cores ? h.load1 / h.cores : 0, h.load1.toFixed(2) + ' (1 min) / ' + h.cores + ' cores'); }
    if (h.memTotalKb) {
      var usedKb = h.memTotalKb - (h.memAvailableKb || 0);
      html += bar('RAM', usedKb / h.memTotalKb, (usedKb / 1048576).toFixed(1) + ' / ' + (h.memTotalKb / 1048576).toFixed(1) + ' GB');
    }
    if (h.diskTotalBytes) {
      var usedB = h.diskTotalBytes - (h.diskFreeBytes || 0);
      html += bar('Disk (worlds)', usedB / h.diskTotalBytes, (usedB / 1073741824).toFixed(1) + ' / ' + (h.diskTotalBytes / 1073741824).toFixed(1) + ' GB');
    }
    if (!html) { html = ""<p class='hint'>No host metrics on this platform.</p>""; }
    if (s.containers && s.containers.length) {
      html += ""<table><tr><th>Container</th><th>CPU</th><th>Memory</th></tr>"";
      s.containers.forEach(function (c) {
        html += ""<tr><td><code>"" + c.name + ""</code></td><td>"" + c.cpuPercent.toFixed(1) + "" %</td><td>"" +
          (c.memUsedBytes / 1048576).toFixed(0) + "" / "" + (c.memLimitBytes / 1048576).toFixed(0) + "" MB</td></tr>"";
      });
      html += ""</table>"";
    }
    html += ""<p class='hint'>"" + s.fleet.playersOnline + "" player(s) online · "" + s.fleet.accounts +
      "" account(s) · "" + s.fleet.reportsOpen + "" open report(s)</p>"";
    el.innerHTML = html;
  }).catch(function () { el.textContent = 'stats unavailable'; });
})();
</script>");

        return WorldHostPortalPages.Shell("Fleet admin — Blocks Beyond the Stars", sb.ToString());
    }
}
