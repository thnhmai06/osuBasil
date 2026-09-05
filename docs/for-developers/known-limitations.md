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

The 2026-09-05 API-only re-run (see RC11 below) did add real capacity-envelope data points, all at commit
`5bde7b5` on 8 logical processors, none reachable from a collapsed server since this run never collapsed:
`GET /health` (no DB) sustained 8,619 req/s at 500 concurrency (p50 45ms/p99 199ms); `GET /users/{id}` (a
SQLite read) sustained 8,592 req/s at 500 concurrency (p50 41ms/p99 261ms). These are lower than the *same*
scenarios' throughput in the original `full-20260905-094716` run (15,229 and 3,688 req/s respectively, though
note the original run's `api_user_500` figure reflects a server already destabilizing partway through, not a
clean baseline) — this run had a `dotnet build` competing for CPU on the same 8-core machine for part of its
duration (`TotalMachineCpuPercent` mean 96%), so these specific numbers should be treated as a lower bound, not
a clean-room measurement. Re-run in isolation before citing these as the capacity baseline. Everything above
500 concurrency, and every write-path number, remains unmeasured either way.

### RC5 — SSE memory leak under load (NEEDS EXPERIMENT; the mechanism itself is already fixed)

The three concrete mechanisms this root cause originally named (`DiffObjects` always producing a non-null,
spammable `{}` diff; `PublishSlotsAsync` having a single call site that starved slot updates; and no
`Writer.Complete()` call on match teardown, leaking the SSE channel) are fixed by ADR-004's SSE rebuild (Phase
3). What was never proven either way is the original claim of an actual memory leak under sustained load: no
load test run has yet exercised "an SSE client stays connected while its match closes" at any real concurrency.
A 93-second soak run showed a flat working set with no leak, but that run never exercised this scenario, so it
neither confirms nor refutes it. Closing this needs the same supervised `full.json` run as the item above.

**Blocker:** same as the capacity item above — needs a supervised multi-hour run.

### RC11 — recurred under a full, supervised, combined-load run (2026-09-05); mechanism still open

ADR-001 (busy-timeout + `synchronous=NORMAL` + collapsing round-trip writes) was verified against the narrow
load profile that originally triggered RC11 (0 failures where there were previously 12 `SQLITE_BUSY` failures at
concurrency 100), but that verification run was short and didn't combine multiplayer with sustained `api` load.
On 2026-09-05 (commit `d5ca2d4`), a supervised run of `Profiles/full.json`'s `login → idle → chat → multiplayer
→ api` sequence (`.loadtest/reports/full-20260905-094716/`) reproduced the same shape of failure RC11 originally
named, starting partway through the `api` scenario (its `api_match_list_500` sub-scenario failed 13390/13390
requests, 100%, and several subsequent sub-scenarios failed heavily):

* **CONFIRMED, from `resources.csv`** (not just the request-level failures): `ThreadPoolQueueLength` climbed
  monotonically from 809 to a peak of 2004 and never recovered; `ThreadCount` climbed 500 → 517; `HandleCount`
  climbed 6776 → 6972; `CpuPercent` sat at ~0.000 throughout (threads blocked on I/O, not computing);
  `TcpConnections` fell 500 → 297 as clients gave up. The transition is abrupt, not gradual: one minute sample
  shows 11 ThreadPool threads at 32% CPU, the next shows 79 threads at 3.6% CPU, and CPU stays pinned near 0%
  for the rest of the run while ThreadPool threads keep climbing at a steady ~1.4/second (the runtime's
  starvation hill-climbing injection rate). Post-collapse failures are overwhelmingly `WSAECONNREFUSED` (a TCP
  RST — the accept backlog was not being drained), meaning most failed requests never reached application code.
* **CONFIRMED, independent of the harness's own reporting:** a native `Test-NetConnection`/`Invoke-WebRequest`
  probe against `127.0.0.1:8443` during the failure window also failed (`TcpTestSucceeded: False`), ruling out a
  client-side (load-generator) artifact.
* **Corrected on re-review (2026-09-05, same day):** the original write-up of this run drew three conclusions
  the artifacts do not actually support, found on a closer read of the NBomber reports and `resources.csv`:
  * *"`api_user_500` ran against an already-collapsed server"* — its own report shows 221,308/224,094 requests
    succeeded at 3,688 RPS with p99 57ms; the failure bucket's own percentiles place the failures at the
    scenario's end, not its start. The collapse happened partway through this scenario, not before it.
  * *"the outbox-full burst preceded and contributed to the collapse"* — the burst is at 10:39 UTC; the
    collapse is at 10:56 UTC. Four scenarios ran cleanly in between, including `api_health_500` at 15,229
    RPS. Seventeen minutes and four healthy scenarios rules this out as a direct precursor. **This burst has
    since been fully root-caused and fixed — see "Outbox-full burst" under Recently closed, below.**
  * *"the DB-free health endpoint surviving isolates the database as the cause"* — scenario order at each
    concurrency tier is fixed (`health → user → match_list → match_report → beatmapset → mixed`), so health
    is the only scenario in the 500-tier that ran *before* the collapse and every DB-touching scenario ran
    *after* it. The observation is explained by ordering, not by the database being uninvolved elsewhere.
  See `plans/rc11-recurrence-analysis-20260905.md` for the full re-review.
* **Logging identified as a contributing factor to the *investigation*, and independently reformed regardless
  of the collapse's cause:** `Logs/latest.log` stopped writing at 17:46 (hit Serilog's 1GB default) while the
  run continued to 18:02 — the exact tooling gap this page already flagged, for the second time. A byte-level
  sample of that log found ~80% of it was one `Information` line per completed API request (3.07M of ~4.11M
  lines), and both file sinks were unbounded synchronous writes reachable from every request thread. All of
  this is now fixed (see [`logging.md`](logging.md)): file sinks roll at 256MB as well as daily; every sink is
  wrapped in `Serilog.Sinks.Async` so a slow console pipe or disk can never block a request thread; the
  per-request API log line is now `Debug` (`Warning` on 5xx) instead of unconditional `Information`; and the
  redundant `UseSerilogRequestLogging()` registration (which built and immediately discarded an event on every
  request) was removed.
* **Re-tested narrower, same day, did not reproduce:** an API-only re-run (`Profiles/api-only.json` — the same
  `api` scenario matrix from `full.json`, with `login`/`idle`/`chat`/`multiplayer` disabled) ran all 18
  sub-scenarios, including `user_500`, `match_list_500`, `match_report_500`, `beatmapset_500`, and `mixed_500`,
  to completion with no collapse: max `ThreadPoolQueueLength` 746 (vs. 2004), max `ThreadCount` 61 (vs. 517),
  and zero samples where CPU was under 0.5% while ThreadCount exceeded 50. **This means the collapse needs
  whatever preceded the `api` phase in the full run — the 70+ minutes of `login`/`idle`/`chat`/`multiplayer`
  load, and/or whatever produced the outbox-full burst — not the `api` load pattern alone.** See
  `.loadtest/reports/api-only-20260905-125951/`.
* **Re-tested with the full sequence, same day, again did not reproduce — after the logging fixes landed.**
  A second `full.json` run (`.loadtest/reports/full-20260905-143915/`, commit `5bde7b5`, with Phase 1/3/4 of
  `plans/rc11-fix-plan-20260905.md` applied — async-wrapped sinks, leveled per-request logging, the harness's
  non-accumulating stdout/stderr drain) ran the complete `login → idle → chat → multiplayer → api` sequence,
  including every 500-concurrency `api` sub-scenario, to completion with no collapse: max `ThreadPoolQueueLength`
  702 (vs. 2004 in the original collapse), max `ThreadCount` 169 (vs. 517) — and that 169 occurred during
  `login`'s 2000-concurrency tier while threads were draining back down (`ThreadPoolQueueLength` was 0 at every
  one of those samples), not during any collapse-shaped event. `TotalMachineCpuPercent` mean was 63%, comparable
  to the original run's 58%, so this was not an easier environment.
* **The outbox-full burst reproduced identically, and its effect on the ThreadPool is now directly measured for
  the first time (CONFIRMED):** 1,056 `MatchRoundEndOutboxFullException` occurrences at 15:28:29–15:31:16 UTC,
  precisely inside this run's `multiplayer_64` window (22:25:17–22:31:26 local, +07:00) — the same scenario and
  same shape of burst as the original run. `resources.csv` for that exact window shows `ThreadPoolQueueLength`
  spiking repeatedly (85 → 115 → 243 → 363 → 288 → 310, several times) while `CpuPercent` continued fluctuating
  normally (never pinned near zero) and the spikes each cleared within seconds. This is the first direct
  measurement connecting the outbox burst to a real, transient ThreadPool queue effect — and equally, direct
  evidence that this effect does not run away into a collapse on its own. (The burst itself was root-caused and
  fixed the next day — see "Outbox-full burst" under Recently closed, below. 88% of the 1,056 count turned out
  to be a load-test harness artifact, not organic server load.)
* **This shifts the balance of evidence toward the console-sink candidate, without a dump to confirm it.**
  The one variable that changed between "collapses every time" (the original run and this investigation's first
  re-run attempt) and "does not collapse across two full-workload attempts" is the logging pipeline: Phase 3
  wrapped every sink in `Serilog.Sinks.Async` (so a stalled console pipe can no longer block a logging caller)
  and Phase 4 replaced the harness's accumulating `ReadToEndAsync` stdout/stderr drain with a bounded,
  continuously-draining one. Nothing else observed in this data changed — the outbox burst, the multiplayer
  load, the API concurrency, and the machine's overall CPU pressure were all comparable to the original run.
  This is consistent with (not proof of) the console-sink-blocking-on-harness-pipe mechanism (Candidate A in
  `plans/rc11-recurrence-analysis-20260905.md`) having been the actual cause, incidentally fixed as a side
  effect of the logging reform rather than diagnosed and fixed directly. The SQLite-write-path candidate
  (Candidate B) remains untested either way — this data doesn't distinguish between "Candidate A was the cause
  and is now fixed" and "the collapse is intermittent and this run simply didn't trigger it."
* **A thread dump during an actual collapse still has not been taken, in three attempts across two
  investigations.** If RC11 recurs in some future run, capturing one (`dotnet-dump collect -p <pid>` then
  `clrstack -all`, or `dotnet-stack report`) is still the one measurement that would close this out with
  certainty rather than an inference from correlation. Until then, treat RC11 as **not currently reproducible
  under this test harness**, not as closed.

**Next step:** if RC11 is suspected again, capture a thread dump at first sign of the collapse signature
(`ThreadPoolQueueLength` climbing while `CpuPercent` stays near zero for more than a few samples) before
assuming it needs a specific scenario sequence to trigger — the evidence above suggests it may not be reliably
reproducible on demand at all. `resources.csv`'s `ThreadPoolQueueLength`/`CpuPercent` and `BasilMetrics`'
`DbCommandDurationMs`/`DbBusyCount`/`MatchLockWaitMs` remain the instrumentation to correlate against if a dump
is obtained.

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
* **Outbox-full burst** (`MatchRoundEndOutboxFullException`, 1,056 occurrences during `multiplayer_64` in the
  2026-09-06 re-run) — root-caused to two independent bugs, both fixed and verified with three repeated
  `multiplayer_64`-only runs:
  1. **Load-test harness bug (not a server bug):** `MultiplayerScenario` used
     `Simulation.KeepConstant(totalPlayers, settings.Duration)`, which (per NBomber's own docs) "keeps a fixed
     number of scenario copies constantly running" — the instant one virtual user's full room lifecycle
     (~15–25s) finished, NBomber respawned a fresh copy under the *same* instance number to hold the count for
     the full 180s window. But `roomMatchIds` (the per-room `TaskCompletionSource<int>` coordinating
     host→followers) is created once for the scenario's entire life. Only the *first* generation's
     `TrySetResult` succeeds; every later respawned generation's followers keep resolving that same stale
     match id for the rest of the 180s, sending real gameplay packets at an already-closed match. Fixed by
     switching to `Simulation.Inject(totalPlayers, settings.Duration, settings.Duration)` — `interval == during`
     is a single one-shot batch, so every copy is injected once and runs to its own natural completion, never
     respawned. This alone cut the exception count from 1,056 to 126 and made request totals land exactly on
     `rooms × playersPerRoom` (512 for 64 rooms), with zero unaccounted extra generations.
  2. **Real server bug, small but genuine:** `MatchCompleteHandler`'s "is anyone still playing" guard has no
     memory of whether the round it just closed was already closed — every slot stays `Complete` once a round
     ends (nothing resets it until the next start), so a duplicate or late-arriving completion for that same
     round passes the guard again and re-enqueues the same round's end. This explained the remaining 126
     exceptions, some concentrated on a single round-id repeated over a dozen times across the whole scenario
     window. Fixed with a one-line idempotency check — `if (!match.InProgress) return;` right after acquiring
     the match lock — that no-ops immediately once a round has already closed. Verified: two more
     `multiplayer_64`-only runs after this fix produced **zero** `MatchRoundEndOutboxFullException` occurrences
     (down from 126). See the new invariant in [`multiplayer.md`](multiplayer.md)'s Invariants section and the
     regression test `MatchCompleteHandlerTests.Handle_CompletionAfterRoundAlreadyClosed_DoesNotReEnqueue`.

  Neither fix touches `MatchRoundEndOutbox`'s capacity (128) or its serial single-consumer drain — with both
  bugs fixed, a room's round genuinely closes at most once, and 128 has comfortable headroom over the 64
  simultaneous closes a fully-synchronized `multiplayer_64` run can produce. No further outbox change is
  needed unless a future measurement shows otherwise.

## Handoff: what the next load test run needs

The 2026-09-05 run (see RC11 above) proved the command and harness work end to end, but also proved things
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
   time. Still open — untouched by this round's logging work.
2. ~~Raise or rotate the log size cap~~ — **done.** File sinks now roll at 256MB as well as daily (see
   [`logging.md`](logging.md)); a run producing several gigabytes of log will roll through several files
   instead of truncating one. Confirmed working during the 2026-09-05 API-only re-run: 8 files of ~256MB each,
   none truncated.
3. **Capture a thread dump during the collapse, this time** — the API-only re-run this round (see RC11 above)
   did not reproduce the collapse, so no dump exists yet from either investigation. The next full-workload run
   should watch `resources.csv`'s `ThreadPoolQueueLength`/`CpuPercent` live and run
   `dotnet-dump collect -p <pid>` (then `clrstack -all`) as soon as CPU drops near zero while the queue is
   climbing — the window was six minutes wide (10:56–11:02 UTC) in the 2026-09-05 run, wide enough to catch
   reliably once watched for.

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
| `Serilog.AspNetCore` / `Serilog.Sinks.Console` / `Serilog.Sinks.File` / `Serilog.Sinks.Async`  | Structured logging, including the size-capped `latest.log`/`errors_latest.log` file sinks and the async wrapper that keeps a sink write from ever blocking a request thread (see [`logging.md`](logging.md)).                |
| `Microsoft.Extensions.Http`                                                                    | `HttpClientFactory`, used by `HttpMirrorSearchClient` (the osu!direct search mirror client).                                               |
| `Microsoft.Extensions.Options` / `.ConfigurationExtensions` / `Configuration.Binder`           | The `IOptions<T>` configuration-binding pattern used throughout (`StorageOptions`, `MirrorOptions`, etc.).                                  |
| `Microsoft.Extensions.Hosting.Abstractions`                                                    | `BackgroundService` base class for `GhostDisconnectService`, `BeatmapWatcherService`, `BeatmapsetMigrationService`, `MatchRoundEndOutbox`.  |
| `Microsoft.Extensions.DependencyInjection.Abstractions` / `Logging.Abstractions`               | DI container and logger abstractions referenced from the Domain/Application layers, which cannot depend on the concrete Infrastructure/Web implementations. |

## See also

* [`database.md`](database.md): SQLite write model and configuration
* [`multiplayer.md`](multiplayer.md): match lifecycle and its invariants
* [`response-envelope.md`](response-envelope.md): the envelope middleware's internal invariant
* [`beatmap-ingestion.md`](beatmap-ingestion.md): the canonical `.osz` storage model
