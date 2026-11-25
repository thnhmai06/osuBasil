#!/usr/bin/env bash
set -euo pipefail

# Orchestrates the k3s load test stack on mocha, cappuccino and espresso.
# Usage:
#   ./docker/loadtests/run.sh deploy
#   ./docker/loadtests/run.sh seed --owner-pass <password> [extra seed args...]
#   ./docker/loadtests/run.sh reset
#   ./docker/loadtests/run.sh run <smoke|pilot|load|stress|spike|soak|capacity|scalability> [extra args...]
#   ./docker/loadtests/run.sh scalability [extra args...]

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
K3S_DIR="$SCRIPT_DIR/k3s"
NS="basil-load"

MOCHA_IP="${MOCHA_IP:-192.168.27.124}"
CAPPUCCINO_SSH="${CAPPUCCINO_SSH:-cappuccino@192.168.27.151}"
ESPRESSO_SSH="${ESPRESSO_SSH:-espresso@192.168.27.150}"
CAPPUCCINO_HOME="${CAPPUCCINO_HOME:-/home/cappuccino}"
ESPRESSO_HOME="${ESPRESSO_HOME:-/home/espresso}"

DATA_DIR="${BASIL_LOAD_DATA:-/home/haith/Desktop/osuBasil Data}"
DUMP_FILE="${BASIL_LOAD_DUMP:-$REPO_ROOT/basil-seeded.dump}"
ACCOUNTS_FILE="${BASIL_LOAD_ACCOUNTS:-$REPO_ROOT/accounts.csv}"
WORLD_FILE="${BASIL_LOAD_WORLD:-$REPO_ROOT/world-data.json}"
REPORTS_ROOT="${BASIL_LOAD_REPORTS:-$REPO_ROOT/reports}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-basil-load-secret}"

publish_binary() {
  local out_dir="$REPO_ROOT/publish/loadtests-linux-x64"
  echo "[publish] Building self-contained Basil.Tests.LoadTests (linux-x64)..."
  dotnet publish "$REPO_ROOT/Basil.Tests.LoadTests/Basil.Tests.LoadTests.csproj" \
    -c Release -r linux-x64 --self-contained true -p:DebugType=none -o "$out_dir"

  echo "[sync] Copying binary and seed files to cappuccino and espresso..."
  for host in "$CAPPUCCINO_SSH" "$ESPRESSO_SSH"; do
    ssh "$host" "mkdir -p ~/loadtests/bin ~/loadtests/data ~/loadtests/reports"
    scp -q -r "$out_dir"/* "$host:~/loadtests/bin/"
    [ -f "$ACCOUNTS_FILE" ] && scp -q "$ACCOUNTS_FILE" "$host:~/loadtests/data/accounts.csv"
    [ -f "$WORLD_FILE" ] && scp -q "$WORLD_FILE" "$host:~/loadtests/data/world-data.json"
  done
}

cmd_deploy() {
  kubectl apply -f "$K3S_DIR/namespace.yaml"

  kubectl -n "$NS" create secret generic basil-load-secrets \
    --from-literal="postgres-password=$POSTGRES_PASSWORD" \
    --dry-run=client -o yaml | kubectl apply -f -

  kubectl -n "$NS" create configmap nginx-load-conf \
    --from-file="nginx-load.conf=$SCRIPT_DIR/nginx-load.conf" \
    --from-file="proxy.conf=$REPO_ROOT/docker/nginx/proxy.conf" \
    --from-file="real-ip.conf=$REPO_ROOT/docker/nginx/real-ip.conf" \
    --dry-run=client -o yaml | kubectl apply -f -

  kubectl -n "$NS" create configmap nginx-certs \
    --from-file="$REPO_ROOT/docker/nginx/certs" \
    --dry-run=client -o yaml | kubectl apply -f -

  kubectl apply -f "$K3S_DIR/postgres.yaml"
  kubectl -n "$NS" rollout status statefulset/postgres --timeout=120s

  kubectl apply -f "$K3S_DIR/basil.yaml"
  kubectl -n "$NS" rollout status deployment/basil --timeout=180s

  kubectl apply -f "$K3S_DIR/nginx.yaml"
  kubectl apply -f "$K3S_DIR/iperf3.yaml"
  kubectl -n "$NS" rollout status deployment/nginx --timeout=120s
  echo "[deploy] Stack is up in namespace $NS."
}

cmd_seed() {
  echo "[seed] Running T2 seeder against $MOCHA_IP:8443..."
  dotnet run --project "$REPO_ROOT/Basil.Tests.LoadTests/Basil.Tests.LoadTests.csproj" -c Release -- \
    seed --connect "$MOCHA_IP" --port 8443 --domain basil.local \
    --accounts "$ACCOUNTS_FILE" --world "$WORLD_FILE" "$@"

  echo "[seed] Taking baseline pg_dump -> $DUMP_FILE..."
  kubectl -n "$NS" exec statefulset/postgres -- pg_dump -U basil -d basil -Fc > "$DUMP_FILE"
  echo "[seed] Done."
}

cmd_reset() {
  if [ ! -f "$DUMP_FILE" ]; then
    echo "Baseline dump '$DUMP_FILE' not found. Run 'seed' first." >&2
    exit 1
  fi

  echo "[reset] Scaling basil to 0..."
  kubectl -n "$NS" scale deployment/basil --replicas=0
  kubectl -n "$NS" wait --for=delete pod -l app=basil --timeout=120s || true

  echo "[reset] Restoring PostgreSQL baseline dump..."
  kubectl -n "$NS" exec -i statefulset/postgres -- pg_restore -U basil -d basil --clean --if-exists < "$DUMP_FILE"

  echo "[reset] Clearing Replays, Screenshots, uploaded Avatars, Logs and nginx load.log..."
  rm -rf "$DATA_DIR/Replays"/* "$DATA_DIR/Screenshots"/* "$DATA_DIR/Avatars"/* /tmp/basil-load/Logs/* 2>/dev/null || true
  : > /tmp/basil-load/nginx-logs/load.log 2>/dev/null || true

  echo "[reset] Starting basil..."
  kubectl -n "$NS" scale deployment/basil --replicas=1
  kubectl -n "$NS" rollout status deployment/basil --timeout=180s
}

sample_infra() {
  local report_dir="$1"
  local stop_file="$2"
  local infra_log="$report_dir/infra-metrics.log"
  local pg_log="$report_dir/pg-activity.log"

  while [ ! -f "$stop_file" ]; do
    {
      echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
      kubectl -n "$NS" top pods 2>/dev/null || true
      sensors 2>/dev/null | grep -E 'Package|Core|temp' || true
      grep -m 4 'cpu MHz' /proc/cpuinfo 2>/dev/null || true
    } >> "$infra_log"

    {
      echo "=== $(date -u +%Y-%m-%dT%H:%M:%SZ) ==="
      kubectl -n "$NS" exec statefulset/postgres -- \
        psql -U basil -d basil -c "SELECT state, count(*) FROM pg_stat_activity GROUP BY state;" 2>/dev/null || true
    } >> "$pg_log"

    sleep 15
  done
}

cmd_run() {
  local test_name="$1"
  shift
  local run_id="${test_name}-$(date -u +%Y%m%d-%H%M%S)"
  local report_dir="$REPORTS_ROOT/$run_id"
  mkdir -p "$report_dir/manifests" "$report_dir/control"

  publish_binary
  cmd_reset

  cp "$K3S_DIR"/*.yaml "$SCRIPT_DIR/nginx-load.conf" "$report_dir/manifests/"
  kubectl -n "$NS" describe pod -l app=basil > "$report_dir/manifests/basil-pod-describe.txt" 2>&1 || true

  local job_name="load-${run_id}"
  local control_job_name="ctrl-${run_id}"
  local control_duration="${CONTROL_DURATION:-30m}"
  [ "$test_name" = "soak" ] && control_duration="6h30m"
  [ "$test_name" = "smoke" ] && control_duration="4m"

  local stop_file="$report_dir/.stop-sampler"
  sample_infra "$report_dir" "$stop_file" &
  local sampler_pid=$!

  echo "[run] Launching control Job ($control_job_name) on espresso..."
  CONTROL_JOB_NAME="$control_job_name" ESPRESSO_HOME="$ESPRESSO_HOME" RUN_ID="$run_id" \
    CONTROL_DURATION="$control_duration" \
    envsubst < "$K3S_DIR/job-control.yaml" | kubectl apply -f -

  echo "[run] Launching loadtests Job ($job_name) on cappuccino..."
  JOB_NAME="$job_name" CAPPUCCINO_HOME="$CAPPUCCINO_HOME" RUN_ID="$run_id" \
    TEST_ARGS="$test_name $*" \
    envsubst < "$K3S_DIR/job-loadtests.yaml" | kubectl apply -f -

  echo "[run] Waiting for $job_name to complete..."
  local job_status=0
  kubectl -n "$NS" wait --for=condition=complete --timeout=7h "job/$job_name" || job_status=$?

  touch "$stop_file"
  wait "$sampler_pid" 2>/dev/null || true

  kubectl -n "$NS" logs "job/$job_name" > "$report_dir/job-loadtests.log" 2>&1 || true
  kubectl -n "$NS" delete job "$control_job_name" --ignore-not-found >/dev/null 2>&1 || true

  echo "[collect] Gathering reports from cappuccino, espresso and mocha nginx..."
  scp -q -r "$CAPPUCCINO_SSH:~/loadtests/reports/$run_id"/* "$report_dir/" || true
  scp -q -r "$ESPRESSO_SSH:~/loadtests/reports/$run_id"/* "$report_dir/control/" || true
  [ -f /tmp/basil-load/nginx-logs/load.log ] && cp /tmp/basil-load/nginx-logs/load.log "$report_dir/nginx-load.log"
  rm -f "$stop_file"

  echo "[done] Report collected in $report_dir (exit $job_status)."
  return "$job_status"
}

cmd_scalability() {
  for cores in 1 2 4; do
    echo "[scalability] Pinning Basil to $cores CPU(s)..."
    kubectl -n "$NS" patch deployment basil --type='json' -p="[
      {\"op\":\"replace\",\"path\":\"/spec/template/spec/containers/0/resources/requests/cpu\",\"value\":\"$cores\"},
      {\"op\":\"replace\",\"path\":\"/spec/template/spec/containers/0/resources/limits/cpu\",\"value\":\"$cores\"}
    ]"
    kubectl -n "$NS" rollout status deployment/basil --timeout=180s
    cmd_run scalability --cores "$cores" "$@" || true
  done
}

case "${1:-}" in
  deploy) shift; cmd_deploy "$@" ;;
  seed) shift; cmd_seed "$@" ;;
  reset) shift; cmd_reset "$@" ;;
  run) shift; cmd_run "$@" ;;
  scalability) shift; cmd_scalability "$@" ;;
  *)
    echo "Usage: $0 {deploy|seed|reset|run <test> [args...]|scalability [args...]}" >&2
    exit 2
    ;;
esac
