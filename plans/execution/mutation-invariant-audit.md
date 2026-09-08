# Mutation invariant audit

Task 1.1 of `plans/vsa-migration-plan-20260907.md`. Enumerates all 47 real `MatchSession.Lock`
acquisitions (the three non-match locks — `previewLock`, `checksumLock`, `extractLock` — are out of
scope per `plans/execution/phase-1-multiplayer.md`) and classifies each against the three invariants:

1. A slot has a player if and only if its status is not `Open`.
2. `HostId` is either `NoHostId` (`SystemUserIds.BasilBot`) or the player id of an occupied slot.
3. `InProgress` implies at least one non-`Open` slot.

**Facts and classification only.** No design proposal, no `MatchMutationScope` code. "Fallible call"
follows the task's own definition: an awaited I/O/repo call, a broadcast, an explicit throw, an
indexer/`First()` that can throw, or a cast — named precisely regardless of how likely it is to
actually throw in practice, because the point is what a throw *there* would leave behind, not the
probability of it happening.

## Shared mutation sequences

Several lock sites don't mutate `MatchSession` directly — they delegate to one of three shared
sequences in `MatchMembershipService.cs`. Each is analyzed once here; every call site below just
references the verdict.

### `OccupySlot` (lines 315-362) — used by `JoinAsync` and `ForceJoinAsync`

Writes, in order: `slot.Team` (only for `TeamVs`/`TagTeamVs`, conditional) → `slot.Status = NotReady`
→ `slot.PlayerId = userSession.Id` → `match.HostId = userSession.Id` (only `if
(!match.HasGameplayHost)`). The only fallible calls in the method — `channelMembership.Join` (line
324, a broadcast) and `channelMembership.Part` for the lobby (line 327, a broadcast) — both run
**before** the first of these writes. The one remaining fallible call, `await
matchRepository.CreateEventAsync(...)` (line 356), runs **after** the last one.

**Class A.** A throw at either fallible point either leaves no match field touched at all, or leaves
slot/HostId already in a fully valid, mutually consistent state (occupied slot ⇒ non-`Open` status;
HostId, when set, points at the very slot just occupied). No exception window exposes a partial write.

### `LeaveAsync` (lines 373-440) — used by every kick/ban/leave path

```
slot.Reset(...)                                   // line 383 — WRITE 1 (slot → empty)
channel = channelRegistry.GetByName(...)
if (channel is not null) channelMembership.Part(userSession, channel)   // line 386 — BROADCAST
if (userSession.Id == match.HostId)
    ... match.HostId = newHostId.Value / NoHostId  // lines 399/405 — WRITE 2 (host transfer)
SyncEmptyRoomTimer(match)
...
await matchRepository.CreateEventAsync(...)        // after all writes
```

**Class B.** `channelMembership.Part` (line 386) is a broadcast per the task's own definition, and it
runs strictly between WRITE 1 and WRITE 2. If the leaving user *is* the host and that call throws,
execution never reaches the host-transfer block: the slot has already been reset to empty (satisfies
invariant 1 on its own), but `match.HostId` still names a user who now occupies no slot at all —
**violates invariant 2**. In the non-throwing path this never surfaces, but the ordering itself is
what the task asks to flag: a real fallible call sits between two writes whose combination the
invariant depends on. Every call site that invokes `LeaveAsync` while holding the lock inherits this
finding; it is the single root cause behind every Class B row below.

### `MatchMembershipService.StartAsync` (lines 547-599) — used by every `!mp start`/timer-expiry path

Writes: `slot.Status = Playing` (loop, occupied non-NoMap slots) → `match.InProgress = true` (line
585) → `match.CurrentRoundId = await matchRepository.CreateRoundAsync(...)` (line 587, fallible,
**after** `InProgress = true`). If `CreateRoundAsync` throws, `InProgress` is already `true` and
`CurrentRoundId` stays whatever it was before the call.

**Class A, with a caveat.** Invariant 3 only survives this ordering because starting a match
presupposes at least one already-occupied slot; that occupied slot is non-`Open` independent of
whether it made it to `Playing`, so the throw itself adds no new violation. See Observations for why
this precondition is not actually enforced by the method and can be violated even without any
exception (an unrelated, pre-existing gap, out of this audit's throw-focused scope).

## Packets/*.cs — 19 sites, one packet each

| File:line | Method | Fields written (order) | Fallible call between first/last write | Class |
| --- | --- | --- | --- | --- |
| `JoinMatchHandler.cs:55` | `HandleAsync` | delegates entirely to `matchMembership.JoinAsync` → `OccupySlot` | see OccupySlot | A |
| `MatchChangeModsHandler.cs:33` | `HandleAsync` | `match.Mods` or `slot.Mods` (freemod branch: both) | none | A — plain property sets, no await between them |
| `MatchChangePasswordHandler.cs:30` | `HandleAsync` | `match.Password` | none | A — single field |
| `MatchChangeSettingsHandler.cs:47` | `HandleAsync` | `match.Freemods`, `slot.Mods`(loop), `match.Mods`, `match.UnreadyPlayers()`, `match.PrevMapId`, `match.MapId`, `match.MapMd5`, `match.MapName`, `match.UnresolvedMapMd5`, `match.Mode`, `slot.Team`(loop), `match.TeamType`, `match.WinCondition`, `match.Name` | `await beatmapRepository.FetchOneAsync(...)` (only when resolving an unset map) sits between some of these writes | A — none of these fields participate in the 3 invariants (occupancy, HostId, InProgress); a throw anywhere leaves a real, partially-applied settings state that violates none of them |
| `MatchChangeSlotHandler.cs:32` | `HandleAsync` | `match.Slots[slotId]` via `CopyFrom(slot)` (multi-field), then `slot.Reset()` (multi-field) | none — both calls are synchronous, consecutive, no awaits | A |
| `MatchChangeTeamHandler.cs:29` | `HandleAsync` | `slot.Team` | none | A — single field |
| `MatchCompleteHandler.cs:48` | `HandleAsync` | `slot.Status = Complete`, `match.UnreadyPlayers(Complete)`, `match.ResetPlayersLoadedStatus()`, `match.InProgress = false` | `roundEndOutbox.Enqueue(...)` (already caught for `MatchRoundEndOutboxFullException`; any other exception is still possible) — but it runs **after** all four writes above | A — all invariant-relevant writes complete before the only fallible point; `InProgress = false` never needs a companion write to stay valid |
| `MatchFailedHandler.cs:27` | `HandleAsync` | none (relay only) | n/a | A |
| `MatchHasBeatmapHandler.cs:28` | `HandleAsync` | `slot.Status = NotReady` | none | A — single field |
| `MatchLoadCompleteHandler.cs:29` | `HandleAsync` | `slot.Loaded = true` | none | A — single field |
| `MatchLockHandler.cs:31` | `HandleAsync` | `slot.Status` (Open or Locked, one branch) | none | A — single field |
| `MatchNoBeatmapHandler.cs:28` | `HandleAsync` | `slot.Status = NoMap` | none | A — single field |
| `MatchNotReadyHandler.cs:27` | `HandleAsync` | `slot.Status = NotReady` | none | A — single field |
| `MatchReadyHandler.cs:27` | `HandleAsync` | `slot.Status = Ready` | none | A — single field |
| `MatchScoreUpdateHandler.cs:39` | `HandleAsync` | none (relay + optional live-score publish) | n/a | A |
| `MatchSkipRequestHandler.cs:30` | `HandleAsync` | `slot.Skipped = true` | none | A — single field |
| `MatchStartHandler.cs:28` | `HandleAsync` | delegates entirely to `matchMembership.StartAsync` | see StartAsync caveat | A |
| `MatchTransferHostHandler.cs:37` | `HandleAsync` | `match.HostId = targetId.Value` | `await matchRepository.CreateEventAsync(...)` — runs **after** the only write | A — target is read from `match.Slots[slotId].PlayerId` and verified non-null before the write, so HostId always lands on an occupied slot's player |
| `PartMatchHandler.cs:28` | `HandleAsync` | delegates entirely to `matchMembership.LeaveAsync` | see LeaveAsync | **B** |

## `Features/Multiplayer/MatchMembershipService.cs` — 5 sites

| File:line | Method | Fields written | Fallible call between | Class |
| --- | --- | --- | --- | --- |
| `MatchMembershipService.cs:124` | `CreateAsync` (GameSession creator branch) | delegates to `JoinAsync` → `OccupySlot`, then conditionally `SyncEmptyRoomTimer` (EmptyRoomTimer/EmptyRoomWarningSent) | see OccupySlot; `SyncEmptyRoomTimer` itself has no fallible call between its own writes | A |
| `MatchMembershipService.cs:153` | `CreateAsync` (non-GameSession creator branch) | `SyncEmptyRoomTimer` only (EmptyRoomTimer, EmptyRoomWarningSent) | none | A — non-invariant fields, fully synchronous |
| `MatchMembershipService.cs:199` | `CreateEmptyAsync` | `SyncEmptyRoomTimer` only | none | A |
| `MatchMembershipService.cs:786` | `EmptyRoomCloseLoopAsync` (60s-warning tick) | `match.EmptyRoomWarningSent = true` | none (broadcast `AnnounceToRoomAndReferees` runs after) | A — single field |
| `MatchMembershipService.cs:801` | `EmptyRoomCloseLoopAsync` (close tick) | `match.EmptyRoomTimer = null`, `match.EmptyRoomWarningSent = false`, then delegates to `CloseAsync`→`TeardownMatch` | `AnnounceToRoomAndReferees` runs **before** both writes (not between them); `CloseAsync`/`TeardownMatch` writes only `PendingTimer`/`EmptyRoomTimer` (non-invariant) before its own fallible `roundEndOutbox.DrainAsync` | A — no invariant-relevant field is ever written in the close path (slots/HostId/InProgress are left untouched; the match is simply removed from the registry) |

## `Features/Multiplayer/MatchControlService.cs` — 1 site

| File:line | Method | Fields written | Fallible call between | Class |
| --- | --- | --- | --- | --- |
| `MatchControlService.cs:829` | `CountdownLoopAsync` | `match.PendingTimer = null`, `PendingTimerIsAutoStart = false`, `TimerStartedAt = null`, `TimerTotalSeconds = null` (all non-invariant), then conditionally delegates to `matchMembership.StartAsync` | see StartAsync caveat | A |

## `Features/Multiplayer/MatchRoutes.cs` — 3 sites

All three chain multiple `MatchControlService` setting calls (`SetPrivateAsync`, `SetSizeAsync`,
`SetMapAsync`, `SetModsAsync`, `SetTeamTypeWinConditionAndSizeAsync`, `SetNameAsync`,
`SetPasswordAsync`, the static `SetLocked`). None of these ever touch slot occupancy, `HostId`, or
`InProgress` — they only write `Name`, `Password`, `IsPrivate`, `IsLocked`, empty-slot `Status`
(`ApplySize` skips every occupied slot), `MapId`/`MapMd5`/`MapName`/`Mode`, `Mods`/`Freemods`/slot
`Mods`, `TeamType`/`WinCondition`/slot `Team`. Each individual call is independently Class A (per its
own analysis above/inline), and a throw partway through the chain just leaves some settings applied
and others not — a real, invariant-respecting state, since none of the touched fields are invariant
inputs.

| File:line | Method | Fields written | Fallible call between | Class |
| --- | --- | --- | --- | --- |
| `MatchRoutes.cs:308` | `HandleCreate` | Name/Password/IsPrivate/IsLocked/Size/Map/Mods/TeamType/WinCondition (settings only, via chained `MatchControlService` calls) | multiple (`SetMapAsync`'s beatmap lookup, several `EnqueueStateAsync` broadcasts inside each call) | A |
| `MatchRoutes.cs:396` | `HandleSettingsReplace` | same set as above (PUT, full replace) | same shape | A |
| `MatchRoutes.cs:430` | `HandleSettingsUpdate` | same set, PATCH-conditional (`ApplySettingsAsync`, lines 480-508) | same shape | A |

## `Features/Multiplayer/MatchSubResourceRoutes.cs` — 16 sites

| File:line | Method / delegate | Fields written | Fallible call between | Class |
| --- | --- | --- | --- | --- |
| `MatchSubResourceRoutes.cs:231` | `PUT /hosts` → `SetHostAsync` | `match.HostId = target.Id` (single field) | `await matchRepository.CreateEventAsync(...)` after the only write | A — single-field write; see Observations for a *non-exception* validation gap on this same path |
| `MatchSubResourceRoutes.cs:268` | `DELETE /hosts` → `ClearHostAsync` | `match.HostId = NoHostId` | none before the write; publishes after | A |
| `MatchSubResourceRoutes.cs:367` | `PUT /refs` → `SetRefereesAsync` | `_referees` add/remove only (not one of the 3 invariants), interleaved with per-id `CreateEventAsync` calls | yes, multiple, but only around Referees membership | A — Referees is independent of occupancy/HostId/InProgress; a throw mid-loop just leaves a partially-applied referee list |
| `MatchSubResourceRoutes.cs:420` | `PATCH /refs` → `AddRefereeAsync` (looped per target) | `_referees` add only | `CreateEventAsync` after the write | A |
| `MatchSubResourceRoutes.cs:473` | `DELETE /refs` → `RemoveOneRefereeAsync` (looped) | `_referees` remove only | `KickFromChatIfUnseated` (channel Part, a broadcast) then `CreateEventAsync`, both after the write | A |
| `MatchSubResourceRoutes.cs:606` | `PUT /ban` → `SetBansAsync` → `AddBanAndKickIfSeated` | `_bannedIds` add, then (if seated) delegates to `LeaveAsync` | see LeaveAsync | **B** — only when a newly-banned id is currently seated |
| `MatchSubResourceRoutes.cs:658` | `PATCH /ban` → `AddBansAsync` → `AddBanAndKickIfSeated` | same as above | see LeaveAsync | **B** — same condition |
| `MatchSubResourceRoutes.cs:700` | `DELETE /ban` → `UnbanAsync` (looped) | `_bannedIds` remove only | `PublishBansAsync` after | A |
| `MatchSubResourceRoutes.cs:851` | `POST /slots` (force-invite pre-leave, `oldMatch.Lock`) | delegates directly to `matchMembership.LeaveAsync` | see LeaveAsync | **B** |
| `MatchSubResourceRoutes.cs:865` | `POST /slots` (main invite loop, `match.Lock`) | per target: `ForceInviteAsync`→`ForceJoinAsync`→`OccupySlot`, or static `MatchControlService.Invite` (`_invitedIds` add only) | see OccupySlot | A — each iteration's mutation is self-contained; a throw mid-loop leaves some targets seated and others not, a valid state |
| `MatchSubResourceRoutes.cs:969` | `DELETE /slots` → `KickAsync` | delegates to `LeaveAsync` when target is a seated `GameSession` | see LeaveAsync | **B** |
| `MatchSubResourceRoutes.cs:1048` | `HandleSlotsWrite` → `SetSlotsAsync` | two synchronous loops over `entries`: loop 1 vacates old slots (`Reset()`), loop 2 occupies destination slots (`PlayerId`/`Status`/`Mods`/`Team`) | none — both loops are pure in-memory property sets/array indexing over pre-validated (0-15) indices; nothing between or inside them can throw | A — see Observations for a transient window this creates in the *non-exceptional* path |
| `MatchSubResourceRoutes.cs:1139` | `POST /timer` → `StartAsync`/`Timer` | see `MatchControlService.StartAsync`/`BeginCountdown` | see StartAsync caveat (autostart branch) | A |
| `MatchSubResourceRoutes.cs:1190` | `DELETE /timer` → `AbortTimer` | `PendingTimer=null`, `PendingTimerIsAutoStart=false`, `TimerStartedAt=null`, `TimerTotalSeconds=null` | none — fully synchronous | A |
| `MatchSubResourceRoutes.cs:1232` | `POST /abort` → `AbortAsync` | `UnreadyPlayers(Playing)`, `ResetPlayersLoadedStatus()`, `InProgress=false`, `CurrentRoundId=null` | `roundEndOutbox.Enqueue` (caught), then two broadcasts (`Enqueue`, `AnnounceToRoomAndReferees`) — all after the writes | A |
| `MatchSubResourceRoutes.cs:1278` | `POST /close` → `CloseAsync` → `matchMembership.CloseAsync`→`TeardownMatch` | no invariant-relevant field (see MatchMembershipService.cs:801 analysis) | n/a | A |

## `Features/Bot/MpCommandService.cs` — 2 sites

| File:line | Method | Fields written | Fallible call between | Class |
| --- | --- | --- | --- | --- |
| `MpCommandService.cs:315` | `JoinAsync` (`!mp join`) | delegates to `matchMembership.JoinAsync` → `OccupySlot` | see OccupySlot | A |
| `MpCommandService.cs:488` | `RunLockedAsync` — generic wrapper invoked by 21 different `!mp` subcommand closures | varies per closure (see breakdown) | varies | **B**, mixed — see breakdown |

`RunLockedAsync` is one lock acquisition reused for many unrelated action bodies. Breakdown of every
closure it runs:

| Subcommand | Delegate | Class |
| --- | --- | --- |
| `lock`/`unlock` | `SetRoomLocked` → static `IsLocked` | A |
| `size` | `SetSizeAsync` → `ApplySize` (empty slots only) | A |
| `move` | `MoveSlotAsync` → `CopyFrom`+`Reset`, synchronous consecutive | A |
| `host` | `SetHostAsync` → single-field `HostId` | A |
| `clearhost` | `ClearHostAsync` → single-field `HostId=NoHostId` | A |
| `name` | `SetNameAsync` → `Name` (+ `SyncChannelTopic` after) | A |
| `password` | `SetPasswordAsync` → `Password` | A |
| `invite` | static `MatchControlService.Invite` → `_invitedIds` add | A |
| `addref` | `AddRefereeAsync` → `_referees` add | A |
| `removeref` | `RemoveOneRefereeAsync` → `_referees` remove | A |
| `team` | `SetTeamAsync` → `slot.Team` | A |
| `set` | `SetTeamTypeWinConditionAndSizeAsync` → `TeamType`/slot `Team`(loop)/`WinCondition`/empty-slot `Status` | A |
| `map` | `SetMapAsync` → beatmap fetch **before** any write, then `MapId`/`MapMd5`/`MapName`/`Mode`/`UnreadyPlayers` | A |
| `mods` | `SetModsAsync` → `Freemods`/slot `Mods`(loop)/`Mods` | A |
| `start` | `StartAsync` → see StartAsync caveat | A |
| `timer` | `Timer` → `BeginCountdown`, non-invariant timer fields | A |
| `aborttimer` | `AbortTimer` → non-invariant timer fields, fully synchronous | A |
| `abort` | `AbortAsync` → see MatchSubResourceRoutes.cs:1232 analysis | A |
| `kick` | `KickAsync` → delegates to `LeaveAsync` when target is seated | **B** |
| `ban` | `BanAsync` → delegates to `LeaveAsync` when target is seated | **B** |
| `unban` | `UnbanAsync` → `_bannedIds` remove only | A |
| `close` | `CloseAsync` → `matchMembership.CloseAsync` | A |

The row is marked B because two of the closures it hosts (`kick`, `ban`) reach the `LeaveAsync`
hazard while holding this exact lock.

## `Shared/Sessions/PlayerLogoutService.cs` — 1 site

| File:line | Method | Fields written | Fallible call between | Class |
| --- | --- | --- | --- | --- |
| `PlayerLogoutService.cs:63` | `LogoutGameSessionAsync` | delegates directly to `matchMembership.LeaveAsync` | see LeaveAsync | **B** |

## Summary

- **47** real `MatchSession.Lock` acquisitions audited.
- **Class A: 40.** Either a single-field write, or every write after the first is infallible, or the
  fields written (settings, referees, bans, invites, timers) simply don't participate in any of the
  three invariants.
- **Class B: 7**, all one root cause: `MatchMembershipService.LeaveAsync` runs a broadcast
  (`channelMembership.Part`) between resetting the leaving player's slot and reassigning `HostId` when
  that player was the host. Every site that reaches `LeaveAsync` while holding the lock inherits it:
  - `Packets/PartMatchHandler.cs:28`
  - `Shared/Sessions/PlayerLogoutService.cs:63`
  - `Features/Multiplayer/MatchSubResourceRoutes.cs:606` (`PUT /ban`, only when a newly-banned id is seated)
  - `Features/Multiplayer/MatchSubResourceRoutes.cs:658` (`PATCH /ban`, same condition)
  - `Features/Multiplayer/MatchSubResourceRoutes.cs:851` (force-invite's pre-leave of another match)
  - `Features/Multiplayer/MatchSubResourceRoutes.cs:969` (`DELETE /slots`, kick)
  - `Features/Bot/MpCommandService.cs:488` (only the `kick` and `ban` closures it hosts; the other 19 closures it runs are Class A)
- **Class `?`: 0.** No site was left genuinely ambiguous; the one edge case that came close
  (`MatchMembershipService.StartAsync`'s `InProgress` write) resolved to A because the throw itself
  adds no *new* invariant exposure beyond what already exists in the non-exceptional path — recorded
  under Observations instead of `?` since it isn't the exception that's in question there.

## Observations

Findings adjacent to the task but outside its exact "does a throw break an invariant" scope:

1. **`MatchControlService.SetHostAsync` (lines 253-271) never checks that the target occupies a slot
   in the match**, and its HTTP caller (`PUT /matches/{matchId}/hosts`, `MatchSubResourceRoutes.cs:226-229`)
   only checks that the target user id is online — not that they're seated in *this* match. This can
   set `HostId` to a user occupying no slot at all, violating invariant 2 in the ordinary,
   non-exceptional path. The chat equivalent (`MpCommandService.SetHost`, line 719) does guard this
   (`gameTarget.Match != match`); the HTTP path does not.

2. **`MatchControlService.SetSlotsAsync` (lines 1187-1265) has a two-loop shape** (vacate every moved
   player's old slot, then occupy every destination slot) that creates a real transient window where a
   moved player occupies no slot in the match at all, between the two loops. If that player were the
   host, this would violate invariant 2 for the duration of the window. Today this is harmless because
   nothing between or inside the two loops can throw (pure synchronous property sets over pre-validated
   indices) — but it's a latent trap for a future change that adds any awaited or fallible step between
   them.

3. **`MpCommandService.cs:488` acquires the lock with no cancellation token**
   (`await match.Lock.WaitAsync()`), unlike every other of the 47 sites, all of which pass one. Flagged
   in `plans/execution/phase-1-multiplayer.md` as worth a look; nothing in this audit suggests it's
   deliberate.

4. **`MpCommandService`'s `"private"` subcommand (line 178) is the only mutating `!mp` subcommand not
   routed through `RunLockedAsync`.** `SetPrivate` calls `_matchControl.SetPrivateAsync` directly,
   without acquiring `match.Lock` at all — a deviation from the documented rule on `MatchSession.Lock`
   that every read-then-mutate sequence hold it. The mutation itself is a single bool field
   (`IsPrivate`), so it can't produce an invariant-violating intermediate state, but it is a real,
   unexplained gap in the locking discipline (a concurrent `!mp private` racing another mutation is not
   serialized the way every other subcommand is).

5. **`MatchMembershipService.StartAsync` sets `match.InProgress = true` unconditionally** after the
   occupied-slot loop, even on the (currently unreachable-in-practice, but not type-enforced) path where
   zero slots are occupied — e.g. a countdown queued by `!mp start <seconds>` that nobody cancels while
   every player leaves before it fires (leaving only `CancelQueuedAutoStart` triggers, none of which
   include a player leaving). If that happens, invariant 3 is violated with no exception involved at
   all. Out of scope for this audit (which is about throws), but directly relevant to the invariants
   being audited.
