#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ── helpers ────────────────────────────────────────────────────────────────────
log()  { printf '\033[1;34m[start]\033[0m %s\n' "$*"; }
err()  { printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2; }
ok()   { printf '\033[1;32m[ok]\033[0m %s\n' "$*"; }

cleanup() {
  log "Shutting down…"
  kill "$BACKEND_PID" "$FRONTEND_PID" 2>/dev/null || true
  docker compose -f "$ROOT/docker-compose.yml" stop postgres 2>/dev/null || true
}
trap cleanup EXIT INT TERM

# ── preflight ──────────────────────────────────────────────────────────────────
for cmd in dotnet node npm docker; do
  if ! command -v "$cmd" &>/dev/null; then
    err "'$cmd' not found — install it and retry"
    exit 1
  fi
done

# ── .env ──────────────────────────────────────────────────────────────────────
if [[ ! -f "$ROOT/.env" ]]; then
  log ".env not found — copying from .env.example"
  cp "$ROOT/.env.example" "$ROOT/.env"
  log "Edit $ROOT/.env with real values before running in production"
fi

set -a
# shellcheck source=/dev/null
source "$ROOT/.env"
set +a

# ── postgres ───────────────────────────────────────────────────────────────────
log "Starting postgres…"
docker compose -f "$ROOT/docker-compose.yml" up -d postgres

log "Waiting for postgres to be healthy…"
for i in $(seq 1 30); do
  if docker compose -f "$ROOT/docker-compose.yml" exec -T postgres \
      pg_isready -U "${POSTGRES_USER:-ai_job_search_agent}" -d "${POSTGRES_DB:-ai_job_search_agent}" \
      &>/dev/null; then
    ok "Postgres ready"
    break
  fi
  if [[ $i -eq 30 ]]; then
    err "Postgres did not become healthy in time"
    exit 1
  fi
  sleep 1
done

# ── backend (MCP server in HTTP mode) ─────────────────────────────────────────
log "Building backend…"
dotnet build "$ROOT/AiJobSearchAgent.slnx" --configuration Debug --nologo -v q

log "Starting backend (HTTP mode, port 5001)…"
dotnet run \
  --project "$ROOT/src/AiJobSearchAgent.McpServer/AiJobSearchAgent.McpServer.csproj" \
  --configuration Debug \
  --no-build \
  -- --http &
BACKEND_PID=$!
ok "Backend PID $BACKEND_PID"

log "Waiting for backend on port 5001…"
for i in $(seq 1 30); do
  if curl -sf http://localhost:5001/api/config &>/dev/null; then
    ok "Backend ready"
    break
  fi
  if ! kill -0 "$BACKEND_PID" 2>/dev/null; then
    err "Backend process exited unexpectedly"
    exit 1
  fi
  if [[ $i -eq 30 ]]; then
    err "Backend did not start in time"
    exit 1
  fi
  sleep 1
done

# ── frontend ───────────────────────────────────────────────────────────────────
log "Installing frontend dependencies…"
npm --prefix "$ROOT/frontend" install --silent

log "Starting frontend (Vite, port 5173)…"
npm --prefix "$ROOT/frontend" run dev &
FRONTEND_PID=$!
ok "Frontend PID $FRONTEND_PID"

# ── done ───────────────────────────────────────────────────────────────────────
echo ""
ok "All services running"
echo "  Frontend  →  http://localhost:5173"
echo "  Backend   →  http://localhost:5001"
echo "  Postgres  →  localhost:5432"
echo ""
echo "Press Ctrl+C to stop."

wait "$BACKEND_PID" "$FRONTEND_PID"
