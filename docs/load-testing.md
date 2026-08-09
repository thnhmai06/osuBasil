# Load testing

## Why

Basil's real workload is a room full of tournament players on a LAN: a handful of simultaneous matches, dozens of chat
messages a minute, a few API reads. The question that matters is "how many concurrent players can this hardware serve,
and at what latency?" — not "how fast is the login endpoint."

The harness answers that question with real traffic. It speaks the actual bancho binary protocol and the actual `api.`
HTTP surface to a real running server, from a configurable number of virtual users, and records throughput, latency
percentiles, and the server's own resource usage (CPU, working set, GC, threads, handles, TCP connections) alongside it.
It also answers two questions ordinary request-replay load tests can't: "does the server stay usable as load is turned
up past its comfort zone" (stress), and "does it leak memory or sockets over hours" (soak).

## Overview

`tests/Basil.LoadTests` is an executable project (not a test project, despite living under `tests/`) that drives an
NBomber-based load run. It is not part of the solution's test suite — `dotnet test` never runs it.

A run is configured entirely by a profile: a JSON file naming a server host, a client setup, an account pool, and a set
of enabled scenarios. `Program.cs` loads the profile, owns the server's whole lifecycle (publish, start, seed, snapshot,
stop), and runs each enabled scenario in sequence through NBomber. Everything lands in a timestamped report folder.

The harness references `Basil.Protocol` for wire-format serialization only. It deliberately does **not** reference
`Basil.Web`: the server under test is built and launched as a separate process, so the harness cannot accidentally share
state with it.

## Running a load test

```bash
dotnet run --project tests/Basil.LoadTests -- --profile quick
```

The profile name selects `tests/Basil.LoadTests/Profiles/<name>.json`. `quick` is the default and the right place to
start: it uses a `Dotnet` host with auto-publish, seeds 100 accounts, and runs the login and API scenarios at modest
concurrency.

To run a single scenario:

```bash
dotnet run --project tests/Basil.LoadTests -- --profile full --scenario login
```

A profile targets a TLS-secured host on a specific domain, so it needs a `basil-cert.pfx` covering that domain and its
subdomains at the repo root — the same certificate a real deployment uses. See [
`docs/run-deployment.md`](run-deployment.md#2-development-working-on-basil-itself) for how to generate one. DNS is not
required for a local run: `BasilHttpClientFactory` dials `HostAddress` directly while sending the configured domain as
the `Host` header and TLS SNI, so only the profile's `HostAddress` needs to point at a reachable machine.

## What a run does

```text
load profile → publish/start server (owned hosts) or attach (existing)
→ wait healthy → snapshot-or-seed the account pool
→ warm the bcrypt cache (optional) → settle
→ run each enabled scenario through NBomber, one concurrency level per scenario
→ soak leak analysis (if enabled) → stop host → write reports
```

Ordered phases:

1. **Startup benchmark (optional).** If `Scenarios:startup.Enabled`, the server is started, settled, and stopped
   repeatedly before the main run. Measures startup time and idle resource usage. Writes `startup-benchmark.md` and
   `startup-idle-samples.csv`.
2. **Server start.** The `Dotnet` host publishes `Basil.Web` into `.loadtest/server` (if `AutoPublish`) and launches it;
   the `Docker` host runs `docker compose`; the `Existing` host just attaches. The run waits until the server answers
   the health probe.
3. **Account pool.** `Account.Count` accounts are seeded through the admin API (`BasilApiClient`) into the freshly
   started server. On a `Dotnet`/`Docker` host the seeding cost is paid once: after the first seed, the SQLite database
   is snapshotted to `.loadtest/snapshots/basil-<count>-<hash>.db` and restored instead of re-seeded on every run. The
   snapshot filename embeds the account count and password hash, so a run with different accounts gets its own snapshot.
   The `Existing` host cannot snapshot or restore; it ensures the accounts exist and tolerates ones already present from
   a prior run.
4. **Bcrypt warm-up (login scenario only, optional).** If `Scenarios:login.WarmBcryptCache`, every account is logged in
   once before measurement so the measured phase hits the bcrypt-verify cache rather than paying full bcrypt cost on
   every login. This deliberately leaves every account with a live session.
5. **Settle.** The run then waits `PostWarmupSettleSeconds` (11 by default) so those warm-up sessions age past the
   server's relogin guard. See [Why warm-up and settle exist](#why-warm-up-and-settle-exist).
6. **Scenarios.** Each enabled scenario runs in catalog order (`login`, `idle`, `chat`, `multiplayer`, `api`, `sse`,
   `stress`, `soak`). Most scenarios expand `ConcurrentUsers` into one NBomber scenario per level, named `{id}_{level}`
   (e.g. `login_250`). A `--scenario` filter skips everything else.
7. **Soak analysis.** If `Scenarios:soak.Enabled`, `SoakAnalyzer` fits a per-hour slope to each resource series over the
   steady-state window (excluding the soak warm-up) and classifies it `leak` / `watch` / `stable` against the profile's
   thresholds. Writes `soak-analysis.md`.
8. **Reports.** The host is stopped (owned hosts), its results are exported, and `Program.cs` writes `run.json`,
   `resources.csv`, and `summary.md`.

## Profiles

| Profile    | Server host          | Accounts | Scenarios                                    |
|------------|----------------------|----------|----------------------------------------------|
| `quick`    | Dotnet, auto-publish | 100      | startup, login, api                          |
| `full`     | Dotnet, auto-publish | 2000     | startup, login, idle, chat, multiplayer, api |
| `stress`   | Dotnet, auto-publish | 5000     | stress only                                  |
| `soak`     | Dotnet, auto-publish | 1000     | soak only                                    |
| `existing` | Existing (attach)    | 100      | login, api                                   |

`full` is the closest thing to a full validation pass: startup, then login at 100–2000 concurrent, idle at 1000, chat at
200–1000, multiplayer at 4–64 rooms, and the API mix at 50–500. `stress` ramps login-style traffic to 5000 and never
stops on failure by design. `soak` holds a 750-user weighted mix (chat, multiplayer, api, idle) for 12 hours and watches
resource slopes.

## Scenarios

| Scenario      | What it does                                                                                                                                                                                                                                                                   | Scale axis   | Key settings                                                                                                                                                     |
|---------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `login`       | Repeated login → logout round trips. Throughput is *sustained* round trips per second at N concurrent clients, not a thundering herd.                                                                                                                                   | users        | `WarmBcryptCache`, `PostWarmupSettleSeconds`                                                                                            |
| `idle`        | Logged-in clients sitting in the lobby, polling to stay alive. Measures the baseline cost of a live session.                                                                                                                                                                   | users        | —                                                                                                                                                                |
| `chat`        | Senders emitting messages on a channel; the rest of the load is receiving.                                                                                                                                                                                                     | users        | `Channel`, `SendersPercent`, `MessagesPerMinutePerSender`, `MessageBytes`                                                                                        |
| `multiplayer` | Full create → assign map → play → score-update → complete round loops, repeated per room.                                                                                                                                                                                      | rooms (≤ 64) | `Rooms`, `PlayersPerRoom` (≤ 16), `RoundsPerRoom`, `ScoreUpdatesPerSecond`, `BeatmapsetFixture`                                                                  |
| `api`         | Weighted mix over the chosen `api.` endpoints (`health`, `user`, `match_list`, `match_report`, `beatmapset`).                                                                                                                                                                  | users        | `Endpoints`, `MixedWeights`                                                                                                                                      |
| `sse`         | Live SSE channels from the `api.` host.                                                                                                                                                                                                                                        | users        | —                                                                                                                                                                |
| `stress`      | Chained ramp-and-hold: ramp to each level, hold, ramp to the next. Never aborts on a single failure; stops itself only if success rate stays under `UnusableSuccessRatePercent` for `UnusableForSeconds` or CPU stays at/above `SaturationCpuPercent` for `SaturationSeconds`. | users        | `ConcurrentUsers`, `RampSeconds`, `HoldSeconds`, `MaxFailCount`, `UnusableSuccessRatePercent`, `UnusableForSeconds`, `SaturationCpuPercent`, `SaturationSeconds` |
| `soak`        | A long-running weighted mix of chat/multiplayer/api/idle.                                                                                                                                                                                                                      | users        | `ConcurrentUsers`, `DurationSeconds`, `WarmUpSeconds`, `ReportingIntervalSeconds`, `Weights`, `LeakSlopeThresholds`                                              |

Plus the `startup` benchmark, which is not an NBomber scenario and owns the server lifecycle itself.

## Server host kinds

`ServerHost:Kind` selects how the harness reaches the server:

- **`Dotnet`** (default): publishes and launches Basil.Web as a local child process. `Mode: Published` (default) uses a
  published binary under `PublishDirectory` and is the only mode whose process metrics are trustworthy — a published exe
  has no wrapper process. `Mode: Run` uses `dotnet run` for faster iteration, at the cost of process metrics measuring
  the wrong process. `AutoPublish: true` publishes once; a stale `.loadtest/server` from a previous run is reused, so
  delete that folder to force a fresh publish. This host can snapshot the database.
- **`Docker`**: runs the repo's `docker-compose.yml` (or a profile-supplied compose file) and observes the container.
  Can snapshot the database via the `./docker-data/Data` bind mount, but GC/allocation counters are unavailable inside a
  container.
- **`Existing`**: attaches to an already-running server. No snapshot/restore, and process metrics only when
  `Existing.ProcessId` is set. Used for pointing the harness at a deployed instance.

## Reports

Every run writes to `.loadtest/reports/<profile>-<timestamp>/`:

- `run.json` — the full manifest: profile, git commit, timestamps, OS/runtime, host capabilities, startup time, notes.
- `summary.md` — environment, host capabilities, and min/mean/max resource aggregates.
- `resources.csv` — the server's sampled resource timeline (CPU, working set, GC, threads, handles, TCP connections).
- `startup-benchmark.md` + `startup-idle-samples.csv` — only when the startup benchmark ran.
- `soak-analysis.md` — only when soak ran: per-series per-hour slopes with `leak` / `watch` / `stable` verdicts.
- `{scenario}/{n}/` — NBomber's own per-scenario reports (HTML/Csv/Md/Txt, per the profile's `Report.Formats`),
  containing what the harness does not recompute: requests/sec, latency percentiles, and failure counts.

## Design rationale

### Why warm-up and settle exist

The bcrypt warm-up logs every account in once and deliberately does **not** log them out: its purpose is only the
bcrypt-verify cache, and logging out would double the warm-up pass for nothing gained. Those sessions therefore stay
live. The server rejects a relogin within 10 seconds of a live session's last poll, so the run settles for
`PostWarmupSettleSeconds` (default 11) before measurement starts — this lets each account's stale warm-up session age
past that guard so its first measured login evicts it cleanly instead of failing with `user-already-logged-in`.

### Why logout must be cancellation-safe

NBomber cancels in-flight scenario iterations when warm-up ends. A login that completed but was cancelled mid-flight
would otherwise leak a live session, and the account's next login would fail. Two things make that impossible:
- `BanchoClient.LoginAsync` captures the response's `cho-token` as soon as the headers arrive, before it reads the reply
  body, and never lets the scenario's cancellation token interrupt a login request — so a cancelled iteration still
  holds a token it can use to close the session.
- Every scenario closes the sessions it opened: the login, multiplayer, and stress scenarios dispose one client per
  iteration via `await using`, and the idle, chat, and soak scenarios collect each client and dispose the whole set in
  `.WithClean`.
This is a harness-side invariant, not a server workaround.

### Why multiplayer scales by rooms, not users

The server allocates match ids from a fixed 64-slot pool, so `MultiplayerSettings.Rooms` can never exceed 64, and a room
holds at most 16 players. Scaling by rooms is the axis that matters for a tournament server; users are the axis for the
other scenarios.

### Why the account pool is snapshotted

Seeding pays bcrypt once per account. On a locally owned host the seeded database is snapshotted after the first run and
restored on later runs, so repeat benchmarks (or benchmarking different configurations) don't pay the seeding cost or
re-derive account state each time.

## Invariants

- `MultiplayerSettings.Rooms` ≤ 64, `PlayersPerRoom` ≤ 16 — the server's match-id pool and per-match slot count.
- `ClientSettings.PollIntervalSeconds` must stay well under the server's 300-second ghost-session reaper interval, or
  idle clients get reaped mid-run.
- The server rejects a relogin within 10 seconds of a still-live session's last poll (there is no logout grace), so
  every scenario closes every session it opens (see [Why logout must be cancellation-safe](#why-logout-must-be-cancellation-safe))
- `Accounts.Count` must be at least the largest concurrency any enabled scenario asks for.
- `Dotnet` host: `Mode: Run` reports process metrics for the wrong process; use `Published` when those metrics matter.

## Related code

- `tests/Basil.LoadTests/Program.cs` — the run pipeline: phases, report writing, server lifecycle.
- `tests/Basil.LoadTests/Profiles/` — the five shipped profiles.
- `tests/Basil.LoadTests/Configuration/` — every settings type bound from a profile.
- `tests/Basil.LoadTests/Scenarios/` — one file per scenario, plus `ScenarioCatalog` (registration order) and
  `StartupBenchmark`.
- `tests/Basil.LoadTests/Client/` — the bancho/HTTP protocol clients the scenarios drive.
- `tests/Basil.LoadTests/Hosting/` — `IServerHost` and the `Dotnet`/`Docker`/`Existing` implementations, plus
  `ServerDatabase` snapshot logic.
- `tests/Basil.LoadTests/Infrastructure/` — resource sampling, soak analysis, report writers.

## See also

- [`docs/run-deployment.md`](run-deployment.md): how a real instance is deployed, and how to generate the
  `basil-cert.pfx` a profile needs.
- [`docs/architecture.md`](architecture.md): the host-based routing the profiles assume.
- [`docs/testing.md`](testing.md): the test suite this project deliberately sits outside of.
