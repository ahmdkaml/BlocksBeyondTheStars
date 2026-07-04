# Fleet deployment (`deploy/`) — VPS `bbs-host-1`

This folder is the source of truth for what runs on the hosted-worlds VPS
(31.70.113.90, Debian 13). `deploy.yml` (GitHub Actions, manual dispatch, `production`
environment with an approval gate) rsyncs these folders to `/opt/bbs/` and runs
`remote-deploy.sh` over SSH. Architecture: `docs/developer/HOSTED_WORLDS.md`; the
bug-report inbox: `docs/developer/REPORT_HOST.md`.

| Folder | Service | Image | Cadence |
|---|---|---|---|
| `caddy/` | caddy-docker-proxy (TLS + routing) | `lucaslorentz/caddy-docker-proxy` | rarely (config change) |
| `worldhost/` | hosted-worlds control plane | `ghcr.io/marceld23/blocks-beyond-the-stars-worldhost` (`worldhost-image.yml`) | on service change |
| `reports/` | bug-report inbox | `ghcr.io/marceld23/blocks-beyond-the-stars-reports` (`reports-image.yml`) | on service change |

Per-world game containers are NOT deployed from here — WorldHost starts them on demand from the
dedicated-server image pinned in `/opt/bbs/worldhost/.env` (`BBS_WH_SERVER_IMAGE`; that image is
built by `docker.yml` on release tags).

## Secrets model

- GitHub holds exactly **one** deploy secret: `DEPLOY_SSH_KEY` (environment `production`), a
  dedicated ed25519 key for the `bbs` user. The VPS host key is pinned in `deploy.yml`.
- **All service secrets live only on the host** in `/opt/bbs/<service>/.env` (mode 600, owner
  `bbs`) — created once from the `.env.example` files here. CI never reads or writes them;
  `remote-deploy.sh` rewrites only the `*_TAG` line when a deploy pins a new image version.

## Version pinning & rollback

The image workflows publish `:latest` plus an immutable `:sha-<short>` on every main push that
touches the service. Deploys should pin `sha-<short>` (dispatch input); rollback = rerun the deploy
with the previous sha. Rolling the game-server fleet = edit `BBS_WH_SERVER_IMAGE` in
`/opt/bbs/worldhost/.env` and redeploy `worldhost` — running worlds keep their image until their
idle shutdown, new wakes use the new pin.

## One-time host prerequisites (already done on bbs-host-1, 2026-07-04)

Deploy user `bbs` (docker group), ufw (22, 80, 443/tcp+udp, 32000-32999/udp), Docker Engine +
compose plugin, shared network `docker network create bbs-hosted`, `/opt/bbs/{caddy,worldhost,reports}`
with the real `.env` files, and the deploy public key in `~bbs/.ssh/authorized_keys`
(`no-agent-forwarding,no-port-forwarding,no-X11-forwarding`). DNS (Strato, manual): A `play`,
wildcard A `*.play` and A `reports` → the VPS.
