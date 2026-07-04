# Hosted Worlds — Control Plane, Routing & Lifecycle

Status: Phase 0 (server foundations) and Phase 1 (control-plane MVP) implemented; client/portal UX
(Phase 2) and quotas hardening/multi-host (Phase 3) are open. This document is the architecture
reference for the "hosted worlds" feature: players create persistent multiplayer worlds (optionally
from an uploaded singleplayer save, Phase 2) that run as **one dedicated-server container per world**
behind a control plane — the Minecraft-Realms model, adapted to our stack.

The three hosting tiers, side by side:

| Tier | Who runs it | Cost to us | Client entry |
|---|---|---|---|
| Singleplayer | bundled child-process server (ADR 0005) | none | unchanged |
| Self-hosting (LAN/Docker) | the player/community, SELF_HOSTING.md | none | unchanged |
| **Hosted worlds** | **our fleet host** | compute + egress | native menu "Official worlds" (Phase 2) |

## Components

```text
                    ┌──────────────────────────── VPS (Docker host) ────────────────────────────┐
 players ── https ─►│ Caddy (caddy-docker-proxy)                                                │
                    │   ├─ play.blocksbeyondthestars.de      → WorldHost (portal + API)         │
                    │   └─ w-<id>.play.blocksbeyondthestars.de → that world's WS gateway :31415 │
                    │                                                                           │
                    │ WorldHost (src/BlocksBeyondTheStars.WorldHost)                            │
                    │   accounts + sessions + world registry (SQLite)                           │
                    │   orchestrator: route-or-wake, join grants, reaper                        │
                    │   docker CLI: one container per world                                     │
                    │                                                                           │
                    │ bbs-world-<id> containers (the normal dedicated-server image)             │
                    │   volume bbs-world-<id>-saves:/app/saves                                  │
 native UDP ───────►│   host port 3200x → 31415/udp (gameplay), 127.0.0.1:3200x → /status probe │
                    └───────────────────────────────────────────────────────────────────────────┘
```

- **WorldHost** (`src/BlocksBeyondTheStars.WorldHost`) — the control plane: accounts (name +
  PBKDF2 password hash, deliberately no email — privacy-minimal for a kid-facing free tier), bearer
  sessions, the world registry, wake-on-demand allocation and join-token issuing. SQLite registry at
  `worldhost/worldhost.db`. Configured via `BBS_WH_*` env vars (see `WorldHostConfig`); **all quota
  values are operator config, never player-facing settings**: worlds/account (2), max players (12),
  idle minutes (20).
- **Per-world instances** — the unmodified dedicated-server image. The Phase-0 server features make
  them fleet-ready: `BBS_IDLE_SHUTDOWN_MINUTES` (empty world saves + exits → sleeping worlds cost
  ~nothing), `GET /status` on the WS gateway (live joined count for the reaper/allocator),
  `BBS_JOIN_TOKEN_SECRET` (only control-plane-vouched joins get in), `BBS_WORLD_OWNER` (the owner
  account gets WorldAdmin even on an uploaded save with a foreign first-joiner admin).
  **Containers run with `--restart=no`** — an auto-restart policy would wake idle-stopped worlds
  right back up.
- **Join flow** — `POST /api/worlds/{id}/join {playerName}` (Bearer session) → orchestrator ensures
  the instance runs (fast-path route, or `docker run` + poll `/status` until healthy, default 90 s
  budget) → returns a `JoinGrant`: `wssUrl` (browser), `nativeHost`/`nativePort` (desktop UDP) and a
  **2-minute HMAC join token** (`HostedJoinToken`, bound to world + account + player name). The
  instance verifies tokens offline — a control-plane outage never locks players out of a running world.
- **Reaper** — every 30 s reconciles registry vs Docker: instances that exited themselves (idle
  shutdown is the normal path) are marked `stopped`, so lists stay truthful and the next join wakes them.

## Routing, DNS & certificates (decision + Strato specifics)

Decided: **wildcard subdomains** (`w-<id>.play.blocksbeyondthestars.de`), NOT path routing — the
browser client needs zero changes (it already picks ws/wss from the page and takes `server_host`
from the URL), and every world is its own origin. Port-per-instance is not browser-viable (mixed
content + non-443 ports are blocked in school/corporate networks).

DNS lives at **Strato, which has no DNS API**, so Caddy's DNS-challenge wildcard certificate is not
available directly. Two options, in preference order:

1. **On-demand TLS (default for MVP, no second provider needed).** At Strato: one wildcard **A
   record** `*.play` → the VPS IP (plus `play` itself). Caddy issues a certificate per subdomain on
   first request via HTTP-01. Abuse guard: Caddy's `on_demand_tls { ask }` is pointed at WorldHost's
   `GET /ask?domain=…`, which answers 200 **only** for the portal host and subdomains of worlds that
   exist in the registry — nobody can burn our Let's Encrypt rate limits by aiming random names at
   the IP. Trade-off: the first-ever join of a world pays ~1–2 s of certificate issuance.
2. **Subzone delegation (fallback if issuance latency/rate limits ever bite).** At Strato, delegate
   `play` via NS records to a free API-capable DNS zone (Cloudflare / Hetzner DNS); then Caddy's
   DNS-challenge mints one wildcard certificate and on-demand TLS is turned off.

Native desktop clients use UDP and bypass Caddy entirely: each world publishes its stable host port
(`3200x → 31415/udp`) and clients connect to `PublicHost:port` from the join grant. The TCP side of
that port binds to loopback only — it exists for WorldHost's `/status` probe; public wss goes
through Caddy.

## Client integration (implemented for the native client)

- **Native client (desktop):** the main menu offers BOTH worlds — self-hosting exactly as today
  (Singleplayer / Host (LAN) / Join-by-address) AND **"Official Worlds"** (`UiMainMenu`, native-only
  `#if` branch): sign in with the portal account (session stored in `ClientSettings.PortalSessionToken`,
  never the password; `PortalUrl` empty = the official portal, settable for self-hosted WorldHosts),
  list your worlds, one click joins — the join grant's `nativeHost:nativePort` + `joinToken` feed the
  existing connect path (`AppShell.HostedToken` → `GameBootstrap` → `JoinRequest.HostedToken`). The
  HTTP side is `Client.Core/Portal/PortalClient` (mirrors FeedbackUploader: HttpClient, sync, never
  throws, testable headless). Manual/SP/LAN joins explicitly clear the hosted token.
- **In-game report button:** on hosted worlds (portal session + hosted join present) every player row
  in the Alliances → find-players list carries a "Report" button — one tap files a report via
  `POST /api/reports`, the button becomes the confirmation. Absent everywhere else.
- **One-time welcome (MOTD):** on a hosted world's FIRST join of a player, the server sends one
  bilingual system line (be kind + rules + beta notice) — `PlayerState.HostedWelcomeShown` persists
  so it never repeats. Keyed on the join-token gate, so self-hosted servers are unaffected.
- **Web client:** NO server choice, ever. The browser client is always bound to whoever serves it:
  a self-hosted Docker's `/play` page points at that same installation's server (exactly as today,
  via the portal deep-link parameters), and the official portal's pages point at the official
  hosted worlds. The WebGL menu never grows a server picker.

## Reserved developer names

`WorldHostConfig.ReservedNames` (override: `BBS_WH_RESERVED_NAMES`, comma-separated) protects the
developers' identities — default list: Marcel, Justus, Verena, juju, JuMaVe Games, FlashMiner,
JustusJulius, BloddyMary. Enforced at **two** layers:

1. **Account signup** — a reserved name registers only with the operator's claim code
   (`BBS_WH_RESERVED_CLAIM_CODE`, checked constant-time; unset ⇒ unclaimable). A successful claim
   permanently flags the account `is_developer`.
2. **In-game player name on hosted worlds** — the join grant refuses reserved names for
   non-developer accounts, so nobody can *play as* "Justus" on any hosted world regardless of what
   their account is called.

Matching is normalized — lowercase, with spaces/`-`/`_` stripped — so "ju ju", "J_ustus" and
"JuMaVeGames" are all caught. Self-hosted servers are out of scope (operators control their own
worlds; the per-save name-token claim still applies there).

## Community rules, beta notice, reports & bans (implemented server-side)

Not a legal EULA (the software is AGPL; this covers the hosted SERVICE) — a lightweight,
kid-friendly acceptance + enforcement loop:

- **Rules acceptance at signup** — the portal signup requires the checkbox and sends
  `acceptedTermsVersion`; the registry stores version + timestamp per account. The bilingual rules
  live at `/rules` (family project, be kind; **no hate, bullying, racism, insults → immediate
  ban**; never share personal data; beta notice). When the operator bumps
  `BBS_WH_TERMS_VERSION`, logins report `termsOutdated` and world actions are refused until the
  account re-accepts (`POST /api/accept-terms`).
- **Beta notice** — on the landing page, the worlds page and in the rules: hosted worlds/saves can
  break or disappear at any time; download backups (the export endpoint is the answer).
- **Player reports ("Spieler melden")** — `POST /api/reports` (Bearer): reported in-game name,
  category (chat / name / griefing / other), optional message (capped 500 chars), optional world.
  Reports are reviewed manually — nothing auto-punishes. Operator review via the admin API
  (`X-Admin-Token`, enabled by `BBS_WH_ADMIN_TOKEN`): `GET /api/admin/reports`,
  `POST /api/admin/reports/{id}/close`, `POST /api/admin/ban` (ban/unban + reason).
- **Bans** — `banned` flag + reason on the account; the join grant (the choke point every hosted
  entry passes) refuses banned accounts with the reason. Banned players can still file reports.
- Still open (client-side, Phase 2 rest): in-game report button on hosted worlds, first-join
  welcome MOTD, **Impressum + Datenschutzerklärung** pages before public launch (DSGVO — say the
  minimal-data story out loud: name + password hash, no email).

## Save upload / export (implemented)

The SP↔hosted round-trip. Instances bind-mount `<BBS_WH_WORLDS_DIR>/<worldId>/saves` at
`/app/saves` (a bind mount, not a named volume, precisely so WorldHost can do this):

- `POST /api/worlds/{id}/save` (owner, world stopped, raw `world.db` bytes): streamed to a temp
  file with a hard cap (`BBS_WH_UPLOAD_MAX_BYTES`, default 50 MB), then validated — SQLite magic
  header, `PRAGMA quick_check`, and the game schema anchor (`world_meta` table) — before it
  replaces the live file; the previous generation is kept as `world.db.bak`.
- `GET /api/worlds/{id}/save` (owner, world stopped): downloads the current `world.db` — the
  backup path the beta notice points players to, and the way back into singleplayer.

## Security notes

- WorldHost owns the Docker socket ⇒ root-equivalent. Everything that reaches `docker run` is
  server-generated (world ids: 12 hex chars, validated everywhere) or passed strictly as an **env
  value** via `ProcessStartInfo.ArgumentList` (argv-level, no shell) — display names never become
  arguments. Keep it that way.
- Sessions are stored hashed (SHA-256); passwords PBKDF2-SHA256/210k. Login failures are uniform
  (no name-exists oracle). Lost password = lost account for now — recovery UX is a Phase-2 concern.
- Join tokens live 120 s and name one world + one player; the per-world secret never leaves the
  host (it is injected into the instance's env).
- Deleting a world stops the container and removes the registry row but **keeps the saves volume**
  (operator-recoverable); automated retention/archival is Phase 3.

## Operations quick reference

Deployment is codified in **`deploy/`** (compose files for caddy / worldhost / reports — the source
of truth for `/opt/bbs/` on the VPS, see `deploy/README.md`) and two GitHub Actions workflows:
`worldhost-image.yml` builds `ghcr.io/marceld23/blocks-beyond-the-stars-worldhost` on main pushes
(`:latest` + immutable `:sha-<short>`), and `deploy.yml` (manual dispatch, `production` environment
with approval gate, single `DEPLOY_SSH_KEY` secret) rsyncs `deploy/` to the host and runs
`remote-deploy.sh` with per-service health checks. All operator secrets (claim code, admin token,
report keys) live only in `/opt/bbs/<service>/.env` on the host — never in CI.

```bash
# One-time host setup: shared network + caddy-docker-proxy with on-demand TLS ask endpoint
docker network create bbs-hosted
# Caddy global option:  on_demand_tls { ask http://worldhost:31417/ask }   (deploy/caddy/BaseCaddyfile)

# WorldHost operator env → /opt/bbs/worldhost/.env (template: deploy/worldhost/.env.example)
BBS_WH_BASE_DOMAIN=play.blocksbeyondthestars.de
BBS_WH_PUBLIC_HOST=play.blocksbeyondthestars.de
BBS_WH_SERVER_IMAGE=ghcr.io/marceld23/blocks-beyond-the-stars-server:0.6.2   # fleet version pin (WP14)
# quotas (operator policy): BBS_WH_MAX_WORLDS_PER_ACCOUNT=2, BBS_WH_MAX_PLAYERS=12, BBS_WH_IDLE_MINUTES=20
```

Instances the control plane starts carry caddy-docker-proxy labels
(`caddy=w-<id>.<domain>`, `caddy.reverse_proxy={{upstreams 31415}}`), so routing appears/disappears
with the container — no proxy config to maintain.

## Registry storage: SQLite now, Postgres when it earns it

The registry is SQLite on purpose: one host, one writer process, a tiny write volume, backup = one
file — and it matches the repo's SQLite-first philosophy. Note the distinction: **world saves** can
already live in PostgreSQL today (`BBS_DATABASE_PROVIDER=postgresql`, one schema per world — the
game supports it since PR #116); that is orthogonal to the registry. Move the REGISTRY to Postgres
when one of these becomes true: a second control-plane node (HA), multi-host placement (Phase 3),
or the wish to join registry + world data in one operational database. `HostRegistry` is a single
class behind plain methods, so swapping the backend later is contained.

## Phase 3 hardening (implemented)

- **Archive after inactivity.** `BBS_WH_ARCHIVE_MONTHS` (default 6, 0 = off): an hourly sweep moves
  a stopped world's saves to `<WorldsDir>/_archive/<id>/` and flips its status to `archived` once its
  last activity (join/wake or reaper stamp — `world.last_active_unix`) is older than the window.
  Joining an archived world **transparently restores it** (rename back + normal wake) — from the
  player's side it is just a world that takes a moment longer. Deleting stays separate; archive never
  destroys data. Ports remain allocated to archived worlds (the range bounds total worlds at
  `PortRangeSize`; revisit only if that ever pinches).
- **Rate limits** (fixed windows, in-memory, operator knobs): signups 5/h and logins 10/min per
  client IP (real IP via X-Forwarded-For — the app now honors forwarded headers from Caddy), save
  uploads 6/h and reports 10/h per account. Over-budget calls get HTTP 429 with a friendly text.
- **Blocked-name hygiene** (kid-facing): a short, unambiguous word list (operator-extendable via
  `BBS_WH_BLOCKED_WORDS`) is enforced on account names, world names AND in-game player names at the
  join grant — matched with the same normalization as reserved names, so separator tricks are caught.
  Deliberately minimal to avoid Scunthorpe-style false positives; the report button covers the rest.
- **Prometheus metrics.** `GET /metrics` (loopback only — Caddy does not route it): gauges
  (`bbs_accounts_total`, `bbs_worlds{status=…}`, `bbs_reports_open`) + counters (joins granted,
  wakes, reaped, archived, rate-limited).

## Version policy (fleet)

The fleet pins one image tag (`BBS_WH_SERVER_IMAGE`) — all instances run the same server version.
`Protocol.Version` gates incompatible clients at join (the server rejects mismatches with a clear
message). Save-schema changes must remain load-compatible with saves one release older (uploads are
validated but never migrated silently); document per release in the changelog.

## Open (tracked in the plan)

- Browser play for hosted worlds: a central `/play` on the portal that deep-links the WebGL client
  at `w-<id>` subdomains (incl. passing the join token through the page URL).
- **Impressum + Datenschutzerklärung** pages before public launch (operator-specific content).
- End-to-end playtest against a real Docker fleet (everything below the HTTP layer is unit-tested;
  the docker CLI path and Caddy routing need one real run on the VPS).
- Multi-host placement when one host stops being enough (spawner agent per node; registry → Postgres).
