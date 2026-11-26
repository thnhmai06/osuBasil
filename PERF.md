# Performance

How Basil is measured, and the ledger of every tuning attempt. Nothing is tuned by guess: each change is measured the same
way before and after, kept only when it beats the run-to-run variance, and reverted otherwise. Reverted attempts stay in
the ledger so nobody tries them again without a new reason.

## The harness

`Basil.Tests.LoadTests` (backed by `Basil.Tests.Shared.Clients`, `Basil.Tests.Shared.Workload` and
`Basil.Tests.Shared.Environment`) drives a running server through nginx with NBomber the way real clients do:

* **Tournament rooms (`match_room`)**: 2–8 players (`1v1`, `2v2`, `4v4`), one referee (half over IRC `!mp`, half over REST),
  0–2 osu!tourney clusters (manager plus one child client per slot), in-game spectators, and SSE overlay viewers on
  `/v1/matches/{id}` and `/v1/rooms/{id}/live`;
* **Outside rooms**: solo players (`free_player` — `getscores`, play, encrypted score submission with replay, `getreplay`,
  chat), casual rooms (`casual_room`), standalone spectators (`spectator`), tourney observers (`tourney_cluster`), overlay
  viewers (`overlay_viewer`), IRC users (`irc_chatter`), background activities (`background`), login/logout churn (`churn`),
  and deliberate rule refusals (`edge_cases`).

It classifies every request into `ok`, `refused`, `throttled` (429), `shed` (503) or `error` (only `error` counts against
the SLO), records per-channel latency (`poll`, `submit`, `getscores`, `login`, `rest`, `irc`, `sse`, `download`, `upload`),
streams Basil's `/v1/diagnostics` SSE feed into NBomber custom gauges and `diagnostics.jsonl`, and writes HTML, CSV, Markdown,
text and `run-meta.json` reports under `reports/<test>-<timestamp>/`.

### Seeding accounts and world data

Seed once against the running stack through the public REST API (raises `rest.mutations` temporarily, creates player,
referee, tourney and developer accounts, restores the default rate limit, and writes `accounts.csv` and `world-data.json`):

```bash
BASIL_OWNER_PASSWORD=<password> dotnet run --project Basil.Tests.LoadTests -c Release -- \
  seed --connect 192.168.27.124 --port 8443 --domain basil.local \
  --owner-user admin \
  --accounts accounts.csv --world world-data.json
```

The owner password is read from the `BASIL_OWNER_PASSWORD` environment variable only (a flag would show in `ps` and the shell
history). When an account already exists from an earlier seed, the seeder signs in once with one account of each role and
stops with a clear message if the password in the new `accounts.csv` is wrong (seed again with the same `--seed`, or reset
the database).

`accounts.csv`, `world-data.json`, database dumps and `reports/` hold credentials or generated outputs: keep them out of git.

### nginx limits and server-side SLO log

nginx limits requests and connections per address (`docker/nginx/nginx.conf`), and a load generator comes from one address,
so the default limits would answer it with 429. `docker/loadtests/nginx-load.conf` raises the per-address limits, listens on
8443 (HTTPS) and 6697 (IRC TLS), and writes a tab-separated `load` access log with `$upstream_response_time`, `$request_time`,
`$status`, `$uri` and whether `osu-token` was sent (poll versus login) for server-side SLO reporting.

### Running it

Run a single test locally:

```bash
dotnet run --project Basil.Tests.LoadTests -c Release -- \
  smoke --connect 192.168.27.124 --port 8443 --irc-port 6697 --domain basil.local \
  --accounts accounts.csv --world world-data.json --seed 20261125
```

Available test commands: `seed`, `smoke`, `pilot`, `load`, `stress`, `spike`, `soak`, `capacity`, `scalability`, `control`.
Exit codes: `0` the SLO held, `3` the run was sound and the SLO broke (a failed threshold, server CPU above 80 %, or a failed
server-side check of the smoke run), `1` the run itself broke (bad options, the login lockout guard tripped, the server
restarted, a crash). Capacity, pilot, stress and scalability are meant to break the SLO, so the k3s runner reports `3` as a
successful run for them. `run-meta.json` carries the verdict, the step schedule (`plan.steps`: index, target N, start and end
offset in seconds from the end of the warm-up, for splitting the NBomber timeline and `diagnostics.jsonl` per step), the users
online the server measured beside the target N, and, for smoke, the server-side checks (matches have finished rounds, each
score is stored once, no room or session is left over). `plan <test> [options]` prints the planned duration in seconds.

On the k3s bench (`docker/loadtests/`), one script deploys the stack, resets state between runs, publishes and copies the
binary, runs both the generator Job (`cappuccino`) and the control/ping/iperf3 Job (`espresso`), and gathers every report and
infrastructure log into `reports/<test>-<timestamp>/`:

```bash
./docker/loadtests/run.sh deploy
BASIL_OWNER_PASSWORD=<password> ./docker/loadtests/run.sh seed
./docker/loadtests/run.sh run smoke
./docker/loadtests/run.sh run load --n 200 --seed 20261125
./docker/loadtests/run.sh scalability --step-delta 50 --steps 6
```

Before every run the script restores the baseline database and clears `Replays/`, `Screenshots/`, uploaded `Avatars/`,
`Cache/`, `Logs/` and the nginx load log with a throwaway root pod on the same host paths; it stops if anything is left. The
control probe on `espresso` lasts as long as the test plans to (plus three minutes), and the script waits for the Job to
succeed or fail instead of for success only.

## SLO targets

| Channel | Target |
|---|---|
| osu! poll (`poll`) | p95 <= 200 ms, p99 <= 500 ms |
| score submission (`submit`) | p95 <= 1 s |
| getscores (`getscores`) | p95 <= 500 ms |
| REST read (`rest`) | p95 <= 300 ms |
| login (`login`) | p95 <= 2 s |
| real errors (`error`) | < 0.5 % |
| server | CPU <= 80 %, no OOM, no restart |

`/d/` downloads (`download`) and uploads (`upload`) are reported separately and are not judged against latency targets.

## Ledger

One row per attempt, kept or reverted. Baseline and result are the medians of three runs, as
`osu p95 ms / irc p95 ms / api p95 ms / server CPU % avg / memory MiB max / failed`.

| # | Idea | Baseline | Result | Verdict | Why |
|---|---|---|---|---|---|
| 0 | Baseline (image `e91917168`, 4 cores, 2026-11-25) | 1.98 / 1.63 / 2.87 / 12.2 / 195 / 0 | - | - | Spread over the three runs: osu p95 1.98–1.99, irc 1.59–1.69, api 2.83–2.90, CPU 11.8–13.1 %. One run shed 8 sign-ins while the ramp held the CPU near 0.9: sign-in cost (bcrypt, presence to everyone) is the first candidate. |
| 1 | W14: every layer does only its own job (image `26b078a10` against `4dc697312`, 4 cores, 2026-11-26) | 1.99 / 1.62 / 2.85 / 12.4 / 152 / 0 | 1.98 / 1.66 / 2.68 / 12.4 / 157 / 0 | Kept | A placement change, not a tuning: every figure is within the run-to-run spread (osu p95 1.94–2.03, irc 1.62–1.74, api 2.66–3.01, CPU 11.7–12.9 %). The harness has no multiplayer or spectating, so the score and frame relay that W14 moved are not measured here. |

## Known limits

**Sign-in rate.** A sign-in checks the password with bcrypt (about 145 ms of CPU) and sends the new player everyone's
presence. On 4 cores, 270 sign-ins in 20 s (13.5 a second) held the CPU near 0.9 and the server shed some of them with
503; the osu! client retries. bcrypt is slow on purpose and stays as it is (owner, 2026-11-25). The ramp duration is derived
from `N * 0.145 / (cores * 0.5)` for that reason.
