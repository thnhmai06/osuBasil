# ADR-003 — Match state persistence ordering

> Status: **Proposed — pending review.** No production code in this ADR's scope has been
> changed. Written as part of the 2026 performance/correctness overhaul
> (`chore/perf-investigation`), gated per that plan's rule 4 (decision gates need an approved
> ADR before code).

## Problem

Three call sites write match-round rows to SQLite while holding `MatchSession.Lock`, the
per-match semaphore that also serializes every other read-mutate-broadcast sequence on that
match (join, leave, ready, slot change, settings change, and so on):

- `MatchMembershipService.StartAsync` → `IMatchRepository.CreateRoundAsync` (round start)
- `MatchCompleteHandler.HandleAsync` → `IMatchRepository.SetRoundEndedAsync` (round end, normal completion)
- `MatchControlService.AbortAsync` → `IMatchRepository.SetRoundEndedAsync` (round end, `!mp abort`)

`Microsoft.Data.Sqlite` has no true async I/O (confirmed in Phase 0/1 of this investigation:
see the plan's Evidence section and RC1) — a write under contention blocks for up to
`busy_timeout` (currently 5s, set in `SqliteConnectionFactory`). Because these three writes run
under `MatchSession.Lock`, that same 5-second stall blocks every *other* operation on the same
match: a player trying to join, ready up, or change a slot in room A cannot proceed while room
A's round-start write is stuck behind SQLite contention, even though that write has nothing to
do with slot/settings state.

This ADR exists to answer two questions before any code moves these writes out from under the
lock:

1. What ordering/causality guarantees does the rest of the system actually rely on for these
   three writes, and for match events in general?
2. What mechanism preserves those guarantees once the write no longer runs synchronously inside
   the lock that used to make its ordering trivial?

## Evidence

- RC1 (Phase 0/1, this investigation): SQLite write path ceiling ~136 RPS before ADR-001's
  fixes, ~825 RPS after. Writes under sustained contention block the calling thread for up to
  `busy_timeout`.
- RC11 (Phase 0): a combined multiplayer+API load run drove sustained ThreadPool/queue growth
  that never recovered, root-caused to the same SQLite write-path saturation (RC1), observed
  under exactly the kind of mixed load where match-round writes and API writes compete for the
  same SQLite writer.
- `MatchSession.Lock` discipline is otherwise correct: 22/25 call sites (audited this session,
  see `d4158ec`) that acquire it do so for slot/settings/host mutations only, with no I/O inside
  the critical section. The 3 sites above are the exception.
- `MatchReportService` and the `GET /matches/{id}/report` read path reconstruct a match's
  results from `Round` and `Score` rows keyed by `RoundId`; a score submission
  (`ScoreSubmissionService`, HTTP POST, unordered relative to the bancho connection) is linked
  to `MatchSession.CurrentRoundId` at submission time (see `MatchCompleteHandler`'s remarks on
  why `CurrentRoundId` is deliberately left set after the round-end write completes).
- `MatchEvent` rows (`MatchCreated`, `PlayerJoined`, `HostGranted`, etc.) are the audit trail for
  tournament disputes; Phase 1 of this investigation (`0147004`) already converted every
  fire-and-forget `CreateEventAsync` call to `await`, so those writes are already ordered
  relative to the state transition that produced them — this ADR does not reopen that decision,
  only the round start/end writes.

## Constraints

- No silent behavior change (plan rule 2): a client reading match state, round history, or a
  score's linked round must observe the same causal relationships it does today.
- `Start` must be observably ordered before the `Finish`/`Abort` of the same round: nothing may
  ever record a round ending before it started.
- A `PlayerJoined` for a given userSession must never be observably reordered past that same
  userSession's own later `PlayerLeft` (or vice versa) in anything that reads match events back
  (the `!mp make`+`!mp start` and taskkill-reconnect repro tests from Phase 2 depend on this).
- The round-end write for the *same round* must not race the round-start write for the *next*
  round (`match.NextRoundIndex++` is single-threaded under the lock today; whatever replaces the
  in-lock write must not let two rounds' persistence interleave out of order).
- A round-end write that fails or times out must not silently disappear: it either succeeds, or
  is retried, or is surfaced as a known gap in the match report — never both attempted and lost
  without a trace.

## Alternatives

**A. Per-match ordered outbox, persisted outside the lock.**
Each `MatchSession` gets a bounded, single-consumer queue. The lock-held section enqueues a
small immutable record (round start/end fact) instead of awaiting the write; a single
background consumer per match (or one shared consumer that processes per-match queues
in-order) drains it and performs the actual `CreateRoundAsync`/`SetRoundEndedAsync` call.
Ordering is free (FIFO per match); the lock is held only long enough to enqueue.
Trade-off: needs an explicit answer for what happens when the queue is full (backpressure into
the lock-held caller, defeating the purpose) or when the consumer is behind at match teardown
(must drain before the match's in-memory state is discarded, or the last round's end never
persists).

**B. Fire back into the existing SQLite write path, but off the match lock, with an explicit
per-match sequence number.**
The lock-held section synchronously assigns a monotonic per-match sequence number (already
available via `NextRoundIndex`) and starts the write as a detached, awaited-from-outside-the-lock
task; the repository layer enforces ordering by sequence number at the SQL level (e.g. a
`CHECK` or an `INSERT ... WHERE NOT EXISTS (SELECT 1 FROM Rounds WHERE MatchId=? AND Ended IS
NULL)` guard). Simpler than a full outbox, but pushes ordering enforcement into SQL and doesn't
generalize past these two calls.

**C. Leave writes under the lock, shrink `busy_timeout` instead.**
Rejected outright: this trades one bad failure mode (long lock hold) for another (write
failures under load), without removing the root cause (SQLite's serialized writer). Doesn't
answer this ADR's actual question.

## Decision

**Not yet made.** This ADR is submitted for review before any implementation. The author's
recommendation, given the constraints above, is Option A (per-match ordered outbox) — it gives
FIFO ordering for free per match, keeps the durability failure mode explicit (a full queue is a
visible signal, not a silent drop), and matches the "smallest change that satisfies the
guarantees" bar without pushing ordering logic into SQL (Option B) or leaving the root cause
unaddressed (Option C). This is a recommendation, not a decision — reviewer to confirm or
redirect before code changes.

## Trade-offs

(To be finalized alongside the Decision.) For Option A: added moving parts (one queue +
consumer per match, or one shared consumer keyed by match id) versus today's zero added
infrastructure; a match-teardown path that must drain its queue before discarding in-memory
state, adding one more sequencing rule to `TeardownMatch`/`CloseAsync`.

## Measurements

Not yet run. Once a decision is made, verify with:
- The existing multiplayer load scenario (4/16/32/64 rooms, `Profiles/full.json`) — match-lock
  wait time (`BasilMetrics.MatchLockWaitMs`, already instrumented) should drop measurably for
  rooms whose round-start/end coincides with SQLite write contention.
- A repro test asserting round start/end ordering survives concurrent load (many matches
  starting/ending rounds simultaneously against a saturated SQLite writer).
- `crash-repro-sequence.json` (from the RC11 investigation) rerun to confirm no regression in
  the ThreadPool-saturation behavior this ADR is partly meant to relieve.
