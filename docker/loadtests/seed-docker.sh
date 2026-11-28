#!/usr/bin/env bash
# Grows the seeded accounts of the Docker bench and makes the result the new baseline of every later run.
# Run it between test runs. It asks for the owner password unless BASIL_OWNER_PASSWORD is set.
#   ./docker/loadtests/seed-docker.sh            (2700 players, 200 referees, 400 tourney accounts)
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOCKER_HOST="${DOCKER_HOST:-unix:///var/run/docker.sock}"
export BENCH_DIR="${BENCH_DIR:-$HOME/basil-load}"
DUMP_FILE="${BASIL_LOAD_DUMP:-$REPO_ROOT/basil-seeded.dump}"
cd "$REPO_ROOT"

if pgrep -f "run-docker\.sh (run|scalability)" >/dev/null; then
  echo "A test run is in progress; seed after it ends." >&2
  exit 1
fi

if [ -z "${BASIL_OWNER_PASSWORD:-}" ]; then
  read -rsp "Password of the owner account (${OWNER_USER:-admin}): " BASIL_OWNER_PASSWORD
  echo
fi
export BASIL_OWNER_PASSWORD

# The earlier baseline is kept outside the repository, so a failed seed can be undone.
backup="$BENCH_DIR/backup/$(date -u +%Y%m%d-%H%M%S)"
mkdir -p "$backup"
cp "$DUMP_FILE" accounts.csv world-data.json "$backup/"

./docker/loadtests/run-docker.sh reset

echo "Seeding with the account creation limits raised for the duration of the seed."
dotnet run --project Basil.Tests.LoadTests -c Release -- \
  seed --connect 172.29.0.5 --port 8443 --domain basil.local --owner-user "${OWNER_USER:-admin}" \
  --accounts accounts.csv --world world-data.json \
  --players "${PLAYERS:-2700}" --referees "${REFEREES:-200}" --tourneys "${TOURNEYS:-400}"

docker compose -f docker/loadtests/compose.yaml exec -T postgres pg_dump -U basil -d basil -Fc >"$backup/new.dump"
# only a complete dump replaces the baseline
mv "$backup/new.dump" "$DUMP_FILE"

echo "New baseline: $DUMP_FILE ($(($(wc -l <accounts.csv) - 1)) accounts). Earlier files: $backup"
