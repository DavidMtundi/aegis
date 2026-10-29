#!/usr/bin/env bash
# Smoke-test Postgres backup + restore for the local docker-compose database.
# Prerequisites: docker compose postgres healthy; pg_dump/psql available (or use docker exec).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
COMPOSE="${COMPOSE:-docker compose -f $ROOT/docker-compose.yml}"
CONTAINER="${POSTGRES_CONTAINER:-}"
DB_USER="${POSTGRES_USER:-aegis}"
DB_NAME="${POSTGRES_DB:-aegis}"
SMOKE_DB="${SMOKE_RESTORE_DB:-aegis_restore_smoke}"
WORKDIR="${BACKUP_WORKDIR:-$ROOT/.backup-smoke}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DUMP="$WORKDIR/aegis-$STAMP.dump"

mkdir -p "$WORKDIR"

if [[ -z "$CONTAINER" ]]; then
  # Prefer the compose service container if running.
  CONTAINER="$($COMPOSE ps -q postgres 2>/dev/null || true)"
fi

if [[ -z "$CONTAINER" ]]; then
  echo "Postgres container not found. Start with: (cd $ROOT && docker compose up -d postgres)" >&2
  exit 1
fi

echo "==> Dump $DB_NAME from $CONTAINER → $DUMP"
docker exec "$CONTAINER" pg_dump -U "$DB_USER" -Fc -d "$DB_NAME" > "$DUMP"
ls -lh "$DUMP"

echo "==> Recreate smoke database $SMOKE_DB"
docker exec -i "$CONTAINER" psql -U "$DB_USER" -d postgres -v ON_ERROR_STOP=1 <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$SMOKE_DB' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS $SMOKE_DB;
CREATE DATABASE $SMOKE_DB OWNER $DB_USER;
SQL

echo "==> Restore into $SMOKE_DB"
docker exec -i "$CONTAINER" pg_restore -U "$DB_USER" -d "$SMOKE_DB" --no-owner --role="$DB_USER" < "$DUMP"

echo "==> Verify table presence in restored DB"
TABLES="$(docker exec "$CONTAINER" psql -U "$DB_USER" -d "$SMOKE_DB" -Atc \
  "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema')")"
echo "Restored non-system tables: $TABLES"
if [[ "$TABLES" -lt 1 ]]; then
  echo "Restore produced no application tables" >&2
  exit 1
fi

echo "==> Cleanup smoke database (keep dump at $DUMP)"
docker exec -i "$CONTAINER" psql -U "$DB_USER" -d postgres -v ON_ERROR_STOP=1 <<SQL
DROP DATABASE IF EXISTS $SMOKE_DB;
SQL

echo "OK: backup/restore smoke passed."
