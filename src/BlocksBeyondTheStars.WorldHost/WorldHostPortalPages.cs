// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.WorldHost;

/// <summary>
/// Server-rendered portal shells for the hosted-worlds control plane: landing (sign in / create account
/// with the required community-rules + beta acceptance), "My Worlds" management, and the bilingual rules
/// page. Self-contained like the per-instance PortalPage (inline CSS, no shipped assets); the pages talk
/// to /api with a Bearer session from localStorage. Deliberately compact — the polished experience is the
/// game itself; this portal manages worlds.
/// </summary>
public static class WorldHostPortalPages
{
    public static string Landing(WorldHostConfig config)
        => Shell("Blocks Beyond the Stars — Welten", $@"
<h1>Blocks Beyond the Stars — <span class='o'>Welten</span></h1>
<p class='sub'>Eigene Multiplayer-Welt erstellen und mit Freunden spielen. · <i>Create your own multiplayer world and play with friends.</i></p>
<div class='beta'>⚠ <b>Beta:</b> Gehostete Welten können jederzeit kaputtgehen oder verloren gehen — lade regelmäßig eine Sicherung herunter!
<br><i>Beta: hosted worlds can break or disappear at any time — download a backup regularly!</i></div>
<div class='cols'>
 <div class='card'>
  <h2>Anmelden · <i>Sign in</i></h2>
  <input id='li-name' placeholder='Name' maxlength='24'>
  <input id='li-pass' type='password' placeholder='Passwort · Password'>
  <button onclick='login()'>Anmelden</button>
  <div id='li-terms' style='display:none'>
    <p>Die Community-Regeln haben sich geändert. · <i>The community rules changed.</i></p>
    <label><input type='checkbox' id='li-accept'> Ich akzeptiere die <a href='/rules'>Regeln</a> · <i>I accept the <a href='/rules'>rules</a></i></label>
    <button onclick='reaccept()'>Weiter · Continue</button>
  </div>
 </div>
 <div class='card'>
  <h2>Konto erstellen · <i>Create account</i></h2>
  <input id='su-name' placeholder='Name (3-24: A-Z 0-9 - _)' maxlength='24'>
  <input id='su-pass' type='password' placeholder='Passwort (min. 8) · Password (min. 8)'>
  <p class='hint'>Kein E-Mail nötig. Merke dir dein Passwort gut — ohne E-Mail gibt es keine Wiederherstellung!<br>
  <i>No email needed. Remember your password — without an email there is no recovery!</i></p>
  <label><input type='checkbox' id='su-accept'> Ich akzeptiere die <a href='/rules'>Community-Regeln</a> und den Beta-Hinweis ·
  <i>I accept the <a href='/rules'>community rules</a> and the beta notice</i></label>
  <button onclick='signup()'>Konto erstellen</button>
 </div>
</div>
<div id='msg'></div>
<script>
const TERMS = __TERMS__;
function say(t){{document.getElementById('msg').textContent = t||'';}}
async function post(url, body){{
  const r = await fetch(url, {{method:'POST', headers:{{'Content-Type':'application/json'}}, body: JSON.stringify(body)}});
  let j = null; try {{ j = await r.json(); }} catch {{}}
  return {{ok: r.ok, status: r.status, j}};
}}
async function signup(){{
  if(!document.getElementById('su-accept').checked) return say('Bitte akzeptiere zuerst die Regeln. · Please accept the rules first.');
  const r = await post('/api/signup', {{name: v('su-name'), password: v('su-pass'), acceptedTermsVersion: TERMS}});
  if(!r.ok) return say((r.j&&r.j.error)||'Fehler · Error');
  localStorage.setItem('bbs_session', r.j.sessionToken); location.href='/worlds';
}}
let pendingSession = null;
async function login(){{
  const r = await post('/api/login', {{name: v('li-name'), password: v('li-pass'), acceptedTermsVersion: 0}});
  if(!r.ok) return say(r.status===401 ? 'Name oder Passwort falsch. · Wrong name or password.' : 'Fehler · Error');
  if(r.j.termsOutdated){{ pendingSession = r.j.sessionToken; document.getElementById('li-terms').style.display='block'; return; }}
  localStorage.setItem('bbs_session', r.j.sessionToken); location.href='/worlds';
}}
async function reaccept(){{
  if(!document.getElementById('li-accept').checked) return say('Bitte Regeln akzeptieren. · Please accept the rules.');
  await fetch('/api/accept-terms', {{method:'POST', headers:{{'Authorization':'Bearer '+pendingSession}}}});
  localStorage.setItem('bbs_session', pendingSession); location.href='/worlds';
}}
function v(id){{return document.getElementById(id).value.trim();}}
</script>".Replace("__TERMS__", config.TermsVersion.ToString()));

    public static string Worlds(WorldHostConfig config)
        => Shell("Meine Welten — Blocks Beyond the Stars", @"
<h1>Meine <span class='o'>Welten</span> <span class='sub'>· My Worlds</span></h1>
<div class='beta'>⚠ Beta: Welten können kaputtgehen oder verloren gehen — lade Sicherungen herunter! · <i>Beta: worlds can break or vanish — download backups!</i></div>
<div class='card'>
 <h2>Neue Welt · <i>New world</i></h2>
 <input id='w-name' placeholder='Weltname · World name' maxlength='40'>
 <button onclick='createWorld()'>Erstellen · Create</button>
</div>
<div id='list'></div>
<div class='card'>
 <h2>Spieler melden · <i>Report a player</i></h2>
 <p class='hint'>Jemand war gemein oder macht Mist? Sag uns Bescheid — wir schauen es uns an. · <i>Someone was mean or misbehaving? Tell us — we will look into it.</i></p>
 <input id='r-name' placeholder='Spielername · Player name' maxlength='24'>
 <select id='r-cat'><option value='chat'>Chat</option><option value='name'>Name</option><option value='griefing'>Zerstören · Griefing</option><option value='other'>Anderes · Other</option></select>
 <input id='r-msg' placeholder='Was ist passiert? · What happened?' maxlength='500'>
 <button onclick='report()'>Melden · Report</button>
</div>
<p><a href='/rules'>Regeln · Rules</a> · <a href='#' onclick=""localStorage.removeItem('bbs_session');location.href='/'"">Abmelden · Sign out</a></p>
<div id='msg'></div>
<script>
const S = localStorage.getItem('bbs_session');
if(!S) location.href='/';
const H = {'Authorization':'Bearer '+S, 'Content-Type':'application/json'};
function say(t){document.getElementById('msg').textContent = t||'';}
async function api(method, url, body){
  const r = await fetch(url, {method, headers:H, body: body?JSON.stringify(body):undefined});
  if(r.status===401){ location.href='/'; return null; }
  let j=null; try{ j=await r.json(); }catch{}
  if(!r.ok){ say((j&&j.error)||'Fehler · Error'); return null; }
  return j||{};
}
async function load(){
  const j = await api('GET','/api/worlds'); if(!j) return;
  const el = document.getElementById('list'); el.innerHTML='';
  for(const w of j.worlds){
    const d = document.createElement('div'); d.className='card world';
    d.innerHTML = `<h2>${esc(w.name)} <span class='st ${w.status}'>${w.status}</span></h2>
      <button onclick=""joinWorld('${w.id}')"">Spielen · Play</button>
      <button onclick=""stopWorld('${w.id}')"">Stoppen · Stop</button>
      <button onclick=""dlSave('${w.id}')"">Sicherung laden · Download save</button>
      <label class='up'>Save hochladen · Upload<input type='file' style='display:none' onchange=""upSave('${w.id}', this.files[0])""></label>
      <button class='danger' onclick=""delWorld('${w.id}', '${esc(w.name)}')"">Löschen · Delete</button>
      <div class='grant' id='g-${w.id}'></div>`;
    el.appendChild(d);
  }
  if(!j.worlds.length) el.innerHTML = ""<p class='hint'>Noch keine Welt — erstelle deine erste! · No world yet — create your first!</p>"";
}
async function createWorld(){
  const j = await api('POST','/api/worlds',{name: document.getElementById('w-name').value.trim()});
  if(j){ document.getElementById('w-name').value=''; say(''); load(); }
}
async function joinWorld(id){
  const name = prompt('Dein Spielername? · Your player name?'); if(!name) return;
  say('Welt wird gestartet… · Waking the world…');
  const j = await api('POST',`/api/worlds/${id}/join`,{playerName:name}); if(!j) return;
  say('');
  document.getElementById('g-'+id).innerHTML =
    `<p><b>Im Spiel beitreten · Join in game:</b> Host <code>${esc(j.nativeHost)}</code> Port <code>${j.nativePort}</code><br>
     Browser: <code>${esc(j.wssUrl)}</code><br>
     <span class='hint'>Token (2 min gültig · valid): <code>${esc(j.joinToken)}</code></span></p>`;
  load();
}
async function stopWorld(id){ if(await api('POST',`/api/worlds/${id}/stop`)) load(); }
async function delWorld(id,name){
  if(!confirm(`Welt '${name}' wirklich löschen? · Really delete world '${name}'?`)) return;
  if(await api('DELETE',`/api/worlds/${id}`)!==null) load();
}
async function dlSave(id){
  const r = await fetch(`/api/worlds/${id}/save`, {headers:{'Authorization':'Bearer '+S}});
  if(!r.ok){ let j=null; try{ j=await r.json(); }catch{}; return say((j&&j.error)||'Fehler · Error'); }
  const b = await r.blob(); const a = document.createElement('a');
  a.href = URL.createObjectURL(b); a.download = `${id}-world.db`; a.click(); URL.revokeObjectURL(a.href);
}
async function upSave(id, file){
  if(!file) return;
  say('Upload läuft… · Uploading…');
  const r = await fetch(`/api/worlds/${id}/save`, {method:'POST', headers:{'Authorization':'Bearer '+S}, body: file});
  let j=null; try{ j=await r.json(); }catch{}
  say(r.ok ? 'Save übernommen! · Save imported!' : ((j&&j.error)||'Fehler · Error'));
}
async function report(){
  const j = await api('POST','/api/reports',{reportedName:v('r-name'), category:document.getElementById('r-cat').value, message:v('r-msg')});
  if(j){ say('Danke für deine Meldung! · Thanks for your report!'); document.getElementById('r-name').value=''; document.getElementById('r-msg').value=''; }
}
function v(id){return document.getElementById(id).value.trim();}
function esc(s){return String(s).replace(/[&<>""']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'}[c]));}
load();
</script>");

    public static string Rules(WorldHostConfig config)
        => Shell("Community-Regeln — Blocks Beyond the Stars", $@"
<h1>Community-<span class='o'>Regeln</span> <span class='sub'>· Community Rules (v{config.TermsVersion})</span></h1>
<div class='card'>
 <h2>🇩🇪 Deutsch</h2>
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
</div>
<div class='card'>
 <h2>🇬🇧 English</h2>
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
</div>
<p><a href='/'>Zurück · Back</a></p>");

    /// <summary>Shared page chrome, styled after the per-instance portal (dark starfield, cyan/orange).</summary>
    private static string Shell(string title, string body) => $@"<!DOCTYPE html>
<html lang='de'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>{System.Net.WebUtility.HtmlEncode(title)}</title>
<style>
:root{{--cyan:#5fd7ff;--orange:#ff8c26;--line:#21304c}}
*{{box-sizing:border-box}}
body{{font-family:'Rajdhani','Segoe UI',system-ui,sans-serif;color:#dfe9f7;margin:0;padding:24px;min-height:100vh;
 background:radial-gradient(1100px 640px at 50% -12%,#15243f 0%,#070a12 58%),#070a12}}
main{{max-width:860px;margin:0 auto}}
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
.st{{font-size:.8rem;padding:2px 10px;border-radius:10px;border:1px solid var(--line);vertical-align:middle}}
.st.running{{color:#7dff9e;border-color:#2e7d44}} .st.stopped{{color:#9db2cf}} .st.starting{{color:var(--orange);border-color:#7a5218}}
code{{background:#0a101d;border:1px solid var(--line);border-radius:6px;padding:1px 6px;word-break:break-all}}
#msg{{margin:10px 0;color:var(--orange);min-height:1.2em}}
ul{{line-height:1.6}}
</style>
</head>
<body><main>{body}</main></body>
</html>";
}
