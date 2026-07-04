#!/bin/bash
# Runs ON the VPS as the `bbs` user — deploy.yml pipes this script through ssh after syncing the
# deploy/ folders to /opt/bbs/. Args: <service: all|caddy|worldhost|reports> [worldhost_tag] [reports_tag]
#
# Tags pin immutable image versions (sha-<short> from the image workflows). An empty tag keeps
# whatever is currently pinned in the service's .env — the .env files themselves (which also hold the
# operator secrets) are never replaced, only their *_TAG line is rewritten.
set -euo pipefail

SERVICE="${1:?usage: remote-deploy.sh <all|caddy|worldhost|reports> [worldhost_tag] [reports_tag]}"
WORLDHOST_TAG="${2:-}"
REPORTS_TAG="${3:-}"

pin_tag() { # <env-file> <key> <value> — rewrite one KEY= line, leave the rest (secrets!) untouched
  local file="$1" key="$2" value="$3"
  [ -z "$value" ] && return 0
  if grep -q "^${key}=" "$file"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$file"
  else
    echo "${key}=${value}" >> "$file"
  fi
  echo "pinned ${key}=${value}"
}

wait_healthy() { # <container> — wait for the image's HEALTHCHECK to report healthy
  local name="$1" status
  for _ in $(seq 1 45); do
    status=$(docker inspect -f '{{.State.Health.Status}}' "$name" 2>/dev/null || echo missing)
    if [ "$status" = "healthy" ]; then echo "${name}: healthy"; return 0; fi
    if [ "$status" = "unhealthy" ]; then break; fi
    sleep 2
  done
  echo "ERROR: ${name} did not become healthy (status: ${status})"
  docker logs --tail 40 "$name" 2>&1 || true
  return 1
}

require_env() { # <dir> — the operator .env must exist before the first deploy of that service
  if [ ! -f "$1/.env" ]; then
    echo "ERROR: $1/.env missing — create it from the .env.example in the repo's deploy/ folder."
    return 1
  fi
}

deploy_caddy() {
  cd /opt/bbs/caddy
  docker compose pull -q
  docker compose up -d
  sleep 3
  curl -fsS -m 5 http://127.0.0.1/ >/dev/null && echo "caddy: port 80 OK"
}

deploy_worldhost() {
  require_env /opt/bbs/worldhost
  cd /opt/bbs/worldhost
  pin_tag .env WORLDHOST_TAG "$WORLDHOST_TAG"
  docker compose pull -q
  docker compose up -d
  wait_healthy bbs-worldhost
}

deploy_reports() {
  require_env /opt/bbs/reports
  cd /opt/bbs/reports
  pin_tag .env REPORTS_TAG "$REPORTS_TAG"
  docker compose pull -q
  docker compose up -d
  wait_healthy bbs-reports
}

case "$SERVICE" in
  caddy)     deploy_caddy ;;
  worldhost) deploy_worldhost ;;
  reports)   deploy_reports ;;
  all)       deploy_caddy; deploy_worldhost; deploy_reports ;;
  *)         echo "unknown service: $SERVICE"; exit 2 ;;
esac

echo "DEPLOY OK (${SERVICE})"
