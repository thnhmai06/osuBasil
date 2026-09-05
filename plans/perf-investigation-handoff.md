# Basil performance investigation — handoff

Branch: `chore/perf-investigation` · PR: #7 · Latest commit as of this handoff: `a7f169a`

This document is a handoff summary for whoever picks up this effort next. It is a synthesis, not a
line-by-line translation of the working log this effort was tracked in session-by-session — that raw log
(in Vietnamese, ~30 rounds of incremental notes) exists outside this repo. Ask the previous owner for it if
you need blow-by-blow reasoning for a specific past decision; this document gives you the state, the
evidence, and where to look next.

## 1. Why this exists

Basil is a private osu! stable server for offline tournaments (see the repo's own `CLAUDE.md` and
`docs/for-developers/architecture.md`). Issue #4 (v1.0.0-alpha.1) recorded roughly 60 bugs/enhancements, and
a 2026-08-14 load test showed the server's first real errors at concurrency 100. Rather than keep
patching individual bugs, the project owner asked for a system-level investigation: find root causes, fix
them with evidence (not guesses), and produce a server that is lightweight, predictable under load, and
minimally dependent — not necessarily one that survives an arbitrary "N users" number.

Three investigation passes (bancho core/concurrency, data layer/API pipeline, load-test evidence) produced
an initial root-cause list (RC1–RC10, later RC11/RC12 added). Most are closed. **RC11 is not**, and it is
the reason this handoff exists now rather than after a clean finish — see §4.

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
| DB write path (RC1/RC2, ADR-001) | Fixed in isolation; **recurring under combined load — see §4** |
| Session/match state (RC3/RC4, ADR-002/003) | Done |
| SSE rebuild (RC5, ADR-004) | Mechanism fixed; leak-under-load claim still `NEEDS EXPERIMENT` |
| API pipeline (RC6, ADR-005) | Done; Issue #4's full naming/DTO/OpenAPI audit done |
| Storage (RC7, ADR-006, `.osz` direct storage) | Fully implemented and accepted |
| Protocol/allocation perf (RC8) | Not started — blocked on RC11 (profile-first rule) |
| Security (Phase S) | Done, 5/5 items |
| Beatmap watcher narrowing (Phase 7) | Done |
| Full supervised load test | **Run once (2026-09-05); found RC11 recurring — see §4** |
| 2 OpenAPI spec-conformance bugs found | **Not fixed — see §6** |

Full test suite: 1597 tests passing (Domain 114, Protocol 158, Application 710, Architecture 9,
Infrastructure 256, IntegrationTests 350) as of `d5ca2d4`. Release build clean.

## 4. The open, urgent item: RC11 recurred

**RC11** originally meant "the server dies completely under combined multiplayer + API load," root-caused
to SQLite write-path saturation (`Microsoft.Data.Sqlite` is not truly async, so writes block pool threads;
once write throughput is exceeded for long enough, the ThreadPool backlog grows without bound and never
recovers on its own). ADR-001 (busy-timeout, `synchronous=NORMAL`, collapsed round-trip writes) fixed this
against the narrow profile that first found it (0 failures at concurrency 100, vs. 12 `SQLITE_BUSY` failures
before the fix).

**That verification never covered the actual combined-load scenario.** On 2026-09-05, a supervised run of
`Profiles/full.json`'s `login → idle → chat → multiplayer → api` sequence reproduced the same failure shape,
starting at the `multiplayer → api` transition:

- `api_match_list_500` (one of 18 `api` sub-scenarios, at the highest tested concurrency, 500) failed
  **13390/13390 requests — 100%**. Several subsequent sub-scenarios (`api_match_report_500`,
  `api_beatmapset_500`, `api_mixed_500`) stayed collapsed.
- `resources.csv` for the run confirms this independent of request-level reporting: by the run's final
  samples, `ThreadPoolQueueLength` climbed monotonically 809 → 2004 with no recovery, `ThreadCount` climbed
  500 → 517, `HandleCount` climbed 6776 → 6972, `CpuPercent` sat near 0.000 (threads blocked on I/O, not
  computing), and `TcpConnections` fell 500 → 297 as clients gave up. This is the exact signature the
  original RC11 investigation used to distinguish "ThreadPool saturation, not a crash."
- Confirmed independently of the load-test harness: a native `Test-NetConnection`/`Invoke-WebRequest` probe
  against `127.0.0.1:8443` during the failure window also failed to connect (`TcpTestSucceeded: False`),
  ruling out a client-side (load generator) artifact.
- `Logs/latest.log` stopped writing at 17:46 (hit its ~1 GB size cap) while the run continued to 18:02 —
  the same tooling gap the original RC11 investigation hit, losing log visibility across exactly the window
  that mattered, for the second time.
- The one endpoint type that did **not** fail at the same concurrency: `api_health_500` (no DB access) ran
  clean (913744 ok / 38 timeout). This rules out a generic "Kestrel can't handle 500 concurrent connections"
  explanation — every DB-touching endpoint failed together regardless of which one, while the DB-free one
  didn't. That points at the SQLite/ThreadPool mechanism, not a bug in any one route's own code.
- `errors_latest.log` shows a burst of `MatchRoundEndOutboxFullException` ("Round-end outbox is full") at
  17:39, right as `multiplayer_64` finished and `api` began — consistent with a write backlog from
  multiplayer's round-end persistence not draining before `api`'s load ramped through 50 → 200 → 500
  concurrency over the following ~16 minutes, tipping into full saturation once it hit 500.
- One route under test at the time this happened was `GET /matches/{matchId}` (the tournament match report,
  `MatchReportService`) — it did **not** cause this. `api_health_500` ran clean immediately before the
  collapse, and the collapse had already started (at `api_user_500`) two sub-scenarios before the match
  report scenario even ran. It inherited an already-saturated server, like every other DB-touching endpoint
  tested after it.

**Conclusion: ADR-001 is not sufficient to close RC11.** The narrow verification (login-only, concurrency
100) does not represent the actual combined multiplayer+API load pattern. This needs new investigation, not
a quick patch — per the project's own "profile first" rule, do not optimize RC8 (protocol allocation) or
anything else until this is understood, since it risks fixing the wrong bottleneck.

**Suggested next step**: investigate DB write throughput / lock contention specifically across the
`multiplayer → api` transition, using the `BasilMetrics` instrumentation already in place
(`DbCommandDurationMs`, `DbBusyCount`, `MatchLockWaitMs`) — ideally after raising or fixing the log size cap
so the failure window isn't a blind spot again (see `docs/for-developers/known-limitations.md`'s "Handoff"
section for the two things to fix before the next run: the log cap, and why `full.json`'s `stress`/`soak`
scenarios produced no variants this run and need auditing).

The full run's evidence is preserved at `.loadtest/reports/full-20260905-094716/` (not committed to the
repo — it's a local artifact; copy it somewhere durable if the machine will be reimaged).

## 5. Everything else that's still open

- **RC5 — SSE leak under load** (`NEEDS EXPERIMENT`): the three concrete mechanisms this originally named
  are fixed (ADR-004). Whether an actual memory leak exists under sustained load with SSE clients connected
  through match close/reopen churn is unproven either way — needs the same kind of supervised run as RC11,
  specifically exercising that scenario.
- **RC8 — protocol allocation / login fan-out** (`HYPOTHESIS`): presence confirmed in code
  (`BinaryWriter`-per-primitive allocation, `GameSession`'s double-copy `Dequeue()`), but whether it's the
  *next* bottleneck after RC1/RC5 is not established. Blocked on re-profiling, which is blocked on RC11.
- **Capacity envelope / scaling curve** (Definition of Done item): unmeasured above what's been exercised.
  Blocked on the same supervised-run requirement as RC11, plus the `stress`/`soak` config gap found this
  round.
- **Tourney-client concurrency fix** (`HYPOTHESIS`): the `HashSet<int>` → `ConcurrentDictionary<int, byte>`
  change is safe and cheap either way; whether it was ever actually necessary (real tourney-client traffic
  hitting concurrent access) has never been observed. Low priority.
- **Match Hosts/Referees API "Unable to test"**: this is an environment constraint (the original reporter
  only had one osu! client to test with), not a defect — Basil's own test suite already exercises
  multi-session host/referee transitions in-process. Closed as far as this effort is concerned; see
  `known-limitations.md`.

## 6. Two OpenAPI spec-conformance bugs found (2026-09-05), not yet fixed

Found while self-checking the 6 generated OpenAPI documents (`bancho`, `osuweb`, `beatmapassets`, `avatar`,
`assets`, `basilapi`) with a throwaway validator built on the same `Microsoft.OpenApi` 2.11.0 package the
project already depends on (`OpenApiDocument.Parse` + its `Diagnostic.Errors`/`Warnings`). 5 of 6 documents
are clean. `basilapi.json` has:

1. **Duplicate path template** (spec violation, not an ASP.NET routing bug): `GET /users/{idOrName}` and
   `PUT /users/{userId}` (and their `/avatar` children) normalize to the same template
   (`/users/{}`) once parameter names are ignored — OpenAPI 3.x requires path templates to be unique
   regardless of parameter naming, even though the two routes are perfectly distinguishable by HTTP method
   at the actual routing layer. `PUT` genuinely only accepts a numeric id (admin action); `GET` accepts
   either an id or a username — renaming either parameter to match the other would make one of the two
   descriptions inaccurate. This needs a design decision (accept the pedantic non-conformance, or rename one
   side and document the accepted inaccuracy), not a one-line fix.
2. **Dangling `$ref`** (real bug): the `oneOf` response schema for two SSE routes —
   `GET /matches/{matchId}/live` and `GET /users/{idOrName}/live` — references `MatchLiveSnapshot` and
   `PlayerStatusView` respectively via `$ref`, but neither type has a schema actually registered in
   `components.schemas` (confirmed: each name appears exactly once in the whole document — the dangling
   `$ref` itself, nothing else). Root cause: both operations declare `.Produces<T>()` twice
   (`MatchRoutes.cs:210-211`, `UserRoutes.cs:344-345`), and only the *last*-declared type of the pair
   actually gets a registered component schema (`PlayerLiveScore` and `SpectateFramesEvent`, the second type
   in each pair, both register correctly). The hand-written `oneOf` builder
   (`OpenApiExampleExtensions.cs`) assumes both types it names already have schemas and just emits `$ref`
   strings by name — it should instead verify/force both schemas to exist. This one is a real, fixable code
   bug once someone has time to touch `OpenApiExampleExtensions.cs` and the two duplicate `.Produces<T>()`
   call sites.

Wording/description content in all 6 documents is clean against `CLAUDE.md`'s rule 5 (no implementation
details in `.WithSummary`/`.WithDescription`) and shows no AI-writing filler patterns.

## 7. Where to find things

- [`docs/for-developers/known-limitations.md`](../docs/for-developers/known-limitations.md) — the
  authoritative, versioned tracking doc for every open root cause, the dependency inventory, and the load
  test handoff instructions. Keep this updated as things close or reopen; this handoff document is a
  point-in-time summary, that file is the living one.
- [`docs/adr/`](../docs/adr/) — ADR-003/004/005/007 (ADR-001, 002, 006 were implementation-scoped and
  deleted once done, per an explicit project-owner decision that ADRs here are not permanent documents).
- [`docs/for-developers/working-scopes.md`](../docs/for-developers/working-scopes.md) — what Basil is and
  isn't scoped to do; check before adding anything bancho.py has that Basil doesn't.
- [`tests/Basil.LoadTests/`](../tests/Basil.LoadTests/) — the load-test harness. Run with:
  ```bash
  dotnet run --project tests/Basil.LoadTests -- --profile full
  ```
  (`--profile` takes a bare name, resolved to `Profiles/<name>.json`.) **Read the "Handoff" section of
  `known-limitations.md` first** — there are two known issues to check/fix before trusting the next run's
  `stress`/`soak` results and log capture.
- `CLAUDE.md` (repo root) — the full set of engineering rules this effort has followed throughout
  (surgical changes, evidence labels, test contract rules, API/XML doc conventions). Read it before making
  any change here.

## 8. Recommended immediate next step

Do not start RC8 (protocol/allocation optimization) or attempt the stress/soak run yet. Both are gated on
understanding why RC11 recurred. Start with:

1. Fix the `Logs/latest.log` size cap or its rotation (small, unblocks future debugging of exactly this
   kind of failure).
2. Audit why `full.json`'s `stress`/`soak` scenarios produced no variants.
3. Re-investigate RC11 at the `multiplayer → api` transition specifically, with log capture actually
   working this time, using the existing `BasilMetrics` DB/lock instrumentation.
