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
| 1.3 Adopt the hub | Not started | — | read the Task 0.10 hazard first |
| 1.4 `MatchSession` encapsulation | Not started | — | unwinds the `Shared.Sessions.*` pin |
| 1.5 Decompose `MatchControlService` | Not started | — | 1303 lines, 43 members; **also owns audit Observation 1** — `SetHostAsync` and `PUT /matches/{id}/hosts` never check the target is seated |
| 1.6 Decompose `MatchMembershipService` | Not started | — | 902 lines, 27 members; **also owns audit Observation 5** — `StartAsync` can set `InProgress` with zero occupied slots |
| 1.7 Decompose `MatchSubResourceRoutes` | Not started | — | 1423 lines, 33 members |
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
| B6 decompose the two services | In progress with a worker; owns two invariant bugs from the audit. |

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

The scope itself is built, correct and committed at `9357b56`. **The call-site conversion has not
started**; `grep -rn "Lock.WaitAsync" src/Basil.Server --include=*.cs` still returns 51, of which 3
are unrelated locks.

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

## Next exact step

Task 1.1's enumeration is delegated; its classification and the design decision that follows are the
orchestrator's. When the enumeration lands, verify every class-B claim by reading that site directly
(and spot-check the class-A claims) before accepting it — a worker reported a total as "verified and
unchanged" in Task 0.6 when it was not.

Then decide:

* **Class B empty** — the design stands; record the finding with evidence and go to Task 1.2 unchanged.
* **Class B non-empty** — `MatchMutationScope` gains an explicit `Invalidate()` that those specific
  sequences call to suppress the publish and mark the stream stale instead. Suppression must not
  become the default: that reintroduces the silently-stale-subscriber problem for the far more
  common class A.
