// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Server-rendered portal shells for the hosted-worlds control plane: landing (sign in / create account
/// with the required community-rules + beta acceptance), "My Worlds" management, and the rules page.
/// Pages are fully localized server-side (German default, English via <c>?lang=en</c> — see
/// <see cref="NormalizeLang"/>); a DE/EN switcher lives in the shared footer. Self-contained like the
/// per-instance PortalPage (inline CSS + SVG logo, no shipped assets); the pages talk to /api with a
/// Bearer session from localStorage. Deliberately compact — the polished experience is the game itself.
/// </summary>
public static class WorldHostPortalPages
{
    /// <summary>Serializer for the localized-string maps injected into the page scripts: the default
    /// encoder would escape every umlaut/ellipsis as \uXXXX. Relaxed escaping is safe here — the maps
    /// hold only our own static literals, never player input.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions ScriptJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Clamps a request's language choice to the two supported portal languages. German is
    /// the default for anything unknown — the service's primary audience — and "en" must be an exact,
    /// deliberate choice (URL parameter or the cookie the switcher set).</summary>
    public static string NormalizeLang(string? lang) => lang == "en" ? "en" : "de";

    public static string Landing(WorldHostConfig config, string lang = "de")
    {
        lang = NormalizeLang(lang);
        string T(string de, string en) => lang == "en" ? en : de;

        string body = $@"
<h1>Blocks Beyond the Stars — <span class='o'>{T("Welten", "Worlds")}</span></h1>
<p class='sub'>{T("Eigene Multiplayer-Welt erstellen und mit Freunden spielen.", "Create your own multiplayer world and play with friends.")}</p>
<div class='beta'>⚠ <b>Beta:</b> {T(
        "Gehostete Welten können jederzeit kaputtgehen oder verloren gehen — lade regelmäßig eine Sicherung herunter!",
        "Hosted worlds can break or disappear at any time — download a backup regularly!")}</div>
<div id='msg'></div>
<div class='cols'>
 <div class='card'>
  <h2>{T("Anmelden", "Sign in")}</h2>
  <input id='li-name' placeholder='Name' maxlength='24'>
  <input id='li-pass' type='password' placeholder='{T("Passwort", "Password")}'>
  <button onclick='login()'>{T("Anmelden", "Sign in")}</button>
  <div id='li-terms' style='display:none'>
    <p>{T("Die Community-Regeln haben sich geändert.", "The community rules changed.")}</p>
    <label><input type='checkbox' id='li-accept'> {T(
        "Ich akzeptiere die <a href='/rules?lang=de'>Regeln</a>",
        "I accept the <a href='/rules?lang=en'>rules</a>")}</label>
    <button onclick='reaccept()'>{T("Weiter", "Continue")}</button>
  </div>
 </div>
 <div class='card'>
  <h2>{T("Konto erstellen", "Create account")}</h2>
  <input id='su-name' placeholder='Name (3-24: A-Z 0-9 - _)' maxlength='24'>
  <input id='su-pass' type='password' placeholder='{T("Passwort (min. 8 Zeichen)", "Password (min. 8 characters)")}'>
  <p class='hint'>{T(
        "Keine E-Mail nötig. Merke dir dein Passwort gut — ohne E-Mail gibt es keine Wiederherstellung!",
        "No email needed. Remember your password — without an email there is no recovery!")}</p>
  <label><input type='checkbox' id='su-accept'> {T(
        "Ich akzeptiere die <a href='/rules?lang=de'>Community-Regeln</a> und den Beta-Hinweis",
        "I accept the <a href='/rules?lang=en'>community rules</a> and the beta notice")}</label>
  <button onclick='signup()'>{T("Konto erstellen", "Create account")}</button>
 </div>
</div>" + LandingScript
            .Replace("__TERMS__", config.TermsVersion.ToString())
            .Replace("__L__", System.Text.Json.JsonSerializer.Serialize(new
            {
                acceptFirst = T("Bitte akzeptiere zuerst die Regeln.", "Please accept the rules first."),
                wrongLogin = T("Name oder Passwort falsch.", "Wrong name or password."),
                err = T("Fehler", "Error"),
            }, ScriptJson))
            .Replace("__LQ__", lang == "en" ? "?lang=en" : string.Empty);

        return Shell($"Blocks Beyond the Stars — {T("Welten", "Worlds")}", body, lang);
    }

    // Plain (non-interpolated) verbatim script: single braces stay single, localized strings arrive
    // via the injected __L__ JSON so the JS itself needs no per-language variants.
    private const string LandingScript = @"
<script>
const TERMS = __TERMS__;
const L = __L__;
const LQ = '__LQ__'; // keeps ?lang=en across the JS navigations (the bbs_lang cookie is the fallback)
function say(t){document.getElementById('msg').textContent = t||'';}
async function post(url, body){
  const r = await fetch(url, {method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body)});
  let j = null; try { j = await r.json(); } catch {}
  return {ok: r.ok, status: r.status, j};
}
async function signup(){
  if(!document.getElementById('su-accept').checked) return say(L.acceptFirst);
  const r = await post('/api/signup', {name: v('su-name'), password: v('su-pass'), acceptedTermsVersion: TERMS});
  if(!r.ok) return say(bbsErr(r.j, L.err));
  localStorage.setItem('bbs_session', r.j.sessionToken); location.href='/worlds'+LQ;
}
let pendingSession = null;
async function login(){
  const r = await post('/api/login', {name: v('li-name'), password: v('li-pass'), acceptedTermsVersion: 0});
  if(!r.ok) return say(r.status===401 ? L.wrongLogin : bbsErr(r.j, L.err));
  if(r.j.termsOutdated){ pendingSession = r.j.sessionToken; document.getElementById('li-terms').style.display='block'; return; }
  localStorage.setItem('bbs_session', r.j.sessionToken); location.href='/worlds'+LQ;
}
async function reaccept(){
  if(!document.getElementById('li-accept').checked) return say(L.acceptFirst);
  await fetch('/api/accept-terms', {method:'POST', headers:{'Authorization':'Bearer '+pendingSession}});
  localStorage.setItem('bbs_session', pendingSession); location.href='/worlds'+LQ;
}
function v(id){return document.getElementById(id).value.trim();}
</script>";

    public static string Worlds(WorldHostConfig config, string lang = "de")
    {
        lang = NormalizeLang(lang);
        string T(string de, string en) => lang == "en" ? en : de;

        string body = $@"
<h1>{T("Meine <span class='o'>Welten</span>", "My <span class='o'>Worlds</span>")}</h1>
<div class='beta'>⚠ Beta: {T(
        "Welten können kaputtgehen oder verloren gehen — lade Sicherungen herunter!",
        "Worlds can break or vanish — download backups!")}</div>
<div id='msg'></div>
<div class='card'>
 <h2>{T("Neue Welt", "New world")}</h2>
 <input id='w-name' placeholder='{T("Weltname", "World name")}' maxlength='40'>
 <input id='w-pass' type='password' placeholder='{T("Passwort (optional, min. 4 Zeichen)", "Password (optional, min. 4 characters)")}' maxlength='24' autocomplete='new-password'>
 <input id='w-pass2' type='password' placeholder='{T("Passwort wiederholen", "Repeat password")}' maxlength='24' autocomplete='new-password'>
 <button onclick='createWorld()'>{T("Erstellen", "Create")}</button>
 <p class='hint'>{T("Mit Passwort können nur Spieler beitreten, die es kennen.", "With a password, only players who know it can join.")}</p>
</div>
<div id='list'></div>
<div class='card'>
 <h2>{T("Spieler melden", "Report a player")}</h2>
 <p class='hint'>{T(
        "Jemand war gemein oder macht Mist? Sag uns Bescheid — wir schauen es uns an.",
        "Someone was mean or misbehaving? Tell us — we will look into it.")}</p>
 <input id='r-name' placeholder='{T("Spielername", "Player name")}' maxlength='24'>
 <select id='r-cat'><option value='chat'>Chat</option><option value='name'>Name</option><option value='griefing'>{T("Zerstören (Griefing)", "Griefing")}</option><option value='other'>{T("Anderes", "Other")}</option></select>
 <select id='r-world'><option value=''>{T("Welche Welt? (optional)", "Which world? (optional)")}</option></select>
 <input id='r-msg' placeholder='{T("Was ist passiert?", "What happened?")}' maxlength='500'>
 <button onclick='report()'>{T("Melden", "Report")}</button>
</div>
<div class='card'>
 <h2>{T("Konto löschen", "Delete account")}</h2>
 <p class='hint'>{T(
        "Löscht dein Konto endgültig — mitsamt allen deinen Welten und Spielständen. Das kann niemand rückgängig machen!",
        "Permanently deletes your account — including all your worlds and saves. Nobody can undo this!")}</p>
 <button class='danger' onclick='deleteAccount()'>{T("Konto endgültig löschen", "Delete account permanently")}</button>
</div>
<p><a href='/rules{(lang == "en" ? "?lang=en" : "")}'>{T("Regeln", "Rules")}</a> · <a href='#' onclick=""localStorage.removeItem('bbs_session');location.href='/'"">{T("Abmelden", "Sign out")}</a></p>" + WorldsScript
            .Replace("__L__", System.Text.Json.JsonSerializer.Serialize(new
            {
                err = T("Fehler", "Error"),
                namePrompt = T("Dein Spielername?", "Your player name?"),
                waking = T("Welt wird gestartet…", "Waking the world…"),
                playNow = T("▶ Jetzt im Browser spielen", "▶ Play now in the browser"),
                joinInGame = T("Im Spiel beitreten", "Join in game"),
                token = T("Token (10 min gültig)", "Token (valid 10 min)"),
                play = T("Spielen", "Play"),
                stop = T("Stoppen", "Stop"),
                dlSave = T("Sicherung laden", "Download save"),
                upSave = T("Save hochladen", "Upload save"),
                del = T("Löschen", "Delete"),
                delConfirm = T("Welt '%s' wirklich löschen?", "Really delete world '%s'?"),
                noWorlds = T("Noch keine Welt — erstelle deine erste!", "No world yet — create your first!"),
                uploading = T("Upload läuft…", "Uploading…"),
                upDone = T("Save übernommen!", "Save imported!"),
                reported = T("Danke für deine Meldung!", "Thanks for your report!"),
                delAcc1 = T("Konto und ALLE Welten endgültig löschen?", "Permanently delete the account and ALL worlds?"),
                delAcc2 = T("Wirklich sicher? Es gibt kein Zurück!", "Really sure? There is no way back!"),
                pw = T("Passwort", "Password"),
                pwOff = T("(aus)", "(off)"),
                pwProtected = T("Passwort-geschützt", "Password protected"),
                pwNew = T("Neues Passwort (min. 4 Zeichen)", "New password (min. 4 characters)"),
                pwRepeat = T("Wiederholen", "Repeat"),
                pwSet = T("Setzen", "Set"),
                pwRemove = T("Entfernen", "Remove"),
                pwMismatch = T("Die Passwörter stimmen nicht überein.", "The passwords do not match."),
                pwEnter = T("Bitte ein Passwort eingeben (min. 4 Zeichen).", "Please enter a password (min. 4 characters)."),
                pwSetDone = T("Passwort gesetzt.", "Password set."),
                pwRemovedDone = T("Passwort entfernt.", "Password removed."),
                pwRemoveConfirm = T("Passwort entfernen — dann kann jeder beitreten?", "Remove the password — anyone can join then?"),
                pwNeedPrompt = T("Diese Welt braucht ein Passwort:", "This world needs a password:"),
                pwWrongPrompt = T("Falsches Passwort — nochmal versuchen:", "Wrong password — try again:"),
                st = new
                {
                    stopped = T("gestoppt", "stopped"),
                    starting = T("startet…", "starting…"),
                    running = T("läuft", "running"),
                    archived = T("archiviert", "archived"),
                },
            }, ScriptJson));

        return Shell($"{T("Meine Welten", "My Worlds")} — Blocks Beyond the Stars", body, lang);
    }

    private const string WorldsScript = @"
<script>
const S = localStorage.getItem('bbs_session');
if(!S) location.href='/';
const H = {'Authorization':'Bearer '+S, 'Content-Type':'application/json'};
const L = __L__;
function say(t){document.getElementById('msg').textContent = t||'';}
async function api(method, url, body){
  const r = await fetch(url, {method, headers:H, body: body?JSON.stringify(body):undefined});
  if(r.status===401){ location.href='/'; return null; }
  let j=null; try{ j=await r.json(); }catch{}
  if(!r.ok){ say(bbsErr(j, L.err)); return null; }
  return j||{};
}
async function load(){
  const j = await api('GET','/api/worlds'); if(!j) return;
  const el = document.getElementById('list'); el.innerHTML='';
  for(const w of j.worlds){
    const d = document.createElement('div'); d.className='card world';
    d.innerHTML = `<h2>${esc(w.name)} ${w.hasPassword?`<span title='${L.pwProtected}'>🔒</span> `:''}<span class='st ${w.status}'>${L.st[w.status]||w.status}</span></h2>
      <button onclick=""joinWorld('${w.id}')"">${L.play}</button>
      <button onclick=""stopWorld('${w.id}')"">${L.stop}</button>
      <button onclick=""dlSave('${w.id}')"">${L.dlSave}</button>
      <label class='up'>${L.upSave}<input type='file' style='display:none' onchange=""upSave('${w.id}', this.files[0])""></label>
      <button class='danger' onclick=""delWorld('${w.id}', '${esc(w.name)}')"">${L.del}</button>
      <details><summary>${L.pw} ${w.hasPassword?'🔒':L.pwOff}</summary>
        <input id='p1-${w.id}' type='password' placeholder='${L.pwNew}' maxlength='24' autocomplete='new-password'>
        <input id='p2-${w.id}' type='password' placeholder='${L.pwRepeat}' maxlength='24' autocomplete='new-password'>
        <button onclick=""setWorldPassword('${w.id}')"">${L.pwSet}</button>
        ${w.hasPassword?`<button onclick=""removeWorldPassword('${w.id}')"">${L.pwRemove}</button>`:''}
      </details>
      <div class='grant' id='g-${w.id}'></div>`;
    el.appendChild(d);
  }
  if(!j.worlds.length) el.innerHTML = `<p class='hint'>${L.noWorlds}</p>`;
  const rw = document.getElementById('r-world'); const keep = rw.value;
  rw.length = 1; // keep the '(optional)' placeholder, rebuild the rest from the fresh list
  for(const w of j.worlds){ const o=document.createElement('option'); o.value=w.id; o.textContent=w.name; rw.appendChild(o); }
  rw.value = keep && [...rw.options].some(o=>o.value===keep) ? keep : '';
}
async function createWorld(){
  const pw = document.getElementById('w-pass').value, pw2 = document.getElementById('w-pass2').value;
  if(pw !== pw2){ say(L.pwMismatch); return; }
  const j = await api('POST','/api/worlds',{name: document.getElementById('w-name').value.trim(), password: pw||null});
  if(j){ for(const f of ['w-name','w-pass','w-pass2']) document.getElementById(f).value=''; say(''); load(); }
}
async function setWorldPassword(id){
  const pw = document.getElementById('p1-'+id).value, pw2 = document.getElementById('p2-'+id).value;
  if(!pw){ say(L.pwEnter); return; }
  if(pw !== pw2){ say(L.pwMismatch); return; }
  if(await api('POST',`/api/worlds/${id}/password`,{password: pw})){ say(L.pwSetDone); load(); }
}
async function removeWorldPassword(id){
  if(!confirm(L.pwRemoveConfirm)) return;
  if(await api('POST',`/api/worlds/${id}/password`,{password: ''})){ say(L.pwRemovedDone); load(); }
}
async function joinWorld(id, pw){
  const name = prompt(L.namePrompt); if(!name) return;
  for(;;){
    say(L.waking);
    const r = await fetch(`/api/worlds/${id}/join`, {method:'POST', headers:H, body: JSON.stringify({playerName:name, password: pw||null})});
    if(r.status===401){ location.href='/'; return; }
    let j=null; try{ j=await r.json(); }catch{}
    if(r.ok){
      // Refresh the list FIRST (the world just woke, its status chip changed) — load() rebuilds every
      // card with an empty grant div, so rendering the grant before awaiting it made Play look like a
      // no-op (#252).
      await load();
      say('');
      const playUrl = `/play/?auto_join=1&player_name=${encodeURIComponent(name)}`
        + `&server_host=${encodeURIComponent(j.wssUrl)}&hosted_token=${encodeURIComponent(j.joinToken)}`
        + `&world_id=${encodeURIComponent(j.worldId)}`;
      document.getElementById('g-'+id).innerHTML =
        `<p><a class='playnow' href='${esc(playUrl)}' target='_blank' rel='noopener'>${L.playNow}</a></p>
         <p class='hint'><b>${L.joinInGame}:</b> Host <code>${esc(j.nativeHost)}</code> Port <code>${j.nativePort}</code><br>
         ${L.token}: <code>${esc(j.joinToken)}</code></p>`;
      return;
    }
    if(j && (j.code==='password_required' || j.code==='wrong_password')){
      say(j.code==='wrong_password' ? bbsErr(j,'') : '');
      pw = prompt(j.code==='wrong_password' ? L.pwWrongPrompt : L.pwNeedPrompt);
      if(!pw){ say(''); return; }
      continue;
    }
    say(bbsErr(j, L.err));
    return;
  }
}
async function stopWorld(id){ if(await api('POST',`/api/worlds/${id}/stop`)) load(); }
async function delWorld(id,name){
  if(!confirm(L.delConfirm.replace('%s', name))) return;
  if(await api('DELETE',`/api/worlds/${id}`)!==null) load();
}
async function dlSave(id){
  const r = await fetch(`/api/worlds/${id}/save`, {headers:{'Authorization':'Bearer '+S}});
  if(!r.ok){ let j=null; try{ j=await r.json(); }catch{}; return say(bbsErr(j, L.err)); }
  const b = await r.blob(); const a = document.createElement('a');
  a.href = URL.createObjectURL(b); a.download = `${id}-world.db`; a.click(); URL.revokeObjectURL(a.href);
}
async function upSave(id, file){
  if(!file) return;
  say(L.uploading);
  const r = await fetch(`/api/worlds/${id}/save`, {method:'POST', headers:{'Authorization':'Bearer '+S}, body: file});
  let j=null; try{ j=await r.json(); }catch{}
  say(r.ok ? L.upDone : bbsErr(j, L.err));
}
async function report(){
  const j = await api('POST','/api/reports',{reportedName:v('r-name'), category:document.getElementById('r-cat').value, message:v('r-msg'), worldId:document.getElementById('r-world').value});
  if(j){ say(L.reported); document.getElementById('r-name').value=''; document.getElementById('r-msg').value=''; }
}
async function deleteAccount(){
  if(!confirm(L.delAcc1)) return;
  if(!confirm(L.delAcc2)) return;
  if(await api('DELETE','/api/account')!==null){ localStorage.removeItem('bbs_session'); location.href='/'; }
}
function v(id){return document.getElementById(id).value.trim();}
function esc(s){return String(s).replace(/[&<>""']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'}[c]));}
load();
</script>";

    public static string Rules(WorldHostConfig config, string lang = "de")
    {
        lang = NormalizeLang(lang);
        string T(string de, string en) => lang == "en" ? en : de;

        string card = lang == "en"
            ? $@"
<div class='card'>
 <p><b>Blocks Beyond the Stars is a family and community project.</b> Kids and grown-ups play here
 together — be kind to each other!</p>
 <ul>
  <li>Be friendly and help other players.</li>
  <li>Build, explore and invent — don't destroy on purpose what others have built.</li>
  <li><b>No hate speech, no bullying, no racism, no insults</b> — not in chat, names or builds. Such
   violations lead to an <b>immediate ban</b>, no warning.</li>
  <li>Never share personal data (real name, address, school …) and don't ask others for theirs.</li>
  <li>Saw something bad? Use <b>“Report player”</b> — we review every report.</li>
 </ul>
 <p class='beta'>⚠ <b>Beta notice:</b> the game and its hosted worlds are a beta. Worlds and saves can
 break or disappear at any time. Download a backup of your world regularly if it matters to you!</p>
</div>"
            : $@"
<div class='card'>
 <p><b>Blocks Beyond the Stars ist ein Familien- und Community-Projekt.</b> Hier spielen Kinder und
 Erwachsene zusammen — seid nett zueinander!</p>
 <ul>
  <li>Sei freundlich und hilf anderen Spielern.</li>
  <li>Baue, entdecke und erfinde — zerstöre nicht absichtlich, was andere gebaut haben.</li>
  <li><b>Keine Hetze, kein Mobbing, kein Rassismus, keine Beleidigungen</b> — weder im Chat noch in Namen
   oder Bauwerken. Solche Verstöße führen zum <b>sofortigen Bann</b>, ohne Vorwarnung.</li>
  <li>Gib keine persönlichen Daten weiter (echter Name, Adresse, Schule …) und frage andere nicht danach.</li>
  <li>Etwas Schlimmes gesehen? Nutze <b>„Spieler melden“</b> — wir schauen uns jede Meldung an.</li>
 </ul>
 <p class='beta'>⚠ <b>Beta-Hinweis:</b> Das Spiel und die gehosteten Welten sind eine Beta. Welten und
 Spielstände können jederzeit kaputtgehen oder verloren gehen. Lade dir regelmäßig eine Sicherung deiner
 Welt herunter, wenn sie dir wichtig ist!</p>
</div>";

        return Shell($"{T("Community-Regeln", "Community Rules")} — Blocks Beyond the Stars", $@"
<h1>Community-<span class='o'>{T("Regeln", "Rules")}</span> <span class='sub'>(v{config.TermsVersion})</span></h1>
{card}
<p><a href='/{(lang == "en" ? "?lang=en" : "")}'>{T("Zurück", "Back")}</a></p>", lang);
    }

    /// <summary>Impressum (§5 DDG). Operator data comes from config so a SELF-HOSTED WorldHost never
    /// serves the project authors' identity; unset config renders an explicit "not configured" notice.
    /// The legal body itself stays German (the legally authoritative text for a German operator) —
    /// only the chrome and notices are localized.</summary>
    public static string Impressum(WorldHostConfig config, string lang = "de")
    {
        lang = NormalizeLang(lang);
        string T(string de, string en) => lang == "en" ? en : de;

        string name = System.Net.WebUtility.HtmlEncode(config.LegalName);
        string email = System.Net.WebUtility.HtmlEncode(config.LegalEmail);
        string address = string.Join("<br>", (config.LegalAddress ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => System.Net.WebUtility.HtmlEncode(part.Trim())));

        string operatorBlock = string.IsNullOrEmpty(name)
            ? $@"<p class='beta'>⚠ {T(
                "Der Betreiber dieses (selbst gehosteten) Portals hat sein Impressum noch nicht konfiguriert (BBS_WH_LEGAL_NAME / _ADDRESS / _EMAIL).",
                "The operator of this (self-hosted) portal has not configured their legal notice yet (BBS_WH_LEGAL_NAME / _ADDRESS / _EMAIL).")}</p>"
            : $@"<p><b>{name}</b><br>{address}</p>
                <p>E-Mail: <a href='mailto:{email}'>{email}</a></p>";

        return Shell("Impressum — Blocks Beyond the Stars", $@"
<h1>Impressum <span class='sub'>· Legal notice</span></h1>
{(lang == "en" ? "<p class='hint'>This legal notice is required by German law (§ 5 DDG) and is therefore provided in German.</p>" : "")}
<div class='card'>
 <h2>Angaben gemäß § 5 DDG</h2>
 {operatorBlock}
</div>
<div class='card'>
 <h2>Haftung für Inhalte und Links</h2>
 <p>Die Inhalte dieser Seiten wurden mit größter Sorgfalt erstellt; für Richtigkeit, Vollständigkeit und
 Aktualität wird keine Gewähr übernommen. Von Spielern erstellte Inhalte (Weltnamen, Bauwerke, Chat)
 entstehen live — wir prüfen Meldungen und entfernen Verstöße, sobald wir davon Kenntnis erlangen.
 Für Inhalte externer Links sind ausschließlich deren Betreiber verantwortlich; zum Zeitpunkt der
 Verlinkung waren keine Rechtsverstöße erkennbar.</p>
</div>
<div class='card'>
 <h2>Urheberrecht · Open Source</h2>
 <p>Blocks Beyond the Stars ist ein Open-Source-Projekt: Der Quellcode steht unter der
 <b>AGPL-3.0-Lizenz</b> auf <a href='https://github.com/marceld23/BlocksBeyondTheStars'>GitHub</a>.
 Namen und Logos von „Blocks Beyond the Stars“ und „JuMaVe Games“ bleiben davon unberührt.</p>
</div>
<div class='card'>
 <h2>Beta-Dienst</h2>
 <p>{T(
        "Dieses Welten-Portal ist ein kostenloses Hobby- und Familienprojekt im Beta-Stadium. Es besteht kein Anspruch auf Verfügbarkeit oder Datenerhalt — Welten und Spielstände können jederzeit verloren gehen (nutze die Sicherungs-Funktion!).",
        "This worlds portal is a free hobby and family project in beta. There is no entitlement to availability or data retention — worlds and saves can be lost at any time (use the backup feature!).")}</p>
</div>
<p><a href='/{(lang == "en" ? "?lang=en" : "")}'>{T("Zurück", "Back")}</a></p>", lang);
    }

    /// <summary>Datenschutzerklärung (DSGVO) — the German text is the legally authoritative one; with
    /// <c>?lang=en</c> the English summary card moves to the top so English visitors get the essentials
    /// first, followed by the authoritative German text.</summary>
    public static string Privacy(WorldHostConfig config, string lang = "de")
    {
        lang = NormalizeLang(lang);
        string T(string de, string en) => lang == "en" ? en : de;

        string name = System.Net.WebUtility.HtmlEncode(config.LegalName);
        string email = System.Net.WebUtility.HtmlEncode(config.LegalEmail);
        string address = System.Net.WebUtility.HtmlEncode(config.LegalAddress);
        string controller = string.IsNullOrEmpty(name)
            ? $"<p class='beta'>⚠ {T("Verantwortlicher noch nicht konfiguriert (BBS_WH_LEGAL_*).", "Controller not configured yet (BBS_WH_LEGAL_*).")}</p>"
            : $"<p><b>{name}</b>, {address} — E-Mail: <a href='mailto:{email}'>{email}</a></p>";

        string englishSummary = @"
<div class='card'>
 <h2>🇬🇧 English summary</h2>
 <p>This service is deliberately data-minimal: a self-chosen account name, a password hash (never the
 password), your worlds and your reports — <b>no email, no real name, no ads, no tracking, no third-party
 embeds</b>. IPs are held transiently for rate limiting and in routine server logs only. Hosting is in
 Germany; nothing is shared or sold. Short in-game NPC texts are generated by an LLM at OVHcloud (EU) —
 only the moment's game context (e.g. your in-world player name, the NPC and the situation) is sent, never
 account data or IPs, and built-in fallback texts take over if it is unavailable. Deleting your account
 (button on the worlds page) permanently removes the account, sessions, your reports and all your worlds
 including saves. For any privacy request, email the address above. The German text below is the
 authoritative version.</p>
</div>";

        string germanBody = $@"
<div class='card'>
 <h2>Kurz und ehrlich (für Kinder erklärt)</h2>
 <p>Wir wollen so wenig wie möglich über dich wissen: <b>keine E-Mail, kein echter Name, keine Werbung,
 kein Tracking.</b> Wir speichern nur deinen erfundenen Spielernamen, dein Passwort (verschlüsselt als
 Hash), deine Welten und deine Meldungen. Wenn du dein Konto löschst, ist alles davon weg.</p>
</div>
<div class='card'>
 <h2>1. Verantwortlicher</h2>
 {controller}
</div>
<div class='card'>
 <h2>2. Welche Daten wir verarbeiten</h2>
 <ul>
  <li><b>Konto:</b> selbst gewählter Kontoname, Passwort-Hash (PBKDF2 — das Passwort selbst wird nie
   gespeichert), Zeitpunkt der Regel-Zustimmung. Keine E-Mail-Adresse, kein Klarname.</li>
  <li><b>Welten &amp; Spielstände:</b> die von dir erstellten oder hochgeladenen Welten (Spieldaten).</li>
  <li><b>Meldungen:</b> von dir abgesendete „Spieler melden“-Einträge (gemeldeter Name, Kategorie, Text).</li>
  <li><b>Sitzung:</b> ein zufälliges Sitzungs-Token im localStorage deines Browsers (kein Cookie,
   kein seitenübergreifendes Tracking). Wählst du eine Sprache, merkt sich ein einzelnes Cookie
   (<code>bbs_lang</code>) nur diese Einstellung.</li>
  <li><b>Technisch:</b> IP-Adressen kurzzeitig im Arbeitsspeicher zur Missbrauchs-Begrenzung (Rate-Limits)
   sowie in üblichen Server-Protokollen; keine Auswertung zu Werbezwecken, keine Weitergabe.</li>
 </ul>
 <p>Es werden keine Analyse-/Werbedienste, keine Social-Media-Einbindungen und keine externen Schriftarten
 geladen.</p>
</div>
<div class='card'>
 <h2>3. Zwecke und Rechtsgrundlagen</h2>
 <p>Verarbeitung zur Bereitstellung des Dienstes — Konto, Welten, Spielbetrieb (Art. 6 Abs. 1 lit. b DSGVO) —
 sowie zur Missbrauchsabwehr und Sicherheit — Rate-Limits, Protokolle, Meldungen/Sperren
 (Art. 6 Abs. 1 lit. f DSGVO).</p>
</div>
<div class='card'>
 <h2>4. Hosting und Empfänger</h2>
 <p>Der Dienst läuft auf einem in Deutschland angemieteten Server. Es findet keine Übermittlung in
 Drittländer und kein Verkauf oder Teilen von Daten statt.</p>
 <p><b>KI-generierte Spieltexte:</b> Für kurze Texte von Spielfiguren (NSC-Begrüßungen, Missions-Texte)
 nutzen wir ein Sprachmodell bei <b>OVHcloud</b> (Europäische Union, „AI Endpoints“). Dorthin wird nur der
 spielbezogene Kontext des jeweiligen Moments übermittelt — z.&nbsp;B. dein <b>Spielername in der Welt</b>,
 der Name der Spielfigur und die Spielsituation. Es werden keine Kontodaten, Passwörter oder IP-Adressen
 übermittelt, dort wird kein Konto über dich geführt, und die Anfragen dienen allein der Text-Erzeugung
 (Art. 6 Abs. 1 lit. f DSGVO). Fällt der Dienst aus, nutzt das Spiel automatisch eingebaute Standard-Texte.</p>
</div>
<div class='card'>
 <h2>5. Speicherdauer und Löschung</h2>
 <ul>
  <li>Konto, Welten und Meldungen bleiben gespeichert, bis du dein Konto löschst — dafür gibt es den
   Button <b>„Konto löschen“</b> auf der Welten-Seite; damit werden Konto, Sitzungen, deine Meldungen und
   alle deine Welten samt Spielständen endgültig entfernt.</li>
  <li>Lange inaktive Welten werden nach etwa {config.ArchiveAfterMonths} Monaten archiviert (nicht gelöscht).</li>
  <li>IP-bezogene Einträge in Rate-Limits liegen nur im Arbeitsspeicher; Server-Protokolle werden turnusmäßig überschrieben.</li>
 </ul>
</div>
<div class='card'>
 <h2>6. Deine Rechte</h2>
 <p>Du hast das Recht auf Auskunft, Berichtigung, Löschung, Einschränkung der Verarbeitung,
 Datenübertragbarkeit und Widerspruch (Art. 15–21 DSGVO) — schreib dazu einfach an die oben genannte
 E-Mail-Adresse. Außerdem kannst du dich bei einer Datenschutz-Aufsichtsbehörde beschweren, z.B. beim
 Landesbeauftragten für den Datenschutz und die Informationsfreiheit Rheinland-Pfalz.</p>
</div>";

        return Shell("Datenschutz — Blocks Beyond the Stars", $@"
<h1>Datenschutz<span class='o'>erklärung</span> <span class='sub'>· Privacy policy</span></h1>
{(lang == "en" ? englishSummary + germanBody : germanBody + englishSummary)}
<p><a href='/{(lang == "en" ? "?lang=en" : "")}'>{T("Zurück", "Back")}</a></p>", lang);
    }

    /// <summary>The game logo as a compact self-contained inline SVG (mini block cluster + orbit ring,
    /// condensed from the per-instance PortalPage emblem) — the portal ships no static assets.</summary>
    private const string LogoSvg = @"<svg class='mark' viewBox='0 0 96 72' xmlns='http://www.w3.org/2000/svg' aria-hidden='true'>
<g><rect x='34' y='22' width='16' height='16' rx='2' fill='#2c7193'/><rect x='34' y='22' width='16' height='5' rx='2' fill='#46a4c6'/></g>
<g><rect x='48' y='25' width='16' height='16' rx='2' fill='#367e9f'/><rect x='48' y='25' width='16' height='5' rx='2' fill='#5cb6d6'/></g>
<g><rect x='26' y='34' width='16' height='16' rx='2' fill='#57c2e4'/><rect x='26' y='34' width='16' height='5' rx='2' fill='#8fe2ff'/></g>
<g><rect x='42' y='38' width='16' height='16' rx='2' fill='#3f97bb'/><rect x='42' y='38' width='16' height='5' rx='2' fill='#67c4e2'/></g>
<ellipse cx='48' cy='38' rx='42' ry='22' fill='none' stroke='#5fd7ff' stroke-opacity='.6' stroke-width='2'/>
<circle cx='90' cy='38' r='3.4' fill='#ff8c26'/>
</svg>";

    /// <summary>Shared page chrome, styled after the per-instance portal (dark starfield, cyan/orange):
    /// game-logo header, localized footer with the DE/EN switcher, and the shared error localization.
    /// Internal so the operator admin pages (<see cref="WorldHostAdminPages"/>) share the same look.</summary>
    internal static string Shell(string title, string body, string lang = "de")
    {
        lang = NormalizeLang(lang);
        string T(string de, string en) => lang == "en" ? en : de;

        return $@"<!DOCTYPE html>
<html lang='{lang}'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>{System.Net.WebUtility.HtmlEncode(title)}</title>
<link rel='icon' type='image/x-icon' href='/favicon.ico'>
<style>
:root{{--cyan:#5fd7ff;--orange:#ff8c26;--line:#21304c}}
*{{box-sizing:border-box}}
body{{font-family:'Rajdhani','Segoe UI',system-ui,sans-serif;color:#dfe9f7;margin:0;padding:24px;min-height:100vh;
 background:radial-gradient(1100px 640px at 50% -12%,#15243f 0%,#070a12 58%),#070a12}}
main{{max-width:860px;margin:0 auto}}
header{{max-width:860px;margin:0 auto 6px}}
.brand{{display:flex;align-items:center;gap:12px;text-decoration:none}}
.brand .mark{{width:64px;height:48px;flex:none}}
.brand .word{{font-size:20px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:#eaf6ff;
 text-shadow:0 0 18px rgba(95,215,255,.5),0 0 3px rgba(95,215,255,.85)}}
.brand .word b{{color:var(--cyan);font-weight:700}}
h1{{font-weight:700;letter-spacing:.5px}} .o{{color:var(--orange)}} a{{color:var(--cyan)}}
.sub{{color:#9db2cf;font-size:.95rem}} .hint{{color:#9db2cf;font-size:.9rem}}
.cols{{display:flex;gap:16px;flex-wrap:wrap}} .cols .card{{flex:1 1 300px}}
.card{{background:#0d1526cc;border:1px solid var(--line);border-radius:12px;padding:16px 18px;margin:12px 0}}
.beta{{background:#3a260a;border:1px solid #7a5218;border-radius:10px;padding:10px 14px;margin:12px 0}}
input,select{{display:block;width:100%;margin:8px 0;padding:9px 10px;border-radius:8px;border:1px solid var(--line);
 background:#0a101d;color:#dfe9f7;font:inherit}}
button{{margin:6px 6px 0 0;padding:9px 16px;border-radius:8px;border:1px solid var(--cyan);background:#12335005;
 color:var(--cyan);font:inherit;font-weight:600;cursor:pointer}}
button:hover{{background:#1c4a6e55}} button.danger{{border-color:#e05c5c;color:#e05c5c}}
label.up{{display:inline-block;margin:6px 6px 0 0;padding:9px 16px;border-radius:8px;border:1px solid var(--cyan);
 color:var(--cyan);font-weight:600;cursor:pointer}}
a.playnow{{display:inline-block;margin:8px 0 2px;padding:12px 22px;border-radius:10px;font-weight:700;font-size:16px;
 text-decoration:none;color:#fff;background:linear-gradient(180deg,#2f8fff,#1d68d8);box-shadow:0 8px 22px rgba(29,104,216,.45)}}
a.playnow:hover{{filter:brightness(1.08)}}
.st{{font-size:.8rem;padding:2px 10px;border-radius:10px;border:1px solid var(--line);vertical-align:middle}}
.st.running{{color:#7dff9e;border-color:#2e7d44}} .st.stopped{{color:#9db2cf}} .st.starting{{color:var(--orange);border-color:#7a5218}}
code{{background:#0a101d;border:1px solid var(--line);border-radius:6px;padding:1px 6px;word-break:break-all}}
#msg{{margin:10px 0;color:var(--orange);min-height:1.2em;font-weight:600}}
ul{{line-height:1.6}}
footer{{max-width:860px;margin:28px auto 0;padding-top:12px;border-top:1px solid var(--line);
 color:#9db2cf;font-size:.9rem;text-align:center}}
footer .lang a{{margin:0 4px}} footer .lang .cur{{color:#dfe9f7;font-weight:700}}
</style>
<script>
// Shared error localization: API errors carry a machine `code`; the page's server-chosen language
// (DE default, ?lang=en) picks the column. Ban reasons are operator-written free text, so `banned`
// keeps the original message.
window.bbsErr = function(j, fallback) {{
  var de = __LANGDE__;
  var M = {{
    accept_rules: ['Bitte akzeptiere zuerst die Community-Regeln.', 'Please accept the community rules first.'],
    name_invalid: ['Name: 3-24 Zeichen, nur Buchstaben, Ziffern, - und _.', 'Name must be 3-24 characters: letters, digits, - or _.'],
    password_short: ['Das Passwort braucht mindestens 8 Zeichen.', 'Password must be at least 8 characters.'],
    name_taken: ['Dieser Name ist schon vergeben.', 'This name is already taken.'],
    name_reserved: ['Dieser Name ist reserviert.', 'This name is reserved.'],
    name_blocked: ['Bitte wähle einen anderen Namen.', 'Please choose a different name.'],
    world_name_invalid: ['Weltname: 1-40 druckbare Zeichen.', 'World name must be 1-40 printable characters.'],
    world_limit: ['Welten-Limit erreicht.', 'World limit reached.'],
    no_capacity: ['Gerade keine Kapazität frei — bitte später nochmal versuchen.', 'No capacity available right now — please try again later.'],
    player_name_invalid: ['Spielername: 1-24 druckbare Zeichen.', 'Player name must be 1-24 printable characters.'],
    terms_outdated: ['Die Community-Regeln haben sich geändert — bitte neu akzeptieren.', 'The community rules changed — please accept them again.'],
    world_not_found: ['Welt nicht gefunden.', 'World not found.'],
    world_start_failed: ['Die Welt konnte nicht gestartet werden — bitte gleich nochmal versuchen.', 'The world could not be started — please try again in a moment.'],
    world_wake_failed: ['Die Welt ist nicht rechtzeitig aufgewacht — bitte nochmal versuchen.', 'The world did not come up in time — please try again.'],
    stop_first: ['Bitte stoppe die Welt zuerst.', 'Stop the world first.'],
    upload_too_large: ['Der Save ist zu groß für den Upload.', 'The save exceeds the upload limit.'],
    upload_empty: ['Leerer Upload.', 'Empty upload.'],
    save_invalid: ['Diese Datei ist kein gültiger Blocks-Beyond-the-Stars-Spielstand.', 'This file is not a valid Blocks Beyond the Stars save.'],
    save_missing: ['Diese Welt hat noch keinen Spielstand (nie gestartet).', 'This world has no save yet (never started).'],
    rate_limited: ['Zu viele Anfragen — bitte warte kurz und versuche es dann nochmal.', 'Too many requests — please wait a bit and try again.'],
    password_required: ['Diese Welt braucht ein Passwort.', 'This world needs a password.'],
    wrong_password: ['Falsches Welt-Passwort.', 'Wrong world password.'],
    too_many_attempts: ['Zu viele Passwort-Versuche — bitte warte ein paar Minuten.', 'Too many password attempts — please wait a few minutes.'],
    world_password_invalid: ['Welt-Passwort: 4-24 druckbare Zeichen.', 'World password must be 4-24 printable characters.'],
  }};
  if (j && j.code === 'banned') {{ return j.error || fallback; }}
  var hit = j && j.code && M[j.code];
  return hit ? hit[de ? 0 : 1] : ((j && j.error) || fallback);
}};
</script>
</head>
<body>
<header><a class='brand' href='/{(lang == "en" ? "?lang=en" : "")}'>{LogoSvg}<span class='word'><b>Blocks</b> Beyond the Stars</span></a></header>
<main>{body}</main>
<footer>
<a href='/rules{(lang == "en" ? "?lang=en" : "")}'>{T("Regeln", "Rules")}</a> ·
<a href='/impressum{(lang == "en" ? "?lang=en" : "")}'>{T("Impressum", "Legal notice")}</a> ·
<a href='/datenschutz{(lang == "en" ? "?lang=en" : "")}'>{T("Datenschutz", "Privacy")}</a>
<span class='lang'> · {(lang == "en"
        ? "<a href='?lang=de'>Deutsch</a><span class='cur'>English</span>"
        : "<span class='cur'>Deutsch</span><a href='?lang=en'>English</a>")}</span>
</footer>
</body>
</html>".Replace("__LANGDE__", lang == "en" ? "false" : "true");
    }
}
