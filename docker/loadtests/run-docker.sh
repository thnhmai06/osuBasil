#!/usr/bin/env bash
set -euo pipefail

# The load test bench on this machine alone: server stack, load generator and control probe in Docker.
# Usage:
#   ./docker/loadtests/run-docker.sh build
#   ./docker/loadtests/run-docker.sh up
#   ./docker/loadtests/run-docker.sh reset
#   ./docker/loadtests/run-docker.sh run <smoke|pilot|load|stress|spike|soak|capacity|scalability> [extra args...]
#   ./docker/loadtests/run-docker.sh scalability [extra args...]
#
# Exit code of 'run': 0 the SLO held (or the test is meant to break it: capacity, pilot, stress, scalability),
# 3 the SLO broke, 1 the run itself broke.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
export DOCKER_HOST="${DOCKER_HOST:-unix:///var/run/docker.sock}"
export BENCH_DIR="${BASIL_LOAD_BENCH:-$HOME/basil-load}"
DUMP_FILE="${BASIL_LOAD_DUMP:-$REPO_ROOT/basil-seeded.dump}"
ACCOUNTS_FILE="${BASIL_LOAD_ACCOUNTS:-$REPO_ROOT/accounts.csv}"
WORLD_FILE="${BASIL_LOAD_WORLD:-$REPO_ROOT/world-data.json}"
REPORTS_ROOT="${BASIL_LOAD_REPORTS:-$REPO_ROOT/reports}"
PUBLISH_DIR="$REPO_ROOT/publish/loadtests-linux-x64"
RUNTIME_IMAGE="mcr.microsoft.com/dotnet/runtime-deps:10.0"
GENERATOR_CPUSET="${GENERATOR_CPUSET:-0,4}"
TARGET=(--connect 172.29.0.5 --port 8443 --irc-port 6697 --domain basil.local)

compose() { docker compose -f "$SCRIPT_DIR/compose.yaml" "$@"; }

write_nginx_conf() {
  mkdir -p "$BENCH_DIR/nginx-log"
  sed 's/basil\.basil-load\.svc\.cluster\.local/172.29.0.10/g' "$SCRIPT_DIR/nginx-load.conf" > "$BENCH_DIR/nginx.conf"
}

# Builds HEAD plus the uncommitted changes of tracked files into basil:load; untracked files are left out. A snapshot
# with changes is tagged by its own commit id, which 'git show' can read back.
cmd_build() {
  local source revision
  source="$(git -C "$REPO_ROOT" stash create)"
  source="${source:-HEAD}"
  revision="$(git -C "$REPO_ROOT" rev-parse --short "$source")"
  git -C "$REPO_ROOT" archive "$source" | docker build -t basil:load -t "basil:$revision" -
  echo "[build] basil:load is now $revision."
}

cmd_up() {
  write_nginx_conf
  compose up -d --wait --wait-timeout 300
}

cmd_reset() {
  [ -f "$DUMP_FILE" ] || { echo "Baseline dump '$DUMP_FILE' not found." >&2; exit 1; }
  write_nginx_conf

  echo "[reset] Stopping basil..."
  compose stop basil >/dev/null
  compose up -d --wait postgres

  echo "[reset] Restoring PostgreSQL baseline dump..."
  compose exec -T postgres pg_restore -U basil -d basil --clean --if-exists --no-owner < "$DUMP_FILE"

  # The server writes these folders as its own user, so a throwaway root container clears them; every run must start
  # from the same cold state.
  echo "[reset] Clearing Replays, Screenshots, uploaded Avatars, Cache, Logs and the nginx load log..."
  docker run --rm -v "$BENCH_DIR:/bench" busybox:1.36.1 sh -c '
    set -eu
    for d in /bench/Data/Replays /bench/Data/Screenshots /bench/Data/Avatars /bench/Cache /bench/Logs; do
      [ -d "$d" ] || continue
      find "$d" -mindepth 1 -delete
      [ -z "$(find "$d" -mindepth 1 | head -n 1)" ] || { echo "not cleared: $d" >&2; exit 1; }
    done
    : > /bench/nginx-log/access.log'

  echo "[reset] Starting the stack..."
  compose up -d --wait --wait-timeout 300
}

sample_infra() {
  local report_dir="$1" stop_file="$2"
  while [ ! -f "$stop_file" ]; do
    {
      echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
      docker stats --no-stream --format '{{.Name}} cpu={{.CPUPerc}} mem={{.MemUsage}} net={{.NetIO}}' || true
      sensors 2>/dev/null | grep -E 'Package|Core|temp' || true
      echo "mhz: $(grep 'cpu MHz' /proc/cpuinfo | awk '{printf "%d ", $4}')"
      echo "governor: $(cat /sys/devices/system/cpu/cpu*/cpufreq/scaling_governor | sort | uniq -c | tr -s ' \n' ' ')"
      free -m
    } >> "$report_dir/infra-metrics.log"
    {
      echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
      compose exec -T postgres psql -U basil -d basil -c "SELECT state, count(*) FROM pg_stat_activity GROUP BY state;" || true
    } >> "$report_dir/pg-activity.log" 2>&1
    sleep 15
  done
}

# Runs the load test program in a container on the bench network.
harness() {
  local name="$1" reports="$2"
  shift 2
  docker run --name "$name" --network basil-load --user "$(id -u):$(id -g)" --ulimit nofile=200000 \
    -e HOME=/tmp -e DOTNET_DbgEnableMiniDump=1 -e DOTNET_EnableCrashReport=1 -e DOTNET_DbgMiniDumpType=1 \
    -e DOTNET_DbgMiniDumpName=/reports/core.%p \
    -v "$PUBLISH_DIR:/bin-host:ro" -v "$BENCH_DIR/run-data:/data:ro" -v "$reports:/reports" -w /tmp \
    "$@"
}

cmd_run() {
  local test_name="$1"
  shift
  local run_id="${test_name}-$(date -u +%Y%m%d-%H%M%S)"
  local report_dir="$REPORTS_ROOT/$run_id"
  mkdir -p "$report_dir/manifests" "$report_dir/control"

  echo "[publish] Building self-contained Basil.Tests.LoadTests (linux-x64)..."
  dotnet publish "$REPO_ROOT/Basil.Tests.LoadTests/Basil.Tests.LoadTests.csproj" \
    -c Release -r linux-x64 --self-contained true -p:DebugType=none -o "$PUBLISH_DIR" --nologo -v q

  local planned
  planned="$("$PUBLISH_DIR/Basil.Tests.LoadTests" plan "$test_name" "$@")"
  if ! [[ "$planned" =~ ^[0-9]+$ ]]; then
    echo "Could not read the planned duration of '$test_name $*': $planned" >&2
    return 1
  fi

  # The control probe signs in as the first player; the generator's list leaves that account out, or the two would
  # replace each other's sessions.
  mkdir -p "$BENCH_DIR/run-data"
  cp "$ACCOUNTS_FILE" "$BENCH_DIR/run-data/control.csv"
  awk -F, '!skipped && $3 == "player" { skipped = 1; next } { print }' "$ACCOUNTS_FILE" > "$BENCH_DIR/run-data/accounts.csv"
  cp "$WORLD_FILE" "$BENCH_DIR/run-data/world-data.json"

  cmd_reset

  cp "$SCRIPT_DIR/compose.yaml" "$BENCH_DIR/nginx.conf" "$report_dir/manifests/"
  {
    git -C "$REPO_ROOT" log -1 --format='HEAD %h %s'
    echo "uncommitted changes of the harness and bench:"
    git -C "$REPO_ROOT" status --short
    docker image inspect basil:load --format 'image {{.Id}} {{join .RepoTags " "}}'
    for service in basil postgres nginx; do
      echo "$service cpuset $(compose exec -T "$service" cat /sys/fs/cgroup/cpuset.cpus.effective)"
    done
    echo "generator cpuset $GENERATOR_CPUSET"
    lscpu
  } > "$report_dir/manifests/bench.txt"

  local stop_file="$report_dir/.stop-sampler"
  sample_infra "$report_dir" "$stop_file" &
  local sampler_pid=$!

  local generator="gen-$run_id" control="ctrl-$run_id"
  echo "[run] Starting the control probe..."
  harness "$control" "$report_dir/control" -d --cpus 1 --memory 1g "$RUNTIME_IMAGE" \
    /bin-host/Basil.Tests.LoadTests control "${TARGET[@]}" --accounts /data/control.csv \
    --world /data/world-data.json --duration "$((planned + 180))s" --reports /reports >/dev/null

  echo "[run] Running $generator (planned ${planned}s)..."
  local exit_code=0
  harness "$generator" "$report_dir" --cpuset-cpus "$GENERATOR_CPUSET" --memory 2g "$RUNTIME_IMAGE" \
    /bin-host/Basil.Tests.LoadTests "$test_name" "$@" "${TARGET[@]}" --accounts /data/accounts.csv \
    --world /data/world-data.json --reports /reports > "$report_dir/job-loadtests.log" 2>&1 || exit_code=$?

  touch "$stop_file"
  wait "$sampler_pid" 2>/dev/null || true
  docker logs "$control" > "$report_dir/control/control.log" 2>&1 || true
  docker inspect "$generator" --format 'generator exit={{.State.ExitCode}} oom={{.State.OOMKilled}}' \
    >> "$report_dir/manifests/bench.txt" || true
  docker rm -f "$control" "$generator" >/dev/null 2>&1 || true

  cp "$BENCH_DIR/nginx-log/access.log" "$report_dir/nginx-load.log" || echo "[collect] nginx log missing" >&2
  # The next reset empties the server's logs, so each run keeps its own copy.
  cp -r "$BENCH_DIR/Logs" "$report_dir/basil-logs" || echo "[collect] basil logs incomplete" >&2
  compose logs --no-color basil > "$report_dir/basil-console.log" 2>&1 || true
  rm -f "$stop_file"

  local result=1
  case "$exit_code" in
    0) result=0 ;;
    3)
      case "$test_name" in
        capacity | pilot | stress | scalability) result=0 ;; # these are meant to break the SLO
        *) result=3 ;;
      esac
      ;;
  esac

  echo "[done] Report collected in $report_dir (container exit $exit_code, run result $result)."
  return "$result"
}

cmd_scalability() {
  local pinned
  for pinned in "1:1" "2:1,5" "4:1,2,5,6"; do
    echo "[scalability] Pinning Basil to CPU(s) ${pinned#*:}..."
    BASIL_CPUSET="${pinned#*:}" cmd_run scalability --cores "${pinned%%:*}" "$@" || true
  done
  compose up -d --wait basil
}

case "${1:-}" in
  build) shift; cmd_build ;;
  up) shift; cmd_up ;;
  reset) shift; cmd_reset ;;
  run) shift; cmd_run "$@" ;;
  scalability) shift; cmd_scalability "$@" ;;
  *)
    echo "Usage: $0 {build|up|reset|run <test> [args...]|scalability [args...]}" >&2
    exit 2
    ;;
esac
