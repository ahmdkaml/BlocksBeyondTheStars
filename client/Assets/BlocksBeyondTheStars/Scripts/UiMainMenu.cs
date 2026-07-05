// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Portal;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The uGUI main menu (M27 UI rework): the sci-fi mockup look built in code via <see cref="UiKit"/>
    /// — a SYSTEM CHECK panel, the BLOCKS BEYOND THE STARS title, framed cyan menu buttons wired to the shell, a
    /// tagline and the version. Shown over the animated <see cref="MenuBackground"/>. AppShell spawns
    /// it on the MainMenu phase and destroys it on leaving. Decorative panels (world/server info,
    /// community bar) + editable host/port land in a follow-up.
    /// </summary>
    public static class UiMainMenu
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("MainMenuUI");
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject); // let a gamepad drive the menu (inert on keyboard/mouse)

            // --- SYSTEM CHECK panel (decorative flavour) ---
            UiKit.AddPanel(root, 40f, 40f, 280f, 220f, UiKit.PanelFill);
            UiKit.AddText(root, 60f, 54f, 250f, 22f, shell.L("ui.menu.system_check"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            string[] sysKeys = { "ui.sys.engines", "ui.sys.shields", "ui.sys.life_support", "ui.sys.comms", "ui.sys.navigation" };
            string[] sysIcons = { "sys_engines", "sys_shields", "sys_life", "sys_comms", "sys_nav" };
            for (int i = 0; i < sysKeys.Length; i++)
            {
                float yy = 92f + i * 30f;
                UiKit.AddIcon(root, 46f, yy, 18f, sysIcons[i]);
                UiKit.AddText(root, 72f, yy, 178f, 22f, shell.L(sysKeys[i]), 16, UiKit.TextCol);
                UiKit.AddText(root, 250f, yy, 50f, 22f, shell.L("ui.sys.ok"), 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Title ---
            UiKit.AddLogo(root, 360f, 70f, 1200f, 96f, "BLOCKS BEYOND THE STARS", 64);
            UiKit.AddText(root, 1700f, 44f, 180f, 24f, "VER. " + AppShell.Version, 16, UiKit.CyanDim, TextAnchor.MiddleRight);

            // Connect-to-server dialog (built below; the JOIN button reveals it). Captured by the button.
            // dlgName mirrors the dialog's name input so openers can carry the menu's name field over.
            GameObject connect = null;
            InputField dlgName = null;

            // Official-worlds overlay (native only; built below). Captured by its menu button.
            GameObject official = null;

            // --- One-shot notice (e.g. why the last join was refused) ---
            if (!string.IsNullOrEmpty(shell.MenuNotice))
            {
                UiKit.AddText(root, 90f, 286f, 1200f, 28f, shell.MenuNotice, 17,
                    new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            }

            // --- Menu buttons ---
            const float bx = 90f, bw = 440f, bh = 54f, gap = 62f;
            float by = 322f;
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser build: a slimmed "enter your name and play" screen. There is no singleplayer,
            // host, editors or quit in the browser (no local filesystem, no bundled server, and quitting
            // a browser tab is meaningless). The server is preconfigured via Glitch/URL params, so the
            // primary action just joins it; "Connect to a server…" stays as a manual fallback. A name is
            // required so players never join the public realm anonymously. The whole block is guarded so
            // the native client (the #else below) is byte-for-byte unchanged.
            string[] webName = { shell.PlayerName };
            UiKit.AddText(root, bx, by, bw, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(root, bx, by + 28f, bw, 44f, webName[0], v => webName[0] = v);
            var webWarn = UiKit.AddText(root, bx, by + 80f, bw, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            float wby = by + 112f;
            UiKit.AddButton(root, bx, wby, bw, bh, shell.L("ui.menu.play"), () =>
            {
                if (string.IsNullOrWhiteSpace(webName[0]))
                {
                    webWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                shell.PlayerName = webName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();
                shell.StartJoin();
            }, "btn_join");

            // The manual server picker only helps when /play was opened WITHOUT a deep-linked server —
            // players arriving through the portal already have host/port preconfigured, so the extra
            // choice is just noise for them (#221).
            float wextra = 0f;
            if (!GlitchIntegration.TryGetConfiguredServer(out _, out _, out _))
            {
                UiKit.AddButton(root, bx, wby + gap, bw, bh, shell.L("ui.menu.connect_manual"), () =>
                {
                    if (connect != null)
                    {
                        dlgName.text = webName[0]; // carry the menu's name over (fires the input's onChange)
                        connect.SetActive(true);
                    }
                }, "btn_join");
                wextra = gap;
            }

            UiKit.AddButton(root, bx, wby + wextra + gap, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, wby + wextra + gap * 2f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
#else
            // Pilot name on the menu itself (#221): play actions require a chosen name — the old silent
            // "Pilot" default meant nobody ever picked one and multiplayer names collided. The value is
            // persisted on use; the connect dialog's own name field stays as a per-join override.
            string[] natName = { shell.PlayerName };
            UiKit.AddText(root, bx, by, bw, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(root, bx, by + 28f, bw, 44f, natName[0], v => natName[0] = v);
            var natWarn = UiKit.AddText(root, bx, by + 80f, bw, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            float nby = by + 112f;

            // True when a name is present (warns + blocks otherwise); commits it to the shell + settings.
            bool CommitName()
            {
                if (string.IsNullOrWhiteSpace(natName[0]))
                {
                    natWarn.text = shell.L("ui.webgl.need_name");
                    return false;
                }

                natWarn.text = "";
                shell.PlayerName = natName[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();
                shell.HostedToken = ""; // never let an official-worlds grant leak into SP/LAN/manual joins
                return true;
            }

            UiKit.AddButton(root, bx, nby, bw, bh, shell.L("ui.menu.singleplayer"),
                () => { if (CommitName()) { shell.StartSingleplayer(); } }, "btn_singleplayer");
            UiKit.AddButton(root, bx, nby + gap, bw, bh, shell.L("ui.menu.host"),
                () => { if (CommitName()) { shell.StartHost(); } }, "btn_join");
            UiKit.AddButton(root, bx, nby + gap * 2f, bw, bh, shell.L("ui.menu.join"), () =>
            {
                if (CommitName() && connect != null)
                {
                    dlgName.text = shell.PlayerName; // carry the menu's name over (fires the input's onChange)
                    connect.SetActive(true);
                }
            }, "btn_join");
            UiKit.AddButton(root, bx, nby + gap * 3f, bw, bh, shell.L("ui.menu.official"), () =>
            {
                if (CommitName() && official != null)
                {
                    official.SetActive(true);
                }
            }, "btn_join");
            UiKit.AddButton(root, bx, nby + gap * 4f, bw, bh, shell.L("ui.menu.editors"), () => shell.GoTo(ShellPhase.Editors), "btn_singleplayer");
            UiKit.AddButton(root, bx, nby + gap * 5f, bw, bh, shell.L("ui.menu.settings"), shell.OpenSettings, "btn_settings");
            UiKit.AddButton(root, bx, nby + gap * 6f, bw, bh, shell.L("ui.menu.credits"), () => shell.GoTo(ShellPhase.Credits), "btn_credits");
            UiKit.AddButton(root, bx, nby + gap * 7f, bw, bh, shell.L("ui.menu.quit"), shell.Quit, "btn_exit");
#endif

            // --- World / server info panel (bottom-right, decorative) ---
            UiKit.AddPanel(root, 1290f, 650f, 590f, 250f, UiKit.PanelFill);
            UiKit.AddText(root, 1314f, 666f, 540f, 24f, shell.L("ui.menu.world_info"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddInfo(root, 706f, "info_mode", shell.L("ui.info.mode_title"), shell.L("ui.info.mode_desc"));
            AddInfo(root, 770f, "info_multiplayer", shell.L("ui.info.mp_title"), shell.L("ui.info.mp_desc"));
            AddInfo(root, 834f, "info_procedural", shell.L("ui.info.proc_title"), shell.L("ui.info.proc_desc"));

            // --- Bottom bar ---
            // The participate / "Join in" overlay (built below); the bottom-right button reveals it.
            GameObject participate = null;
            UiKit.AddText(root, 90f, 1030f, 500f, 26f, shell.L("ui.menu.community"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 660f, 1030f, 600f, 26f, shell.L("ui.splash.tagline"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            // "Mach mit" — replaces the old "Wishlist on Steam" line; opens the open-source participate panel.
            UiKit.AddButton(root, 1620f, 1018f, 260f, 48f, shell.L("ui.menu.contribute"),
                () => { if (participate != null) participate.SetActive(true); }, "btn_credits");

            // --- Connect-to-server dialog (added last so it draws on top; hidden until JOIN is pressed) ---
            string[] name = { shell.PlayerName };
            string[] host = { shell.Host };
            string[] port = { shell.Port };
            string[] pass = { "" };
            var dim = UiKit.AddImage(root, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            connect = dim.gameObject;
            dim.raycastTarget = true; // swallow clicks behind the dialog
            var dlg = UiKit.AddPanel(connect.transform, 660f, 280f, 600f, 520f, UiKit.Panel).transform;
            UiKit.AddText(dlg, 30f, 24f, 540f, 30f, shell.L("ui.menu.connect_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddText(dlg, 30f, 80f, 540f, 22f, shell.L("ui.menu.connect_name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            dlgName = UiKit.AddInput(dlg, 30f, 106f, 540f, 38f, name[0], v => name[0] = v);
            UiKit.AddText(dlg, 30f, 160f, 540f, 22f, shell.L("ui.menu.connect_host"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 186f, 540f, 38f, host[0], v => host[0] = v);
            UiKit.AddText(dlg, 30f, 240f, 540f, 22f, shell.L("ui.menu.connect_port"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 266f, 260f, 38f, port[0], v => port[0] = v);
            UiKit.AddText(dlg, 30f, 320f, 540f, 22f, shell.L("ui.menu.connect_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(dlg, 30f, 346f, 540f, 38f, pass[0], v => pass[0] = v);
            var dlgWarn = UiKit.AddText(dlg, 30f, 396f, 540f, 22f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(dlg, 30f, 432f, 270f, 54f, shell.L("ui.menu.connect"), () =>
            {
                // A name is required (#221): joining anonymously fell back to a server-assigned
                // "player_N" identity nobody recognizes, and shared "Pilot" names collide.
                if (string.IsNullOrWhiteSpace(name[0]))
                {
                    dlgWarn.text = shell.L("ui.webgl.need_name");
                    return;
                }

                dlgWarn.text = "";
                shell.PlayerName = name[0].Trim();
                shell.Settings.PlayerName = shell.PlayerName; // remember the identity across sessions
                shell.Settings.Save();

                shell.Host = string.IsNullOrWhiteSpace(host[0]) ? "127.0.0.1" : host[0].Trim();
                shell.Port = string.IsNullOrWhiteSpace(port[0]) ? shell.Port : port[0].Trim();
                shell.Password = pass[0] ?? "";
                shell.HostedToken = ""; // manual join: no official-worlds grant
                shell.StartJoin();
            }, "btn_join");
            UiKit.AddButton(dlg, 310f, 432f, 260f, 54f, shell.L("ui.menu.back"), () => connect.SetActive(false), "btn_exit");
            connect.SetActive(false);

#if !UNITY_WEBGL || UNITY_EDITOR
            // --- Official-worlds overlay (native only; HOSTED_WORLDS.md: the browser NEVER picks servers).
            // Sign in to the worlds portal, list your hosted worlds and join one — the portal answers with
            // host/port + a short-lived join grant that is threaded through shell.HostedToken.
            var odim = UiKit.AddModalDim(root);
            official = odim.gameObject;
            var odlg = UiKit.AddPanel(official.transform, 610f, 180f, 700f, 720f, UiKit.Panel).transform;
            UiKit.AddText(odlg, 30f, 24f, 640f, 30f, shell.L("ui.portal.title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // The same notices the portal shows (client parity): the beta warning, the one-line rules
            // summary, and a button opening the full rules page in the browser.
            UiKit.AddText(odlg, 30f, 58f, 640f, 40f, shell.L("ui.portal.beta"), 13,
                new Color(1f, 0.72f, 0.35f), TextAnchor.UpperLeft);
            UiKit.AddText(odlg, 30f, 100f, 470f, 44f, shell.L("ui.portal.rules_line"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            UiKit.AddButton(odlg, 510f, 100f, 160f, 40f, shell.L("ui.portal.view_rules"),
                () => Application.OpenURL(PortalBase() + "/rules"), "btn_credits");

            var oStatus = UiKit.AddText(odlg, 30f, 592f, 640f, 48f, "", 14,
                new Color(1f, 0.55f, 0.4f), TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.AddButton(odlg, 400f, 648f, 270f, 54f, shell.L("ui.menu.back"), () => official.SetActive(false), "btn_exit");

            // Content area rebuilt on every state change (signed out ↔ signed in ↔ fresh world list).
            var oContent = UiKit.AddPanel(odlg, 0f, 150f, 700f, 440f, new Color(0f, 0f, 0f, 0f)).transform;
            var oWorlds = new List<PortalWorldInfo>();

            string PortalBase() => string.IsNullOrWhiteSpace(shell.Settings.PortalUrl)
                ? PortalClient.DefaultPortalUrl
                : shell.Settings.PortalUrl;

            // WorldHost errors carry a stable machine code → show the player's language when we have a
            // translation ('ui.portal.err_<code>'); otherwise fall back to the API's English text. Ban
            // reasons are operator-written free text, so 'banned' keeps the original message.
            string PortalErr(string code, string error)
            {
                if (string.IsNullOrEmpty(code) || code == "banned")
                {
                    return error;
                }

                string key = "ui.portal.err_" + code;
                string localized = shell.L(key);
                return localized == key ? error : localized;
            }

            bool SignedIn() => !string.IsNullOrEmpty(shell.Settings.PortalSessionToken);

            void SignOut()
            {
                shell.Settings.PortalSessionToken = "";
                shell.Settings.PortalAccountName = "";
                shell.Settings.Save();
                oStatus.text = "";
                RebuildPortal();
            }

            async void DoRefresh()
            {
                oStatus.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.ListWorlds(session));
                if (official == null) { return; } // menu was torn down while the request ran
                if (!r.Ok)
                {
                    if (r.Code == "unauthorized" || r.Error == "unauthorized") { SignOut(); return; } // session expired → back to sign-in
                    oStatus.text = PortalErr(r.Code, r.Error);
                    return;
                }

                oStatus.text = "";
                oWorlds.Clear();
                oWorlds.AddRange(r.Worlds);
                RebuildPortal();
            }

            async void DoLogin(string account, string password)
            {
                oStatus.text = shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                var r = await Task.Run(() => portal.Login(account, password));
                if (official == null) { return; }
                if (!r.Ok)
                {
                    oStatus.text = r.Code.Length > 0
                        ? PortalErr(r.Code, r.Error)
                        : shell.L("ui.portal.login_failed") + (r.Error.Length > 0 ? " (" + r.Error + ")" : "");
                    return;
                }

                shell.Settings.PortalSessionToken = r.SessionToken; // session only — the password is never stored
                shell.Settings.PortalAccountName = account;
                shell.Settings.Save();
                oStatus.text = r.TermsOutdated ? shell.L("ui.portal.terms_outdated") : "";
                RebuildPortal();
                DoRefresh();
            }

            async void DoJoinWorld(string worldId)
            {
                if (!CommitName())
                {
                    oStatus.text = shell.L("ui.webgl.need_name");
                    return;
                }

                oStatus.text = shell.L("ui.portal.waking"); // waking a sleeping world can take a moment
                var portal = new PortalClient(PortalBase());
                string session = shell.Settings.PortalSessionToken;
                string playerName = shell.PlayerName;
                var r = await Task.Run(() => portal.JoinWorld(session, worldId, playerName));
                if (official == null) { return; }
                if (!r.Ok)
                {
                    oStatus.text = PortalErr(r.Code, r.Error);
                    return;
                }

                shell.Host = r.NativeHost;
                shell.Port = r.NativePort.ToString();
                shell.Password = "";
                shell.HostedToken = r.JoinToken; // the grant the server-side token gate verifies
                shell.StartJoin();
            }

            void RebuildPortal()
            {
                for (int i = oContent.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(oContent.GetChild(i).gameObject);
                }

                if (!SignedIn())
                {
                    string[] acc = { shell.Settings.PortalAccountName };
                    string[] pw = { "" };
                    UiKit.AddText(oContent, 30f, 20f, 640f, 22f, shell.L("ui.portal.account"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    UiKit.AddInput(oContent, 30f, 46f, 640f, 38f, acc[0], v => acc[0] = v);
                    UiKit.AddText(oContent, 30f, 100f, 640f, 22f, shell.L("ui.menu.connect_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                    var pwInput = UiKit.AddInput(oContent, 30f, 126f, 640f, 38f, pw[0], v => pw[0] = v);
                    pwInput.contentType = InputField.ContentType.Password;
                    UiKit.AddButton(oContent, 30f, 184f, 300f, 54f, shell.L("ui.portal.login"), () => DoLogin(acc[0].Trim(), pw[0]), "btn_join");
                    UiKit.AddText(oContent, 30f, 260f, 640f, 44f, shell.L("ui.portal.signup_hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                    UiKit.AddText(oContent, 30f, 306f, 640f, 24f, PortalBase(), 15, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    return;
                }

                UiKit.AddText(oContent, 30f, 16f, 420f, 26f,
                    shell.L("ui.portal.signed_in") + " " + shell.Settings.PortalAccountName, 16, UiKit.Ok, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddButton(oContent, 460f, 8f, 210f, 42f, shell.L("ui.portal.logout"), SignOut, "btn_exit");
                UiKit.AddButton(oContent, 30f, 54f, 210f, 42f, shell.L("ui.portal.refresh"), DoRefresh, "btn_settings");

                if (oWorlds.Count == 0)
                {
                    UiKit.AddText(oContent, 30f, 120f, 640f, 48f, shell.L("ui.portal.no_worlds"), 15, UiKit.TextCol, TextAnchor.UpperLeft);
                    UiKit.AddText(oContent, 30f, 170f, 640f, 24f, PortalBase(), 15, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                    return;
                }

                float ry = 116f;
                foreach (var world in oWorlds)
                {
                    string id = world.Id; // capture per row
                    UiKit.AddText(oContent, 30f, ry + 10f, 380f, 26f, world.Name, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    UiKit.AddText(oContent, 414f, ry + 10f, 110f, 26f, world.Status, 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    UiKit.AddButton(oContent, 530f, ry, 140f, 46f, shell.L("ui.portal.play"), () => DoJoinWorld(id), "btn_join");
                    ry += 56f;
                    if (ry > 380f) { break; } // quota keeps this short; guard against overflow anyway
                }
            }

            RebuildPortal();
            if (SignedIn())
            {
                DoRefresh(); // stay signed in across launches: populate the list right away
            }

            official.SetActive(false);
#endif

            // --- Participate / "Join in" overlay (added last so it draws on top; hidden until "Mach mit") ---
            var pdim = UiKit.AddModalDim(root);
            participate = pdim.gameObject;
            var pdlg = UiKit.AddPanel(participate.transform, 560f, 250f, 800f, 580f, UiKit.Panel).transform;
            UiKit.AddText(pdlg, 40f, 26f, 720f, 36f, shell.L("ui.contribute.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            Text Para(float y, float h, string text, int size, Color col)
            {
                var t = UiKit.AddText(pdlg, 40f, y, 720f, h, text, size, col, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                return t;
            }

            Para(82f, 44f, shell.L("ui.contribute.intro"), 18, UiKit.TextCol);
            // Player feedback first (for everyone, in-game) — highlighted; then play, then the GitHub paths.
            Para(138f, 70f, "1.  " + shell.L("ui.contribute.feedback"), 17, UiKit.Ok);
            Para(212f, 50f, "2.  " + shell.L("ui.contribute.play"), 17, UiKit.TextCol);
            Para(266f, 70f, "3.  " + shell.L("ui.contribute.bugs"), 17, UiKit.TextCol);
            Para(340f, 50f, "4.  " + shell.L("ui.contribute.dev"), 17, UiKit.TextCol);
            UiKit.AddText(pdlg, 40f, 424f, 720f, 26f, shell.L("ui.contribute.github"), 17, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(pdlg, 270f, 500f, 260f, 52f, shell.L("ui.menu.back"), () => participate.SetActive(false), "btn_exit");
            participate.SetActive(false);

            return canvas.gameObject;
        }

        private static void AddInfo(Transform root, float y, string icon, string title, string desc)
        {
            UiKit.AddIcon(root, 1314f, y + 4f, 32f, icon);
            UiKit.AddText(root, 1356f, y, 500f, 22f, title, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1356f, y + 24f, 500f, 22f, desc, 14, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }
    }
}
