# Load testing

## Overview

`tests/Basil.LoadTests` is Basil's end-to-end load-testing harness.

It drives a real Basil server using the same protocols as real clients:

* Bancho binary protocol;
* HTTP APIs on the `api.` host;
* SSE connections.

The harness measures both application behavior and server resource usage under load.

It is designed to answer:

* how many concurrent players a given machine can support;
* how latency changes as concurrency increases;
* where the server becomes saturated;
* whether resources such as memory, sockets, or handles grow during long runs.

The harness supports three types of load evaluation:

* **load**: behavior at expected operating levels;
* **stress**: behavior beyond the expected operating range;
* **soak**: long-running behavior and resource stability.

`tests/Basil.LoadTests` is an executable project, not a test project. It is intentionally excluded from the normal `dotnet test` suite.

## Architecture

The harness runs the server as a separate process or container.

```text
Load-test profile
       │
       ▼
Program.cs
       │
       ├── server lifecycle
       ├── account preparation
       ├── scenario selection
       ├── resource sampling
       └── report generation
              │
              ▼
        real Basil server
         /          \
        /            \
Bancho protocol    HTTP/SSE
```

The harness references `Basil.Protocol` for wire-format serialization but does not reference `Basil.Web`.

This separation is deliberate. The server under test must have its own process and state so the harness cannot accidentally bypass the network boundary or share application state with it.

## Running a load test

Start with the default `quick` profile:

```bash
dotnet run --project tests/Basil.LoadTests -- --profile quick
```

The profile name maps to:

```text
tests/Basil.LoadTests/Profiles/<name>.json
```

Run a specific scenario:

```bash
dotnet run --project tests/Basil.LoadTests -- --profile full --scenario login
```

The `--scenario` option restricts execution to the selected scenario.

The `quick` profile is the recommended first run because it uses a local `Dotnet` host, automatically publishes Basil, seeds a small account pool, and runs a limited login/API workload.

## TLS and server addressing

Load-test profiles target a TLS-secured Basil host.

The repository root must contain a `basil-cert.pfx` covering the configured domain and its required subdomains.

See [`https.md`](../for-technicians/https.md) for certificate generation.

A local DNS server is not required.

`BasilHttpClientFactory` connects directly to the configured `HostAddress` while using the configured domain for:

* the HTTP `Host` header;
* TLS SNI.

Therefore:

```text
HostAddress → network destination
Domain      → Basil virtual host + TLS identity
```

Only `HostAddress` needs to resolve to a reachable machine.

## Run lifecycle

A normal run follows this pipeline:

```text
load profile
    │
    ├── optional startup benchmark
    │
    ▼
start or attach to server
    │
    ▼
wait for health probe
    │
    ▼
prepare account pool
    │
    ├── restore snapshot
    │      or
    └── seed accounts
    │
    ▼
optional bcrypt warm-up
    │
    ▼
settle
    │
    ▼
run enabled scenarios
    │
    ▼
optional soak analysis
    │
    ▼
stop owned server
    │
    ▼
write reports
```

### Startup benchmark

When `Scenarios:startup:Enabled` is enabled, the harness repeatedly starts and stops the server before the main load run.

It measures:

* startup duration;
* idle resource usage.

The results are written to:

```text
startup-benchmark.md
startup-idle-samples.csv
```

The startup benchmark is independent of NBomber scenarios and owns the server lifecycle itself.

### Server startup

The selected `ServerHost` controls how Basil is started:

* `Dotnet` publishes and launches `Basil.Web`;
* `Docker` starts the configured Compose deployment;
* `Existing` attaches to an already-running instance.

The run does not begin scenarios until the health probe succeeds.

### Account preparation

The configured `Account.Count` accounts are prepared through the admin API using `BasilApiClient`.

For `Dotnet` and `Docker` hosts, the first seed creates a database snapshot:

```text
.loadtest/snapshots/basil-<count>-<hash>.db
```

Later runs restore that snapshot instead of repeating account creation and bcrypt hashing.

The snapshot identity includes:

* account count;
* password hash.

This prevents incompatible account pools from sharing a snapshot.

`Existing` hosts cannot snapshot or restore the database. They ensure the required accounts exist and tolerate accounts already present from previous runs.

## Bcrypt warm-up

The login scenario can optionally warm the server's bcrypt verification cache.

When:

```text
Scenarios:login:WarmBcryptCache
```

is enabled, every account is logged in once before measurement.

The warm-up intentionally leaves those sessions alive.

The harness then waits:

```text
Scenarios:PostWarmupSettleSeconds
```

before starting measurement.

The default is 11 seconds.

This matters because Basil rejects a relogin while the previous session's last poll is still within the server's 10-second relogin guard.

The settle period therefore allows warm-up sessions to age beyond that guard before measured logins begin.

## Scenarios

| Scenario      | Purpose                                       | Scale axis       |
| ------------- | --------------------------------------------- | ---------------- |
| `login`       | Repeated login/logout round trips             | concurrent users |
| `idle`        | Live clients polling while otherwise inactive | concurrent users |
| `chat`        | Channel message sending and receiving         | concurrent users |
| `multiplayer` | Full match and round lifecycle                | rooms            |
| `api`         | Weighted HTTP API workload                    | concurrent users |
| `sse`         | Long-lived SSE subscriptions                  | concurrent users |
| `stress`      | Ramp-and-hold load beyond normal capacity     | concurrent users |
| `soak`        | Long-running mixed workload                   | concurrent users |

Scenarios are registered in `ScenarioCatalog` order.

Most scenarios expand `ConcurrentUsers` into separate NBomber scenarios:

```text
login_100
login_250
login_500
...
```

A `--scenario` filter prevents other scenarios from running.

### Login

Repeatedly performs:

```text
login → logout
```

The metric represents sustained round trips per second at the selected concurrency.

It is not a simultaneous-login or thundering-herd benchmark.

Important settings:

* `WarmBcryptCache`;
* `PostWarmupSettleSeconds`.

### Idle

Maintains logged-in clients that remain in the lobby and poll to stay alive.

This measures the baseline resource cost of maintaining live sessions without significant application activity.

### Chat

Creates message senders and receivers on a configured channel.

Important settings:

* `Channel`;
* `SendersPercent`;
* `MessagesPerMinutePerSender`;
* `MessageBytes`.

### Multiplayer

Exercises the complete match lifecycle:

```text
create
  → assign map
  → play
  → score updates
  → complete round
```

The workload is repeated independently for each room.

Important settings:

* `Rooms`;
* `PlayersPerRoom`;
* `RoundsPerRoom`;
* `ScoreUpdatesPerSecond`;
* `BeatmapsetFixture`.

The server currently supports at most 64 match ids and 16 players per match, so:

```text
Rooms ≤ 64
PlayersPerRoom ≤ 16
```

Multiplayer load is therefore scaled by rooms rather than users.

### API

Runs a weighted mixture of API operations.

The standard endpoint categories are:

* `health`;
* `user`;
* `match_list`;
* `match_report`;
* `beatmapset`.

Important settings:

* `Endpoints`;
* `MixedWeights`.

### SSE

Creates live SSE subscriptions through the `api.` host.

This measures the resource cost of maintaining long-lived HTTP connections.

### Stress

Stress testing uses chained ramp-and-hold stages:

```text
ramp → hold
       │
       ▼
next concurrency level
       │
       ▼
ramp → hold
       │
      ...
```

The scenario intentionally does not stop because of individual operation failures.

It stops only when the configured unusable or saturation conditions persist for their required duration.

Important settings:

* `ConcurrentUsers`;
* `RampSeconds`;
* `HoldSeconds`;
* `MaxFailCount`;
* `UnusableSuccessRatePercent`;
* `UnusableForSeconds`;
* `SaturationCpuPercent`;
* `SaturationSeconds`.

### Soak

Soak testing runs a long-lived weighted mixture of:

* chat;
* multiplayer;
* API;
* idle clients.

Important settings:

* `ConcurrentUsers`;
* `DurationSeconds`;
* `WarmUpSeconds`;
* `ReportingIntervalSeconds`;
* `Weights`;
* `LeakSlopeThresholds`.

`SoakAnalyzer` evaluates resource series over the steady-state portion of the run and classifies each series as:

* `stable`;
* `watch`;
* `leak`.

The warm-up portion is excluded from slope analysis.

## Shipped profiles

| Profile    | Host                 | Accounts | Scenarios                                    |
| ---------- | -------------------- | -------: | -------------------------------------------- |
| `quick`    | Dotnet, auto-publish |      100 | startup, login, api                          |
| `full`     | Dotnet, auto-publish |     2000 | startup, login, idle, chat, multiplayer, api |
| `stress`   | Dotnet, auto-publish |     5000 | stress                                       |
| `soak`     | Dotnet, auto-publish |     1000 | soak                                         |
| `existing` | Existing             |      100 | login, api                                   |

`full` is the primary validation profile.

Its workload currently covers:

* login: 100-2000 concurrent users;
* idle: 1000 users;
* chat: 200-1000 users;
* multiplayer: 4-64 rooms;
* API: 50-500 concurrent users.

`stress` intentionally pushes login-style traffic to 5000 users.

`soak` holds a 750-user mixed workload for 12 hours.

## Server hosts

`ServerHost:Kind` determines how the harness obtains the server under test.

### Dotnet

The default host publishes and launches `Basil.Web` as a local child process.

Two execution modes are available:

* `Published`: launches the published executable;
* `Run`: launches through `dotnet run`.

`Published` is the correct mode when process-level resource metrics matter.

With `Run`, the measured process may be the .NET wrapper rather than the actual Basil process.

`AutoPublish: true` publishes once and reuses the existing:

```text
.loadtest/server
```

Delete this directory to force a fresh publish.

The Dotnet host can snapshot and restore the SQLite database.

### Docker

The Docker host runs the repository's `docker-compose.yml`, or a profile-supplied Compose file.

The host can snapshot the database through the:

```text
./docker-data/Data
```

bind mount.

Container-level GC and allocation counters are unavailable.

### Existing

The Existing host attaches to a server that is already running.

It cannot snapshot or restore the database.

Process-level metrics are available only when:

```text
Existing.ProcessId
```

is configured.

This mode is intended for testing deployed or externally managed instances.

## Resource monitoring

The harness records server resource usage alongside scenario results.

The resource timeline includes:

* CPU;
* working set;
* GC activity;
* threads;
* handles;
* TCP connections.

These samples are written to:

```text
resources.csv
```

Resource monitoring is separate from NBomber's request metrics.

This allows a load result such as increasing latency to be correlated with server-side resource behavior instead of treating the latency number in isolation.

## Reports

Each run creates:

```text
.loadtest/reports/<profile>-<timestamp>/
```

The directory contains:

| File                       | Purpose                                                                                                 |
| -------------------------- | ------------------------------------------------------------------------------------------------------- |
| `run.json`                 | Complete run manifest, including profile, commit, timestamps, runtime, OS, host capabilities, and notes |
| `summary.md`               | Environment information and resource aggregates                                                         |
| `resources.csv`            | Sampled server resource timeline                                                                        |
| `startup-benchmark.md`     | Startup measurements when enabled                                                                       |
| `startup-idle-samples.csv` | Startup benchmark resource samples                                                                      |
| `soak-analysis.md`         | Resource slope analysis when soak is enabled                                                            |
| `{scenario}/{n}/`          | NBomber's scenario reports                                                                              |

NBomber reports contain metrics that the harness does not recompute, including:

* requests/operations per second;
* latency percentiles;
* failure counts.

## Scenario cleanup

Load-test scenarios must close every session they create.

This is a harness invariant, not a server-side workaround.

Short-lived scenarios such as login, multiplayer, and stress dispose their clients per iteration.

Long-lived scenarios such as idle, chat, and soak retain their clients and dispose the complete collection during `.WithClean`.

This distinction matters because Basil rejects a relogin while an existing session is still considered live.

## Cancellation safety

NBomber can cancel an in-flight iteration when a warm-up or scenario phase ends.

A cancellation must not leave a live Basil session behind.

`BanchoClient.LoginAsync` therefore captures the `cho-token` as soon as the response headers arrive, before reading the response body.

The login request itself is not allowed to be interrupted by the scenario cancellation token.

This guarantees that even a cancelled iteration has the token required to close the session.

The invariant is:

```text
login begins
    │
    ▼
cho-token received
    │
    ├── iteration completes normally → logout
    │
    └── iteration cancelled          → logout using captured token
```

Without this guarantee, warm-up cancellation could leak sessions and cause subsequent `user-already-logged-in` failures.

## Design rationale

### Why use real protocols?

Replay-based HTTP load tests cannot exercise Basil's actual Bancho protocol path.

The harness therefore uses the same network surfaces as real clients.

This makes the measured workload include protocol parsing, session management, packet handling, database access, chat routing, and other server-side work that a synthetic internal benchmark would bypass.

### Why scale multiplayer by rooms?

Basil allocates match ids from a fixed 64-slot pool and each match has at most 16 player slots.

The meaningful tournament capacity variable is therefore the number of simultaneous rooms rather than a single global player count.

### Why snapshot accounts?

Account creation performs bcrypt hashing.

Repeating account seeding for every benchmark run would add setup cost to the benchmark workflow and make repeated experiments unnecessarily expensive.

A seeded SQLite snapshot makes the account pool reproducible without paying that cost every time.

### Why warm the bcrypt cache?

A login benchmark can measure two very different workloads:

```text
cold bcrypt verification
```

or:

```text
cached bcrypt verification
```

The optional warm-up allows the benchmark to intentionally choose between those conditions.

When enabled, the measured phase represents steady-state login behavior rather than repeatedly paying the initial bcrypt-cache cost.

## Invariants

The following constraints are part of the load-test contract:

* `MultiplayerSettings.Rooms` must be ≤ 64.
* `MultiplayerSettings.PlayersPerRoom` must be ≤ 16.
* `ClientSettings.PollIntervalSeconds` must remain well below Basil's 300-second ghost-session reaper interval.
* Every scenario must close every session it opens.
* `Accounts.Count` must be at least the largest concurrency level used by any enabled scenario.
* A measured login cannot immediately reuse an account whose previous session is still inside Basil's 10-second relogin guard.
* `PostWarmupSettleSeconds` must therefore be long enough for warm-up sessions to age past that guard.
* `Dotnet` `Published` mode must be used when process-level resource metrics are part of the result.
* `Dotnet` `Run` mode does not provide trustworthy Basil process metrics because the wrapper process may be measured instead.
* Multiplayer load must not configure more than 64 rooms or more than 16 players per room.

## Related code

* [`tests/Basil.LoadTests/Program.cs`](../../tests/Basil.LoadTests/Program.cs): run lifecycle and report generation
* [`tests/Basil.LoadTests/Profiles/`](../../tests/Basil.LoadTests/Profiles/): shipped load-test profiles
* [`tests/Basil.LoadTests/Configuration/`](../../tests/Basil.LoadTests/Configuration/): profile configuration types
* [`tests/Basil.LoadTests/Scenarios/`](../../tests/Basil.LoadTests/Scenarios/): scenario implementations and `ScenarioCatalog`
* [`tests/Basil.LoadTests/Client/`](../../tests/Basil.LoadTests/Client/): Bancho and HTTP clients used by scenarios
* [`tests/Basil.LoadTests/Hosting/`](../../tests/Basil.LoadTests/Hosting/): `IServerHost` and host implementations
* [`tests/Basil.LoadTests/Hosting/ServerDatabase`](../../tests/Basil.LoadTests/Hosting/): database snapshot and restore logic
* [`tests/Basil.LoadTests/Infrastructure/`](../../tests/Basil.LoadTests/Infrastructure/): resource sampling, soak analysis, and report generation

## See also

* [`deployment.md`](../for-technicians/deployment.md): deploying Basil and preparing runtime dependencies
* [`https.md`](../for-technicians/https.md): TLS requirements for Basil and load-test hosts
* [`architecture.md`](architecture.md): Basil's host and routing architecture
* [`testing.md`](testing.md): the normal automated test suite, which intentionally excludes `Basil.LoadTests`
