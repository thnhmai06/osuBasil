# Known limitations

## Overview

This page lists what the perf-investigation effort (tracked outside the repo; see the PR history on
`chore/perf-investigation`) has **not** proven, alongside a full inventory of Basil's production dependencies
and why each exists.

A root cause here is either:

* **SUPPORTED** — the mechanism is traced through the code but has not been reproduced under real conditions, or
* **HYPOTHESIS** — consistent with the evidence gathered so far, but not yet demonstrated, or
* **NEEDS EXPERIMENT** — the only way to close it is a benchmark or load test that has not been run.

Do not treat an entry here as a confirmed bug to fix on sight. Each one already has a concrete next step; follow
that step (usually "reproduce it" or "measure it") before writing a fix.

## Open items

### Capacity above 100 concurrent users is unmeasured (NEEDS EXPERIMENT)

A supervised run on 2026-09-05 (commit `d5ca2d4`, see RC11 below) completed `login → idle → chat → multiplayer →
api`, but `full.json`'s `stress`/`soak` scenario sections produced no variants and were skipped entirely --
config gap, not yet re-checked against the profile file. The stress ramp (100 → 5000 concurrent users) and the
12-hour soak at 750 have therefore still never run. The server's capacity envelope and scaling-curve knees above
the levels exercised (multiplayer 64 rooms, the `api` cluster's own concurrency) remain unknown, and are now
additionally blocked on RC11 recurring at that boundary (see below) before a stress ramp would even be
meaningful to run.

**Blocker:** fix or confirm the `full.json` stress/soak scenario configuration, resolve RC11's recurrence, then
run under supervision again.

### RC5 — SSE memory leak under load (NEEDS EXPERIMENT; the mechanism itself is already fixed)

The three concrete mechanisms this root cause originally named (`DiffObjects` always producing a non-null,
spammable `{}` diff; `PublishSlotsAsync` having a single call site that starved slot updates; and no
`Writer.Complete()` call on match teardown, leaking the SSE channel) are fixed by ADR-004's SSE rebuild (Phase
3). What was never proven either way is the original claim of an actual memory leak under sustained load: no
load test run has yet exercised "an SSE client stays connected while its match closes" at any real concurrency.
A 93-second soak run showed a flat working set with no leak, but that run never exercised this scenario, so it
neither confirms nor refutes it. Closing this needs the same supervised `full.json` run as the item above.

**Blocker:** same as the capacity item above — needs a supervised multi-hour run.

### RC11 — recurred under a full, supervised, combined-load run (2026-09-05) despite ADR-001

ADR-001 (busy-timeout + `synchronous=NORMAL` + collapsing round-trip writes) was verified against the narrow
load profile that originally triggered RC11 (0 failures where there were previously 12 `SQLITE_BUSY` failures at
concurrency 100), but that verification run was short and didn't combine multiplayer with sustained `api` load.
On 2026-09-05 (commit `d5ca2d4`), a supervised run of `Profiles/full.json`'s `login → idle → chat → multiplayer
→ api` sequence (`.loadtest/reports/full-20260905-094716/`) reproduced the same shape of failure RC11 originally
named, starting partway through the `api` scenario (its `api_match_list_500` sub-scenario failed 13390/13390
requests, 100%, and several subsequent sub-scenarios failed heavily):

* **CONFIRMED, from `resources.csv`** (not just the request-level failures): by the run's final samples,
  `ThreadPoolQueueLength` climbed monotonically from 809 to a peak of 2004 and never recovered; `ThreadCount`
  climbed 500 → 517; `HandleCount` climbed 6776 → 6972; `CpuPercent` sat at ~0.000 throughout (threads blocked on
  I/O, not computing); `TcpConnections` fell 500 → 297 as clients gave up. This is the same signature the
  original RC11 investigation used to conclude "ThreadPool saturation, not a crash" -- CPU near zero, queue
  depth growing without bound, no recovery.
* **CONFIRMED, independent of the harness's own reporting:** a native `Test-NetConnection`/`Invoke-WebRequest`
  probe against `127.0.0.1:8443` during the failure window also failed (`TcpTestSucceeded: False`), ruling out a
  client-side (load-generator) artifact.
* **Gap re-confirmed:** `Logs/latest.log` stopped writing at 17:46 (hit its ~1 GB size cap) while the run
  continued until 18:02 -- the exact tooling gap this page already flagged lost visibility across precisely the
  window that mattered.
* **Not yet root-caused further this round:** whether this is the same RC1 mechanism resurfacing at a different
  concurrency/duration than ADR-001's own verification covered, or a related-but-distinct saturation path, is
  not established. Re-profiling (needed for RC8 below) should not proceed until this is understood, per the
  plan's "profile first" rule -- optimizing anything else while this is open risks fixing the wrong bottleneck.

**Next step:** investigate the write-path/lock-contention state during the `multiplayer` → `api` transition
specifically (the same transition that first exposed RC11), using `resources.csv` and `BasilMetrics`'
`DbCommandDurationMs`/`DbBusyCount`/`MatchLockWaitMs` instrumentation already in place, ideally with the log
size cap raised or rotation fixed first so the failure window isn't a blind spot again.

### RC8 — protocol allocation and login fan-out (HYPOTHESIS)

`BinaryWriter`-per-primitive packet allocation, `GameSession`'s outbound queue's double-copy `Dequeue()`, and
login's channel-info broadcast fan-out are all confirmed present in the code. Whether any of them is the *next*
bottleneck after RC1 (SQLite writes) and RC5 (SSE) were addressed is not established — that requires re-profiling
under load, which needs the same supervised run as the two items above. Do not optimize these speculatively; the
plan's own rule for this phase is "profile first."

### Tourney client tracking's concurrency fix has unconfirmed real-world impact (HYPOTHESIS)

`_tourneyClients` was changed from a plain `HashSet<int>` to `ConcurrentDictionary<int, byte>` (matching the
pattern already used by `_referees`/`_bannedIds`/`_invitedIds` in the same file) because concurrent, unsynchronized
mutation of a `HashSet<int>` is a real category of bug. Whether it was ever actually hit — i.e., whether real
tourney client traffic produces the concurrent access this fix protects against — has never been observed, since
no load test scenario exercises tourney-client connections. The fix is safe and low-cost either way; only its
necessity is unconfirmed.

### Match Hosts/Referees API — cannot be manually exercised with real osu! clients in this environment

Issue #4 marked this "Unable to test" because reproducing it needs two osu! client connections at once, and the
environment that reported it only runs one client at a time (confirmed: this is an environment constraint, not
evidence of a defect). Basil's own test suite already exercises multi-session host/referee transitions in-process
(constructing `GameSession` objects directly, which does not require a second real client): see
`MatchTransferHostHandlerTests` (host transfer, including the TOCTOU re-check under lock), the referee-focused
cases in `MpCommandServiceTests` (`HandleAsync_AddRef_*`, `HandleAsync_RemoveRef_*`, `RemoveRef_LastReferee_*`),
and the API-level cases in `MatchSubResourceEndpointTests` (`Refs_*`, `Hosts_*`, `Ban_*`). If a specific
host/referee transition is ever suspected of a real bug, write an in-process regression test for that exact
transition rather than trying to reproduce it with two real clients.

## Recently closed (investigated this round, not left open)

* **`EnvelopeMiddleware`'s two internal throw sites** (`JsonNode.ParseAsync`, `GetValue<int>()`) — traced and
  confirmed unreachable by any current `basilapi` route (every enveloped body is framework-serialized from a
  typed object, never a re-parse of client input), and covered by `ExceptionLoggingMiddleware` even if they were
  ever hit. See [`response-envelope.md`](response-envelope.md)'s "Internal invariant" section.
* **`MatchMembershipService.CloseAsync`'s slot/registry-miss edge case** — traced and confirmed unreachable:
  `PlayerLogoutService.LogoutAsync` is the only code path that removes a `GameSession` from the registry, and it
  always clears the match slot under the same match lock first. See [`multiplayer.md`](multiplayer.md)'s
  invariants list.
* **Phase 7 — narrowing `BeatmapWatcherService` to non-recursive, `.osz`-only watching** — this was previously
  blocked on "no legacy folder left to reach," a statement about deployment data rather than something fixable
  in code. On closer trace, the real coupling was narrower: `BeatmapsetRoutes.HandleReplace`/`HandleDelete`'s
  legacy-folder branches didn't reconcile the DB themselves; they relied on the watcher noticing the folder
  change. Making both branches call `BeatmapIngestionService.ReconcileFolderAsync`/`ReconcileDeletedFolderAsync`
  inline removed that reliance, so the watcher's folder-watching (`IncludeSubdirectories`, the
  `Directory.Exists`/deleted-folder arms in `Settle`) could be dropped outright — a legacy folder still gets
  reconciled by startup reconciliation, the migration pass, or the route that writes to it, just never by the
  live watcher. See [`beatmap-ingestion.md`](beatmap-ingestion.md)'s "Ingestion triggers" section.

## Handoff: what the next load test run needs

The 2026-09-05 run (see RC11 above) proved the command and harness work end to end, but also proved two things
worth fixing before the next run rather than re-discovering again:

```bash
dotnet run --project tests/Basil.LoadTests -- --profile full
```

(`--profile` takes the profile name only, not a path; the harness resolves it to
`tests/Basil.LoadTests/Profiles/full.json` relative to the built output.) Before the next run:

1. **Check why `stress`/`soak` produced no variants** — `full.json` is supposed to ramp 100 → 5000 concurrent
   users then soak at 750 for 12 hours, but the 2026-09-05 run's log showed both scenarios "disabled or produced
   no variants; skipping." Read `Scenarios:stress`/`Scenarios:soak` in the profile and whatever gates
   `ScenarioCatalog` uses to decide a scenario has variants, before assuming the ramp/soak will actually run next
   time.
2. **Raise or rotate the log size cap** — `Logs/latest.log`/`errors_latest.log` hit their ~1 GB cap and stopped
   writing 16 minutes before the run actually ended, for the second time this effort has needed exactly that
   window. Either raise the cap for a load-test run specifically, or fix log rotation so a capped file rolls
   into a fresh one instead of going silent (see [`logging.md`](logging.md)).

Watch for the server process exiting or stopping responding to `/health`; if it does (or if `resources.csv`
shows `ThreadPoolQueueLength` climbing without recovering, as it did this round), capture the last ~2 minutes of
`Logs/latest.log`/`Logs/errors_latest.log` and the process's final `ThreadPoolQueueLength`/`ThreadCount` from
`resources.csv` before stopping the run.

## Final dependency inventory

Every third-party package referenced by a production (`src/`) project, and why it exists. Test-only tooling
(xUnit, NSubstitute, BenchmarkDotNet, coverlet) is omitted — its purpose is standard and not a project-specific
decision.

| Package                                                                                      | Why it exists                                                                                                                             |
|-----------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `Microsoft.Data.Sqlite`                                                                       | The database engine. Chosen for a single-process, self-contained tournament server with no separate DB service to install (see [`database.md`](database.md)). |
| `Dapper`                                                                                       | Thin SQL mapper over `Microsoft.Data.Sqlite`. Chosen deliberately over a full ORM so repositories keep explicit control of their SQL.       |
| `dbup-sqlite`                                                                                  | Runs the numbered schema migrations (`001_base.sql` onward) at startup, on both a fresh database and an upgrading one. Previously flagged as a removal candidate (a hand-rolled runner could replace it for this project's small migration count); not yet evaluated. |
| `Microsoft.Extensions.Caching.Memory`                                                          | Backs the read-through caching decorators (beatmap/beatmapset/user/settings repositories) and `BeatmapsetAssetCache`.                       |
| `BCrypt.Net-Next`                                                                              | Hashes the admin key and user passwords.                                                                                                    |
| `BouncyCastle.Cryptography`                                                                    | Rijndael-256 decryption of osu! stable's legacy score-submission encoding; no modern alternative implements this legacy scheme.             |
| `FFMpegCore`                                                                                   | Extracts and trims the 10-second audio preview clip (with fade-out) from a beatmapset's audio file.                                         |
| `SixLabors.ImageSharp` / `SixLabors.ImageSharp.Web`                                            | Beatmap background/thumbnail/menu-icon image processing, and the on-the-fly resize provider middleware ahead of routing. Previously flagged as a removal candidate (the middleware runs its 6 providers ahead of routing on every bancho request); not yet evaluated. |
| `ppy.osu.Game.Rulesets.{Osu,Taiko,Catch,Mania}`                                                | Official osu! difficulty/star-rating calculators. Display-only (no pp-based gameplay); kept per an explicit user decision earlier in this effort rather than reimplemented. |
| `Microsoft.AspNetCore.OpenApi` / `Microsoft.OpenApi` / `Microsoft.Extensions.ApiDescription.Server` | Generates the OpenAPI schema for every host (`bancho`, `osuweb`, `beatmapassets`, `avatar`, `assets`, `basilapi`) at build time.        |
| `Scalar.AspNetCore`                                                                            | Serves the interactive API reference UI at `/docs/basil-api/`.                                                                             |
| `Serilog.AspNetCore` / `Serilog.Sinks.Console` / `Serilog.Sinks.File`                          | Structured logging, including the size-capped `latest.log`/`errors_latest.log` file sinks (see [`logging.md`](logging.md)).                |
| `Microsoft.Extensions.Http`                                                                    | `HttpClientFactory`, used by `HttpMirrorSearchClient` (the osu!direct search mirror client).                                               |
| `Microsoft.Extensions.Options` / `.ConfigurationExtensions` / `Configuration.Binder`           | The `IOptions<T>` configuration-binding pattern used throughout (`StorageOptions`, `MirrorOptions`, etc.).                                  |
| `Microsoft.Extensions.Hosting.Abstractions`                                                    | `BackgroundService` base class for `GhostDisconnectService`, `BeatmapWatcherService`, `BeatmapsetMigrationService`, `MatchRoundEndOutbox`.  |
| `Microsoft.Extensions.DependencyInjection.Abstractions` / `Logging.Abstractions`               | DI container and logger abstractions referenced from the Domain/Application layers, which cannot depend on the concrete Infrastructure/Web implementations. |

## See also

* [`database.md`](database.md): SQLite write model and configuration
* [`multiplayer.md`](multiplayer.md): match lifecycle and its invariants
* [`response-envelope.md`](response-envelope.md): the envelope middleware's internal invariant
* [`beatmap-ingestion.md`](beatmap-ingestion.md): the canonical `.osz` storage model
