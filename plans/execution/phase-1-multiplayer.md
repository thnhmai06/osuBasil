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
| 1.3 Adopt the hub (plan B5) | **Done** | worker | `IMatchLiveEvents`/`MatchLiveEvents` retired; design in `plans/execution/hub-adoption-decision.md`: deltas only, seed handshake deleted because `SeedIfNotSuperseded` had no callers |
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
| B5 adopt the event hub | **Done.** `IMatchLiveEvents`/`MatchLiveEvents` retired; see below. |
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

**Done**, closing Stage B. The tree compiles and every test project is green (arithmetic below);
verify with a build before trusting that, since more than one previous worker here left it not
compiling.

| Commit | What landed |
|---|---|
| `16e02489` | `StateStream` returns `(latest, version)` read together under the lock `Publish` takes, and a subscriber fences hub events against its own state version |
| `d48dca61` | the seed handshake deleted — `MarkStale`, `IsStale`, `SeedIfNotSuperseded`, `_hasSnapshot`, and the two `LiveEventHubTests` cases that pinned the seed race. **The invariant test also landed in this same commit**, `LiveEventHubTests.SubscriberOpensAtStateVersionAndEveryLaterItemIsStrictlyNewer` — see the correction below. |
| `8f035ae8` | the match state, score and chat streams moved onto the hub |
| (this session) | `IMatchLiveEvents`/`MatchLiveEvents` retired outright — the second of the two parallel eventing mechanisms that coexisted since Phase 0. See below for what that touched. |

**Correction to a stale note this file used to carry:** an earlier revision of this section said the
invariant test — *a subscriber's first item is the state at version N, and every later item has a
version strictly greater than N* — was still owed. It was not: `git show d48dca61` shows the two
`LiveEventHubTests` seed-race cases coming out and `SubscriberOpensAtStateVersionAndEveryLaterItemIsStrictlyNewer`
going in in that same commit, already bounded with a five-second `CancellationTokenSource` per the
decision document's requirement. Verified passing on its own (`dotnet test tests/Basil.Server.Tests
--no-build --filter "FullyQualifiedName~LiveEventHubTests"` → 2/2) before touching anything else this
session. Do not re-add it; if a future reader thinks it's missing, run that filter first.

### What retiring `IMatchLiveEvents`/`MatchLiveEvents` touched

Production: `Features/Multiplayer/MatchLiveEvents.cs` and `Shared/Eventing/IMatchLiveEvents.cs` are
deleted outright (both files were left with nothing but a stray `using` once
`mcp__rider__safe_delete` removed the class and interface, so the files themselves went too).
`MatchLifecycle.cs`, `MatchLiveSnapshotBuilder.cs`, `MultiplayerServiceCollectionExtensions.cs`,
`IIrcConnection.cs`, `IPlayerInputEvents.cs` and `PlayerInputEvents.cs` were already migrated to
`ILiveEventHub` by the prior session; those diffs needed no further work.

Tests: deleted `MatchLiveEventsTests.cs` outright (10 `[Fact]` cases, no `[Theory]`s — its subject no
longer exists) and `FakeMatchLiveEvents`/`Fixture.EventBus` from `MultiplayerTestSupport.cs`,
`NoOpMatchLiveEvents` from `TcpIrcConnectionTests.cs`, and `_eventBus`/the `eventBus` parameter from
`MatchMembershipServiceTests.cs`, all via `mcp__rider__safe_delete` (each previewed clean — zero
conflicts — once the call sites below stopped referencing them). `LiveSseEndpointTests.cs` needed
only a doc-comment fix (it already tests `ILiveEventHub` directly, the `IMatchLiveEvents` mention was
just stale prose).

**What `safe_delete` surfaced that this file's list missed:** three call sites were passing a bare
`null` for `MatchLifecycle`'s `hub` parameter instead of a real `ILiveEventHub` — a half-migrated
state the prior session left mid-flight. `MultiplayerTestSupport.cs` and `MatchMembershipServiceTests.cs`
already had an unused `Hub`/`_hub` field sitting right there; wiring it in was enough, but it turned
`MatchMembershipServiceTests.Leave_LastPlayer_StartsEmptyRoomTimerInsteadOfImmediateTeardown` and
`CloseAsync_CompletesEverySseSubscriberRegisteredOnTheMatch` red first (`hub.Forget` on a null hub —
confirmed with `dotnet test ... --filter "FullyQualifiedName~MatchMembershipServiceTests"` before
touching anything, 2 failed / 22 passed). The third site, `TcpIrcConnectionTests.cs`, keeps `null!`
with a one-line comment — that constructor path is logout-only and never reaches `TeardownMatch`.

A fourth, `EmptyRoomAutoCloseTests.cs`, wasn't on the checkpoint's list at all: deleting
`IMatchLiveEvents` left its `Substitute.For<IMatchLiveEvents>()` call site referring to a type that no
longer existed, and it surfaced as a null argument once the interface was gone. Fixed the same way —
added a real `_hub` field, wired into both `MatchBroadcast` and `MatchLifecycle` — and it now passes
(197/197 across the Multiplayer + `TcpIrcConnectionTests` filter, up from 196/197 red).

`MatchMembershipServiceTests`'s teardown assertion used to read `_eventBus.Forgotten` (the fake's own
recorded call log). With the fake gone, it now pins the same observable behaviour a different way:
open a subscription on the match's `main` stream, assert `_hub.HasSubscribers` is true, run
`CloseAsync`, assert it's false. Same contract (teardown drops the hub's per-match bookkeeping),
verified through the real `LiveEventHub` instead of a recorded call.

`MatchLiveEvents.cs`'s private nested `PerMatchHub<T>` was not shared with anything else in the
codebase (confirmed by grep before deleting the file) — it went with the rest of the file.

## Next exact step

Stage B is done — B1 through B6 all landed. The next task in the phase table is **Task 1.4,
`MatchSession` encapsulation**, which unwinds the `Shared.Sessions.*` reaching into `Multiplayer`
that `Shared_Should_Not_Reference_Features` currently pins as an exact-set-equality allowlist. Read
that pinned list and the "What Phase 1 inherits from Phase 0" section at the top of this file before
starting — removing an entry means editing the list, not just the code.

Test arithmetic verified this session, run per-project with `--no-build` (`Basil.IntegrationTests`
last): ArchitectureTests 6/6, Domain.Tests 114/114, Protocol.Tests 158/158, Server.Tests 1052/1052,
IntegrationTests 362/363 (the one known-unrelated `BeatmapDifficultyEndpointTests` flake, not touched
here). Route table: 140 literal patterns, unchanged by B5 (it touches no routes).
