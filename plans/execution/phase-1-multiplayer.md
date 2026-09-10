# Phase 1 — Multiplayer

Largest phase in the plan: 11 tasks. It settles the patterns Phases 2-4 follow, so a decision made
sloppily here is repeated three more times.

Plan section: `plans/vsa-migration-plan-20260907.md`, Tasks 1.1 through 1.11.

## What Phase 1 inherits from Phase 0

* **The event hub is built but unadopted.** `Shared/Eventing` holds `ILiveEventHub`, `LiveEventHub`,
  `LiveSubscription`, `StreamKey`, `StateStream`, `SequenceGate`, `SseSubscriberRegistry`,
  `BoundedSseChannel` and `SseEndpoints`. `IMatchLiveEvents`/`MatchLiveEvents` still exist and still
  carry every publish. Two parallel eventing mechanisms live side by side until Task 1.3 retires the
  old one.
* **Task 0.10's advisor review flagged a Phase 1 hazard:** a stale-subscriber publish race can hand a
  subscriber a delta instead of a full snapshot. Task 1.3 must read that entry in
  `plans/execution/phase-0-foundation.md` before adopting the hub.
* **`Shared.Sessions.*` reaching into `Multiplayer`** is the largest remaining cluster pinned by
  `Shared_Should_Not_Reference_Features`, and Task 1.4 is where it gets unwound. Removing an entry
  from that pinned list requires editing the list: the test asserts exact set equality.

## Task status

| Task | Status | Owner | Notes |
| --- | --- | --- | --- |
| 1.1 Mutation invariant audit | Done | orchestrator + worker | 40 class A, 7 class B all from one root cause; fixed at source instead of adding `Invalidate()` |
| 1.2 `MatchMutationScope` (plan B4) | **Done** | orchestrator + worker | 48 lock sites (`02dab44f`) and all 25 publish sites converted; `NextStateVersion` deleted; verified green at 1654 |
| 1.3 Adopt the hub (plan B5) | Not started | — | design decided in `plans/execution/hub-adoption-decision.md`: deltas only, and the seed handshake is deleted because `SeedIfNotSuperseded` has no callers |
| 1.4 `MatchSession` encapsulation | Not started | — | unwinds the `Shared.Sessions.*` pin |
| 1.5 Decompose `MatchControlService` (plan B6) | **Done** | worker + orchestrator | 1,483 lines to 906 across three handler groups; invariant bug fixed at `b4a95cc4` |
| 1.6 Decompose `MatchMembershipService` (plan B6) | **Done** | worker | split three ways at `0204b503`; invariant bug fixed at `b4a95cc4` |
| 1.7 Decompose `MatchSubResourceRoutes` (plan B1) | **Done** | worker | 1,335 lines to 31 across thirteen endpoint files |
| 1.8 Localize the slice, own `!mp` help | Not started | — | |
| 1.9 Logging pass and test triage | Not started | — | |
| 1.10 Advisor checkpoint | Not started | — | |
| 1.11 Re-run the file-overlap check | Not started | — | gates the Phase 2-4 fan-out |

## Task 1.1 — measured facts so far

`grep -rn "Lock.WaitAsync" src/Basil.Server --include=*.cs` returns **50** hits, but three of them are
unrelated locks and are out of scope for this audit:

* `Shared/Http/BanchoHostGroups.cs:233` — `previewLock`
* `Features/Scores/ScoreSubmissionService.cs:206` — `checksumLock`
* `Features/Beatmaps/BeatmapsetAssetCache.cs:52` — `extractLock`

That leaves **47 real `MatchSession.Lock` acquisitions**, distributed as:

| File | Sites |
| --- | --- |
| `Features/Multiplayer/MatchSubResourceRoutes.cs` | 16 |
| `Features/Multiplayer/MatchMembershipService.cs` | 5 |
| `Features/Multiplayer/MatchRoutes.cs` | 3 |
| `Features/Multiplayer/Packets/*.cs` | 20 (one each) |
| `Features/Bot/MpCommandService.cs` | 2 |
| `Features/Multiplayer/MatchControlService.cs` | 1 |
| `Shared/Sessions/PlayerLogoutService.cs` | 1 |

`MpCommandService.cs:488` acquires the lock with **no cancellation token** (`await match.Lock.WaitAsync()`),
unlike every other site. Worth a look during the audit rather than assuming it is deliberate.

## The invariants the audit tests against

From the plan, stated against the real model in `Features/Multiplayer/MatchSession.cs`:

1. A slot has a player if and only if its status is not `Open`.
2. `HostId` is either `NoHostId` (which is `SystemUserIds.BasilBot`) or the player id of an occupied slot.
3. `InProgress` implies at least one non-`Open` slot.

## Task 1.3's design question, already analysed

The Task 0.10 hazard resolves to a concrete mechanism, read out of the code rather than inferred:

* `StateStream.Publish` returns a **merge-patch delta**, and only returns a full state on the very
  first publish, when there is nothing to diff against.
* `LiveSubscription.OnPublish`'s `!_hasSnapshot` branch **adopts that payload as the subscription's
  snapshot** and sets `_hasSnapshot = true`.
* `SeedIfNotSuperseded` returns `false` once `_version > fence`.

So a subscriber that opens while the stream is stale, and has a publish land before its seed
arrives, takes a **delta as its first item** -- violating the SSE contract's guarantee that the
first item is a full snapshot. Because `OnPublish` has already set `_hasSnapshot`, a retrying seeder
cannot repair it either: that path is closed permanently.

Two candidate fixes, with their costs:

1. **Hand the hub both payloads** (`full` + `delta`). The hub stays dumb -- it moves two byte arrays
   and understands neither -- but every publish must serialize the full state, which is exactly the
   unconditional-snapshot-build cost the performance investigation flagged.
2. **Stop `OnPublish` adopting a delta as a snapshot.** Leave `_hasSnapshot` false, and let the
   seeder retry against `StateStream.Latest`, which is always the newest full state. Full
   serialization then happens only when a subscriber is actually seeding.

Option 2 is cheaper and keeps the hub a loudspeaker. Not decided yet -- decide it while doing Task
1.3, against the real call sites.


## Stage B progress

| Task | State |
| --- | --- |
| B1 split `MatchSubResourceRoutes` | **Done.** 1,335 lines to 31; eight endpoint files plus five more the split surfaced. |
| B2 split `MatchRoutes` | **Done.** 611 lines to 27. |
| B3 database boundary | Closed — the finding behind it was a measurement error. |
| B4 `MatchMutationScope` | **Done.** Every lock site and every publish site; `NextStateVersion` deleted. |
| B5 adopt the event hub | Not started. Carries a design decision recorded below. |
| B6 decompose the two services | **Done.** `MatchMembershipService` split three ways (`0204b503`). `MatchControlService` 1,483 lines to 906: slots (`52b49d0f`), countdown (`4d669be8`), lifecycle (`c2e4be34`). Both invariant bugs fixed at `b4a95cc4`. Route table re-verified byte-identical at 138 entries. |

**The route table was verified byte-identical after B1 and B2**, 138 entries against
`plans/execution/baseline/routes.txt`. The baseline is derived from source, so it can be re-derived
without running the server:

```bash
grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Server --include=*.cs | sort -u
```

Note the `-u`. The baseline is deduplicated, and several hosts each map `/` and `/health`, so a
plain `sort` reports seven phantom additions.

## Task B4 — where it stands

**Done:** the scope is built and correct, and all forty-eight match-lock acquisitions go through
it. The only `Lock.WaitAsync` left in the server is inside `MatchSession` itself. Converting them
also fixed six places that allocated a state version with the lock *not* held — the countdown loop
allocated one per announce tick with no lock at all — and several that awaited a broadcast while
still holding it.

**Also done.** `MatchControlService`'s 24 publish sites and `MatchMembershipService.StartAsync`
take a `MatchMutationScope` and request their publishes through it, so nothing allocates a version
by hand any more and `MatchSession.NextStateVersion()` is gone — zero occurrences remain in `src/`.

That work was finished by a worker that then died on a session limit before it could commit, and
the orchestrator swept its eight files into a documentation commit (`ba3de03a`) with `git add -A`.
The code is correct and verified — build green, 1654 passed, 0 failed — but a reader of that commit
message would never guess it contains the conversion. Recorded here because the history does not
say so.

## The trap in it

Re-verified 2026-09-10: `grep -rn "Lock.WaitAsync" src/Basil.Server --include=*.cs` returns **4**,
of which three are unrelated locks (`extractLock`, `checksumLock`, `previewLock`) and the fourth is
inside `MatchSession` itself, where the scope acquires it. `NextStateVersion` has zero occurrences
in `src/`. B4 is finished; an earlier revision of this file said the conversion had not started,
which was true when written and stopped being true two commits later.

The bug that cost three worker sessions is worth knowing before touching this code again.
`BeginMutationAsync` was an `async` method that set an `AsyncLocal<bool>` after awaiting the lock.
An `AsyncLocal` assignment made inside an async method belongs to that method's execution context
and is discarded when it returns, so the caller never saw it, the nesting guard never fired, and the
second `BeginMutationAsync` on the same flow waited on a lock its own caller held. Every test run
hung instead of failing.

Two consequences that generalise:

* **Check and mark synchronously, before any await**, when a marker has to be visible to the caller.
  Awaiting moves into a private method. The marker is a box, not a bool, so that clearing it from
  inside an async method mutates the object the caller already holds.
* **A test that pins "throws rather than waits" must carry a timeout.** Both such tests here were
  written to hang on regression. A hung suite is much harder to diagnose than a failed assertion,
  and it is exactly why this stayed invisible.

## How this phase died, twice

Both interruptions are worth reading before resuming, because they were the same mistake at
different scales.

A worker finished B4's call-site conversion and died on a session limit before committing. The
orchestrator swept its eight files into a documentation commit with `git add -A`, so the code is
correct and verified but the history does not say what it contains.

A worker was killed at 02:40 on 2026-09-10 having removed `StartAsync`, `AbortAsync` and their
result enums from `MatchControlService` without repointing a single caller. The tree did not
compile. Its `git status` was indistinguishable from the diagnostics tree's, which was green; only a
build told them apart.

Both are the same rule unlearned: **commit the moment a task is green, and treat "a member is
removed but not yet rewired" as a broken tree rather than a work in progress.** A three-handler
group is three tasks, not one.

## Order-dependent tests, and the one still open

Two tests pass alone and fail inside their project's full run. That shape is worse than a failing
test, because it teaches a reader to re-run rather than to look, and it is how a real regression gets
waved through as "the usual flake".

**Fixed.** `CommandLineTests` captured output by redirecting `Console.Out`, which is process-global,
so anything another test printed in the window landed in its buffer. `CommandLine.TryRunAsync` now
takes a writer and the test owns it (`1c29ce45`).

**Still open.** `BeatmapDifficultyEndpointTests.GetDifficulty_PrivateBeatmapsetWithoutAdminKey_ReturnsNotFound`
fails in the full `Basil.IntegrationTests` run and passes with its class alone. This is the class the
phase 0 checkpoint calls the "Windows file-handle flake" candidate. It has not been diagnosed, only
observed, and it should be diagnosed rather than tolerated — the `Console.SetOut` one looked
identical from the outside and turned out to be a straightforward defect.

Not the same thing as `StartupUpdateCheck.StopAsync` throwing `ObjectDisposedException` on a second
stop, which was a real bug in the update system and is fixed at `dcc63374`. A worker had attributed
that to this same phase 0 flake note.

## Task B5 — where it stands

Started 2026-09-10 10:20. **Three commits in, one piece left.** The tree compiles at every point
below; verify with a build before trusting that, since a previous worker here left it not compiling.

| Commit | What landed |
|---|---|
| `16e02489` | `StateStream` returns `(latest, version)` read together under the lock `Publish` takes, and a subscriber fences hub events against its own state version |
| `d48dca61` | the seed handshake deleted — `MarkStale`, `IsStale`, `SeedIfNotSuperseded`, `_hasSnapshot`, and the two `LiveEventHubTests` cases that pinned the seed race |
| `8f035ae8` | the match state, score and chat streams moved onto the hub |

**Left to do: retire `IMatchLiveEvents` and `MatchLiveEvents`.** This is the second of the two
parallel eventing mechanisms that have coexisted since Phase 0, and removing it is what closes B5.
Fifteen files are modified and uncommitted, mid-retirement:

* `Features/Multiplayer/MatchLifecycle.cs`, `MatchLiveSnapshotBuilder.cs`,
  `MultiplayerServiceCollectionExtensions.cs`
* `Features/Irc/IIrcConnection.cs`, `Features/Spectating/IPlayerInputEvents.cs`,
  `PlayerInputEvents.cs`
* nine test files, chiefly `MultiplayerTestSupport.cs`, which every multiplayer test builds on

Still referencing the old mechanism: `Features/Multiplayer/MatchLiveEvents.cs`,
`Shared/Eventing/IMatchLiveEvents.cs`, `MatchLiveEventsTests.cs`, `LiveSseEndpointTests.cs`,
`TcpIrcConnectionTests.cs`, `MatchMembershipServiceTests.cs`, `MultiplayerTestSupport.cs`.
`mcp__rider__safe_delete` on the interface is the way to find anything this list misses.

**The design is settled** in `plans/execution/hub-adoption-decision.md`. Do not re-derive it, and
note that this file's own "Task 1.3's design question" section below weighs two candidates the
decision document rejects in favour of a third. The decision document wins.

**Still owed:** the invariant test the decision document names — *a subscriber's first item is the
state at version N, and every later item has a version strictly greater than N*. That is what
replaces the two deleted `LiveEventHubTests` cases, and it is the reason deleting them was safe, so
B5 is not finished until it exists. Bound it with a `CancellationTokenSource` of a few seconds: two
tests on this project were written to hang on regression instead of failing, and a hung suite is far
harder to diagnose.

## Next exact step

Finish retiring `IMatchLiveEvents`, add the invariant test, and close B5 — which closes Stage B.

Then **B5, adopt the event hub.** The design question this file records under "Task 1.3's design
question" is already decided, and decided differently than either option sketched here: see
`plans/execution/hub-adoption-decision.md`. The hub carries deltas only, and the seed handshake is
deleted rather than fixed, because `SeedIfNotSuperseded` turned out to have no callers. Do not
re-derive that; the analysis below is kept for its reasoning, not as an open question.
