#!/usr/bin/env bash
set -euo pipefail

# Orchestrates the k3s load test stack on mocha, cappuccino and espresso.
# Usage:
#   ./docker/loadtests/run.sh deploy
#   BASIL_OWNER_PASSWORD=<password> ./docker/loadtests/run.sh seed [extra seed args...]
#   ./docker/loadtests/run.sh reset
#   ./docker/loadtests/run.sh run <smoke|pilot|load|stress|spike|soak|capacity|scalability> [extra args...]
#   ./docker/loadtests/run.sh scalability [extra args...]
#
# Exit code of 'run': 0 the SLO held (or the test is meant to break it: capacity, pilot, stress, scalability),
# 3 the SLO broke, 1 the run itself broke (lockout guard, server restart, crash, timeout, bad options).

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
PUBLISH_DIR="$REPO_ROOT/publish/loadtests-linux-x64"

publish_binary() {
  local out_dir="$PUBLISH_DIR"
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
  # The owner password travels in the environment, never on a command line (it would show in ps and shell history).
  if [ -z "${BASIL_OWNER_PASSWORD:-}" ]; then
    echo "Set BASIL_OWNER_PASSWORD to the owner password before seeding." >&2
    exit 2
  fi
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
  if [ -n "$(kubectl -n "$NS" get pods -l app=basil -o name)" ]; then
    echo "[reset] The basil pod is still running; refusing to reset under a live server." >&2
    exit 1
  fi

  echo "[reset] Restoring PostgreSQL baseline dump..."
  kubectl -n "$NS" exec -i statefulset/postgres -- pg_restore -U basil -d basil --clean --if-exists < "$DUMP_FILE"

  # The server writes these folders as its own user (possibly root), so a throwaway root pod with the same host paths
  # clears them, and the run stops if anything is left: every run must start from the same cold state.
  echo "[reset] Clearing Replays, Screenshots, uploaded Avatars, Cache, Logs and nginx load.log..."
  kubectl -n "$NS" delete pod clear-state --ignore-not-found --wait=true >/dev/null
  DATA_DIR="$DATA_DIR" envsubst '${DATA_DIR}' < "$K3S_DIR/clear-state.yaml" | kubectl apply -f -
  local phase=""
  for _ in $(seq 1 90); do
    phase="$(kubectl -n "$NS" get pod clear-state -o jsonpath='{.status.phase}' 2>/dev/null || true)"
    [ "$phase" = "Succeeded" ] || [ "$phase" = "Failed" ] && break
    sleep 2
  done
  if [ "$phase" != "Succeeded" ]; then
    echo "[reset] Clearing the state failed (pod phase '${phase:-unknown}'):" >&2
    kubectl -n "$NS" logs pod/clear-state >&2 || true
    exit 1
  fi
  kubectl -n "$NS" delete pod clear-state --ignore-not-found >/dev/null

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

# Waits until a Job has succeeded or failed (a failed run is still a finished run); returns 124 on timeout.
wait_for_job() {
  local job="$1" deadline=$((SECONDS + $2)) done_count failed_count
  while [ "$SECONDS" -lt "$deadline" ]; do
    done_count="$(kubectl -n "$NS" get "job/$job" -o jsonpath='{.status.succeeded}' 2>/dev/null || true)"
    failed_count="$(kubectl -n "$NS" get "job/$job" -o jsonpath='{.status.failed}' 2>/dev/null || true)"
    if [ "${done_count:-0}" -ge 1 ] || [ "${failed_count:-0}" -ge 1 ]; then return 0; fi
    sleep 15
  done
  return 124
}

# Prints the exit code of the Job's container, or nothing when it never ran.
job_exit_code() {
  kubectl -n "$NS" get pods -l "job-name=$1" \
    -o jsonpath='{.items[0].status.containerStatuses[0].state.terminated.exitCode}' 2>/dev/null || true
}

cmd_run() {
  local test_name="$1"
  shift
  local run_id="${test_name}-$(date -u +%Y%m%d-%H%M%S)"
  local report_dir="$REPORTS_ROOT/$run_id"
  mkdir -p "$report_dir/manifests" "$report_dir/control"

  publish_binary

  # The control probe must cover the whole run: size it from the test's own plan (plus a margin for start and stop).
  local planned
  planned="$("$PUBLISH_DIR/Basil.Tests.LoadTests" plan "$test_name" "$@")"
  if ! [[ "$planned" =~ ^[0-9]+$ ]]; then
    echo "Could not read the planned duration of '$test_name $*': $planned" >&2
    return 1
  fi

  cmd_reset

  cp "$K3S_DIR"/*.yaml "$SCRIPT_DIR/nginx-load.conf" "$report_dir/manifests/"
  kubectl -n "$NS" describe pod -l app=basil > "$report_dir/manifests/basil-pod-describe.txt" 2>&1 || true

  local job_name="load-${run_id}"
  local control_job_name="ctrl-${run_id}"
  local control_duration="${CONTROL_DURATION:-$((planned + 180))s}"

  local stop_file="$report_dir/.stop-sampler"
  sample_infra "$report_dir" "$stop_file" &
  local sampler_pid=$!

  echo "[run] Launching control Job ($control_job_name) on espresso..."
  CONTROL_JOB_NAME="$control_job_name" ESPRESSO_HOME="$ESPRESSO_HOME" RUN_ID="$run_id" \
    CONTROL_DURATION="$control_duration" \
    envsubst '${CONTROL_JOB_NAME} ${ESPRESSO_HOME} ${RUN_ID} ${CONTROL_DURATION}' < "$K3S_DIR/job-control.yaml" | kubectl apply -f -

  echo "[run] Launching loadtests Job ($job_name) on cappuccino..."
  JOB_NAME="$job_name" CAPPUCCINO_HOME="$CAPPUCCINO_HOME" RUN_ID="$run_id" \
    TEST_ARGS="$test_name $*" \
    envsubst '${JOB_NAME} ${CAPPUCCINO_HOME} ${RUN_ID} ${TEST_ARGS}' < "$K3S_DIR/job-loadtests.yaml" | kubectl apply -f -

  echo "[run] Waiting for $job_name to finish (planned ${planned}s)..."
  local wait_status=0
  wait_for_job "$job_name" $((planned + 1800)) || wait_status=$?
  local exit_code
  exit_code="$(job_exit_code "$job_name")"

  touch "$stop_file"
  wait "$sampler_pid" 2>/dev/null || true

  kubectl -n "$NS" logs "job/$job_name" > "$report_dir/job-loadtests.log" 2>&1 || true
  kubectl -n "$NS" delete job "$control_job_name" --ignore-not-found >/dev/null 2>&1 || true

  echo "[collect] Gathering reports from cappuccino, espresso and mocha nginx..."
  scp -q -r "$CAPPUCCINO_SSH:~/loadtests/reports/$run_id"/* "$report_dir/" || true
  scp -q -r "$ESPRESSO_SSH:~/loadtests/reports/$run_id"/* "$report_dir/control/" || true
  [ -f /tmp/basil-load/nginx-logs/load.log ] && cp /tmp/basil-load/nginx-logs/load.log "$report_dir/nginx-load.log"
  rm -f "$stop_file"

  # 0 the SLO held, 3 it broke, anything else (or no exit code, or a timeout) means the run itself broke.
  local result=1
  if [ "$wait_status" -eq 0 ]; then
    case "${exit_code:-}" in
      0) result=0 ;;
      3)
        case "$test_name" in
          capacity | pilot | stress | scalability) result=0 ;; # these are meant to break the SLO
          *) result=3 ;;
        esac
        ;;
    esac
  fi

  echo "[done] Report collected in $report_dir (container exit ${exit_code:-none}, wait $wait_status, run result $result)."
  return "$result"
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
