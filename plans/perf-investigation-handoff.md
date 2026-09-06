# Basil performance investigation — handoff

Branch: `chore/perf-investigation` · PR: #7 · Latest commit as of this handoff: `7085abe`

This document is a handoff summary for whoever picks up this effort next. It is a synthesis, not a
line-by-line translation of the working log this effort was tracked in session-by-session — that raw log
(in Vietnamese, ~28 rounds of incremental notes) lives at [`perf-investigation-log.md`](perf-investigation-log.md)
in this same directory. Read that if you need blow-by-blow reasoning for a specific past decision; this
document gives you the state, the evidence, and where to look next.

**This revision supersedes the previous handoff written at `a7f169a`.** That version left RC11 as an open,
unexplained recurrence. Since then, two follow-up load-test rounds and a root-cause investigation closed
most of the gap — see §4, rewritten below. Do not read `a7f169a`'s version of §4 as current.

## 1. Why this exists

Basil is a private osu! stable server for offline tournaments (see the repo's own `CLAUDE.md` and
`docs/for-developers/architecture.md`). Issue #4 (v1.0.0-alpha.1) recorded roughly 60 bugs/enhancements, and
a 2026-08-14 load test showed the server's first real errors at concurrency 100. Rather than keep
patching individual bugs, the project owner asked for a system-level investigation: find root causes, fix
them with evidence (not guesses), and produce a server that is lightweight, predictable under load, and
minimally dependent — not necessarily one that survives an arbitrary "N users" number.

Three investigation passes (bancho core/concurrency, data layer/API pipeline, load-test evidence) produced
an initial root-cause list (RC1–RC10, later RC11/RC12 added). All are now closed or reduced to a single,
narrow, unmeasured item — see §3 and §4.

## 2. Operating rules (still apply to any further work here)

These constraints shaped every change made so far and should keep shaping what comes next:

1. **Evidence labels**: every claim is `CONFIRMED` (direct evidence: log/measurement/closed-loop code trace),
   `SUPPORTED` (traced but not reproduced), `HYPOTHESIS` (consistent with evidence, unproven), or
   `NEEDS EXPERIMENT` (requires a benchmark/load test not yet run). Do not implement against a HYPOTHESIS or
   NEEDS EXPERIMENT claim as if it were fact.
2. **No silent behavior changes** to the bancho packet protocol, session behavior, or API response schema
   without naming the change explicitly (ADR + updated tests).
3. **Checkpoint per phase**: tests → benchmark → load test → checkpoint, before moving to the next phase.
4. **Decision gates**: anything marked `[GATE]` needs an approved ADR before code.
5. **Dependency removal needs proof**: the goal is *minimal* dependencies, not *fewest possible*. Don't
   remove a library and hand-roll a worse replacement without justification.
6. **No hardcoded "N users" target.** The goal is a capacity envelope + scaling curve with an agreed SLO, not
   a pass/fail number.

## 3. Status at a glance

| Area | Status |
|---|---|
| DB write path (RC1/RC2, ADR-001) | Fixed; verified again under combined load — see §4 |
| Session/match state (RC3/RC4, ADR-002/003) | Done |
| SSE rebuild (RC5, ADR-004) | Mechanism fixed; leak-under-load claim still `NEEDS EXPERIMENT` |
| API pipeline (RC6, ADR-005) | Done; Issue #4's full naming/DTO/OpenAPI audit done |
| Storage (RC7, ADR-006, `.osz` direct storage) | Fully implemented and accepted, including watcher narrowing (Phase 7) |
| Round-end outbox burst (found during RC11 follow-up) | Root-caused and fixed — see §4 |
| Logging pipeline (RC11 follow-up) | Reformed: size-rolling sinks, async-wrapped, leveled per-request logging — see §4 |
| Protocol/allocation perf (RC8) | Not started — `HYPOTHESIS`, blocked on re-profiling under the now-stable server |
| Security (Phase S) | Done, 5/5 items |
| Investigation-only items (`EnvelopeMiddleware` throw sites, `CloseAsync` edge case) | Traced and confirmed unreachable, closed as non-issues |
| Match Hosts/Referees "unable to test" (Issue #4) | Closed — environment constraint, not a defect; covered by existing in-process tests |
| Full supervised load test | Run three times (2026-09-05/06); RC11 no longer reproduces — see §4 |
| Stress/soak scenario ramp (100 → 5000 users, 12h soak) | **Still never run** — config gap unaudited, see §5 |
| 2 OpenAPI spec-conformance bugs found | Dangling `$ref` fixed; duplicate path template still needs a decision — see §6 |

Full test suite: 1598 tests passing, Release build clean, as of `7085abe`.

## 4. RC11 — where it actually landed

**RC11** originally meant "the server dies completely under combined multiplayer + API load," root-caused
to SQLite write-path saturation. ADR-001 (busy-timeout, `synchronous=NORMAL`, collapsed round-trip writes)
fixed this against the narrow profile that first found it, but that verification never covered the actual
combined-load scenario.

**Round 1 (2026-09-05, commit `d5ca2d4`)**: a supervised run of `Profiles/full.json`'s full
`login → idle → chat → multiplayer → api` sequence reproduced RC11's original failure shape almost exactly —
`ThreadPoolQueueLength` climbing 809 → 2004 with no recovery, `CpuPercent` pinned near 0 (threads blocked on
I/O, not computing), and the DB-free `api_health_500` scenario running clean while every DB-touching
scenario after it failed. Confirmed independent of the harness with a native `Test-NetConnection` probe.
Full detail: `plans/rc11-recurrence-analysis-20260905.md`.

**Re-review the same day** corrected three conclusions the raw evidence didn't actually support (the
`api_user_500` scenario was not already-collapsed when it started; the round-end-outbox burst preceded the
collapse by 17 minutes and 4 healthy scenarios, ruling it out as a direct precursor; the DB-free endpoint
surviving is explained by scenario ordering, not proof the database is uninvolved). Also found:
`Logs/latest.log` had stopped writing at 17:46 (hit Serilog's 1GB default) while the run continued to
18:02 — the same tooling gap that had already cost a prior investigation its evidence, for the second time.

**The fix plan** (`plans/rc11-fix-plan-20260905.md`) called for naming the blocking mechanism via a thread
dump *before* reforming logging, since the logging reform alone was expected to make Candidate A (console
sink blocking on the load-harness's stdout pipe) disappear without ever confirming it was the cause. In
practice: an API-only re-run (Phase 2, scope-narrowed at the user's request) did not reproduce the
collapse, so there was no dump to take, and Phases 2b/5's SQLite-specific fix were skipped rather than
guessed at. The user chose to proceed with the logging reform anyway and record RC11's mechanism as open.

**Logging reform shipped** (commit `7085abe`, Phases 1/3/4/6 of the fix plan):
- File sinks roll at 256MB in addition to daily (previously silently truncated at Serilog's 1GB default).
- Both sinks wrapped in `Serilog.Sinks.Async`, so a stalled console pipe or slow disk can never block a
  request thread.
- Per-request API logging leveled to `Debug` (`Warning` on 5xx) instead of unconditional `Information` —
  this alone was ~80% of a collapsed run's 1GB log.
- Removed a redundant `UseSerilogRequestLogging()` registration that built and discarded a log event on
  every request.
- The load-test harness itself no longer accumulates a run's entire stdout/stderr in memory.

**Round 2 (same day, commit `5bde7b5`)**: the *full* `login → idle → chat → multiplayer → api` sequence,
with the logging reform applied, completed with **no collapse**: max `ThreadPoolQueueLength` 702 (vs. 2004
in round 1), max `ThreadCount` 169 (vs. 517), comparable machine CPU pressure to round 1 (63% vs. 58% mean).

**The round-end-outbox burst reproduced identically in round 2** (1,056 `MatchRoundEndOutboxFullException`
occurrences during the `multiplayer_64` window) and was root-caused the next day (commit `7085abe`):

1. **Load-test harness bug, not a server bug**: `MultiplayerScenario` used `Simulation.KeepConstant`, which
   respawns a fresh scenario copy under the same instance number the instant one virtual user's room
   lifecycle finishes. The per-room `TaskCompletionSource` coordinating host→followers is created once for
   the scenario's whole life, so every respawned generation's followers kept resolving the *first*
   generation's stale match id and sending gameplay packets at an already-closed match. Fixed by switching
   to `Simulation.Inject` with `interval == during` — a one-shot batch where every copy runs once to its own
   natural completion. Cut the exception count from 1,056 to 126.
2. **Real server bug, small but genuine**: `MatchCompleteHandler`'s "anyone still playing" guard had no
   memory of whether the round it just closed was already closed, so a duplicate or late-arriving
   completion for that round re-entered the block and re-enqueued the same round's end. Fixed with a
   one-line idempotency check (`if (!match.InProgress) return;`) right after acquiring the match lock.
   Verified with two further `multiplayer_64`-only runs: zero `MatchRoundEndOutboxFullException` occurrences.

**Where this leaves RC11's mechanism**: still not named from a stack — no thread dump was ever taken,
across three attempts in two investigations. The balance of evidence favors the console-sink-blocking
candidate (the one variable that changed between "collapses every time" and "doesn't collapse across two
full-workload attempts" was the logging pipeline), but this is inference from correlation, not proof. The
SQLite-write-path candidate remains untested either way. **Treat RC11 as not currently reproducible under
this test harness — not as a confirmed-fixed root cause.** If it recurs in some future run, capturing a
thread dump at the first sign of the collapse signature (`ThreadPoolQueueLength` climbing while
`CpuPercent` stays near zero) is the one measurement that would close this with certainty. Full detail and
the corrected evidence table: `docs/for-developers/known-limitations.md`'s RC11 entry — that document is
the authoritative, living record; this section is a point-in-time summary of it.

The full evidence for all three runs is preserved at `.loadtest/reports/full-20260905-094716/`,
`.loadtest/reports/api-only-20260905-125951/`, and `.loadtest/reports/full-20260905-143915/` (not committed
to the repo — local artifacts; copy them somewhere durable if the machine will be reimaged).

## 5. Everything else that's still open

- **Stress/soak scenario ramp never run**: `full.json`'s `stress` (100 → 5000 concurrent users) and `soak`
  (12h at 750) scenario sections produced no variants and were skipped entirely in every run so far — a
  config gap, not yet audited against `ScenarioCatalog`'s variant-selection logic. The server's capacity
  envelope and scaling-curve knees above what's been exercised (multiplayer 64 rooms, `api` cluster
  concurrency up to 500) remain unknown. **This is the actual next blocking item**, not RC11 itself — RC11
  no longer reproduces, but the stress/soak gap has never been touched.
- **RC5 — SSE leak under load** (`NEEDS EXPERIMENT`): the three concrete mechanisms this originally named
  are fixed (ADR-004). Whether an actual memory leak exists under sustained load with SSE clients connected
  through match close/reopen churn is unproven either way — needs a supervised run specifically exercising
  that scenario (a 93-second soak showed a flat working set, but never exercised this scenario).
- **RC8 — protocol allocation / login fan-out** (`HYPOTHESIS`): presence confirmed in code
  (`BinaryWriter`-per-primitive allocation, `GameSession`'s double-copy `Dequeue()`), but whether it's the
  *next* bottleneck is not established. Blocked on re-profiling — now unblocked in principle since RC11 no
  longer reproduces, but the stress/soak run above should happen first so profiling targets the real ceiling.
- **Tourney-client concurrency fix** (`HYPOTHESIS`): the `HashSet<int>` → `ConcurrentDictionary<int, byte>`
  change is safe and cheap either way; whether it was ever actually necessary has never been observed. Low
  priority.

Everything else from the original root-cause list (RC1–RC4, RC6, RC7, RC12, the two investigation-only items,
Phase 7, and the outbox-full burst) is closed. See `docs/for-developers/known-limitations.md`'s "Recently
closed" section for the closure evidence on each.

## 6. Two OpenAPI spec-conformance bugs found (2026-09-05); one fixed, one needs a decision

Found while self-checking the 6 generated OpenAPI documents (`bancho`, `osuweb`, `beatmapassets`, `avatar`,
`assets`, `basilapi`) with a throwaway validator built on the same `Microsoft.OpenApi` 2.11.0 package the
project already depends on (`OpenApiDocument.Parse` + its `Diagnostic.Errors`/`Warnings`). 5 of 6 documents
are clean. `basilapi.json` had:

1. **Duplicate path template** (spec violation, not an ASP.NET routing bug) — **still open, needs a
   decision**: `GET /users/{idOrName}` and `PUT /users/{userId}` (and their `/avatar` children) normalize to
   the same template (`/users/{}`) once parameter names are ignored — OpenAPI 3.x requires path templates to
   be unique regardless of parameter naming, even though the two routes are perfectly distinguishable by
   HTTP method at the actual routing layer. `PUT` genuinely only accepts a numeric id (admin action); `GET`
   accepts either an id or a username — renaming either parameter to match the other would make one of the
   two descriptions inaccurate. Needs a design decision (accept the pedantic non-conformance, or rename one
   side and document the accepted inaccuracy) from the project owner, not a unilateral fix.
2. **Dangling `$ref`** — **fixed** (commit `611ed36`): the `oneOf` response schema for two SSE routes —
   `GET /matches/{matchId}/live` and `GET /users/{idOrName}/live` — referenced `MatchLiveSnapshot` and
   `PlayerStatusView` respectively via `$ref`, but neither type had a schema registered in
   `components.schemas`. Root cause: both operations declare `.Produces<T>()` twice on the same status code
   (`MatchRoutes.cs:210-211`, `UserRoutes.cs:353-354`), and only the *last*-declared type of the pair
   actually gets a registered component schema. Fixed in `OpenApiExampleExtensions.cs` by generating the
   lost type's schema explicitly via .NET 10's `OpenApiOperationTransformerContext.GetOrCreateSchemaAsync`
   instead of assuming it was promoted to a named component — it comes back inlined rather than `$ref`'d,
   which is correct since neither type is reused by any other operation. Verified with a dedicated
   regression test (`BasilApiDocument_SseRouteUnsharedPayloadType_IsNotADanglingRef`), confirmed to fail
   against the pre-fix code via revert-and-fail.

Wording/description content in all 6 documents is clean against `CLAUDE.md`'s rule 5 (no implementation
details in `.WithSummary`/`.WithDescription`) and shows no AI-writing filler patterns. This audit has not
been re-run since; re-run it if any route's `.Produces<T>()` declarations change again.

## 7. Where to find things

- [`docs/for-developers/known-limitations.md`](../docs/for-developers/known-limitations.md) — the
  authoritative, versioned tracking doc for every open root cause, the dependency inventory, and the load
  test handoff instructions. Keep this updated as things close or reopen; this handoff document is a
  point-in-time summary, that file is the living one.
- [`perf-investigation-log.md`](perf-investigation-log.md) — the full session-by-session working log
  (Vietnamese), for the reasoning behind any specific past decision.
- [`rc11-recurrence-analysis-20260905.md`](rc11-recurrence-analysis-20260905.md) /
  [`rc11-fix-plan-20260905.md`](rc11-fix-plan-20260905.md) — RC11's round-1 evidence and the fix plan that
  produced the logging reform; both are self-marked as superseded by `known-limitations.md` for current
  status.
- [`docs/adr/`](../docs/adr/) — ADR-003/004/005/007 (ADR-001, 002, 006 were implementation-scoped and
  deleted once done, per an explicit project-owner decision that ADRs here are not permanent documents).
  ADR-006 is the last implementation-scoped one remaining as of this handoff — `docs/` no longer references
  it (that cross-reference dependency was removed), so it can be deleted whenever the project owner wants.
- [`docs/for-developers/working-scopes.md`](../docs/for-developers/working-scopes.md) — what Basil is and
  isn't scoped to do; check before adding anything bancho.py has that Basil doesn't.
- [`tests/Basil.LoadTests/`](../tests/Basil.LoadTests/) — the load-test harness. Run with:
  ```bash
  dotnet run --project tests/Basil.LoadTests -- --profile full
  ```
  (`--profile` takes a bare name, resolved to `Profiles/<name>.json`.) There's also `Profiles/api-only.json`
  for scoped API-only iteration, added during the RC11 follow-up.
- `CLAUDE.md` (repo root) — the full set of engineering rules this effort has followed throughout
  (surgical changes, evidence labels, test contract rules, API/XML doc conventions). Read it before making
  any change here.

## 8. Recommended immediate next step

1. **Audit why `full.json`'s `stress`/`soak` scenarios produce no variants.** This is the actual remaining
   blocker for the capacity-envelope/scaling-curve deliverable — RC11 no longer blocks it.
2. Once that's fixed, run the stress ramp and soak under supervision, watching for RC11's signature
   (`ThreadPoolQueueLength` climbing while `CpuPercent` stays near zero) and ready to capture a thread dump
   (`dotnet-dump collect -p <pid>` then `clrstack -all`) immediately if it appears — this is still the one
   measurement that would close RC11 with certainty rather than an inference from correlation.
3. Independently of the above: decide on the remaining OpenAPI item in §6 (the duplicate-path-template
   non-conformance) — the dangling `$ref` is already fixed.
4. Only after the stress/soak run has a real capacity ceiling to profile against: start RC8 (protocol
   allocation) re-profiling. Don't optimize it speculatively before then.
