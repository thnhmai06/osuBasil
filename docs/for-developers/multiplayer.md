# Multiplayer and tournament match reports

## Overview

A multiplayer match in Basil has two distinct representations:

* a live `MatchSession` driven by the Bancho protocol;
* a persisted tournament record used to reconstruct the match report after the live room is gone.

The live session owns transient state such as slots, host, referees, timers, and the current round. Persistent
`Matches`, `Rounds`, and `Scores` rows provide the durable history.

The tournament match report (TRT) is built from these sources rather than stored as a separate document.

## Why the report is derived

A completed tournament match needs to retain:

* which rounds were played;
* which beatmap and rules were used for each round;
* which players submitted scores;
* the scores and team assignments;
* the resulting winner.

The live room cannot provide this information after its sessions disappear.

However, storing a second complete representation of the match while it is running would duplicate state already
maintained by `MatchSession` and introduce synchronization problems.

Basil therefore persists the events and results needed to reconstruct the report and derives the report when it is
requested.

The effective model is:

```text
Live match
    │
    ├── transient state
    │      ├── slots
    │      ├── host
    │      ├── referees
    │      └── current round
    │
    └── persisted state
           ├── Match
           ├── Rounds
           └── Scores
                    │
                    ▼
             MatchReportService
                    │
                    ▼
               match report
```

There is no persisted report document that can become stale.

## Contract

* `GET /matches/{matchId}` builds the report at read time.
* For an active match, the report combines persisted rounds and scores with the current live `MatchSession`.
* For a closed match, the report can be reconstructed entirely from persistent state.
* Every match sub-resource has a JSON endpoint and, where applicable, a corresponding `/live` SSE endpoint. The SSE side
  is documented in [`sse.md`](sse.md).
* `POST /matches/{matchId}/abort` and `POST /matches/{matchId}/close` return the resulting match state rather than a
  separate success document.
* The winning team is derived when the report is built rather than stored as another piece of mutable match state.
* Match identity is represented by `Matches.Id`; the Bancho protocol's room id is a separate, short-lived identifier.

## Match identity

A match has two different identifiers during its lifetime.

### Persistent match ID

`Matches.Id` is the stable match identifier used by:

* API routes;
* chat commands;
* database relationships;
* persisted reports.

It remains valid after the live room has disappeared.

### Bancho room id

The live Bancho protocol identifies a room by a room id. The lobby hands one out when the room opens: the lowest id
not held by another open room. The protocol carries it as an unsigned 16-bit value, so at most 65,535 rooms can be
open at once.

The room id is:

* short-lived, and reused once its room closes;
* unrelated to the persistent database id.

Code must not use the room id as the external identity of a match.

The database therefore stores the stable match id while the live room owns the protocol-level room id.

## Match roles

Basil deliberately models three independent match roles.

### Creator

`MatchSession.CreatorId` identifies the account that created the room.

A creator is assigned when a room is created through:

* `!mp make`;
* `!mp makeprivate`;
* the game client's match-creation flow.

A match created through `POST /matches` has no creator.

The creator:

* permanently counts as a referee;
* cannot be removed from the referee list;
* retains this status for the lifetime of the room;
* is the only account allowed to add or remove referees through chat.

Creator status is therefore an immutable property of the room, not a property of the creator's current session.

### Referee

`MatchSession.Referees` represents persistent match authority.

A referee:

* can execute referee-level `!mp` commands;
* can remain a referee after disconnecting;
* can remain a referee after leaving the room;
* loses the role only through explicit referee removal.

Referee status is independent of whether the user is currently seated.

### Host

`MatchSession.HostId` represents transient in-client host authority. It is `null` while nobody
holds it.

Unlike creator and referee status, host status depends on the live match membership:

* a seated player can become host;
* `!mp host` transfers host authority;
* leaving the room removes host authority;
* an IRC-only user cannot become host because an `IrcSession` cannot occupy a multiplayer slot.

The three roles must not be collapsed into a single "owner" concept.

## Match lifecycle

A match begins as a live room and accumulates persistent tournament history as rounds are played.

```text
!mp make / CREATE_MATCH
        │
        ▼
Create MatchSession
        │
        ├── creator/referee established
        └── eligible game creator may occupy slot 0
        │
        ▼
!mp start / MATCH_START
        │
        ▼
Create Round
        │
        └── latest round = (match, previous number + 1)
        │
        ▼
Players submit scores
        │
        └── score references the latest round
        │
        ▼
MATCH_COMPLETE
        │
        └── round is marked complete
        │
        ▼
!mp start again
        │
        └── create next Round,
            which becomes the latest round
        │
        ▼
!mp close / POST /matches/{id}/close
        │
        ▼
Live room ends
        │
        ▼
GET /matches/{id}
        │
        └── report reconstructed from persisted history
```

A match can therefore have multiple rounds, while the room's latest round is the one receiving score submissions.

## Rounds

A `Round` represents one beatmap played inside a match.

It is created when a new round starts, not when the match itself is created.

A round is identified by its match and its number. Rounds are numbered from `1` within each match in the order they
start. An aborted round is still a round: it keeps its number, is marked as aborted, and the next round takes the
following number. The live room assigns the number itself, so starting a round does not wait for the database.

A round records information such as:

* game mode;
* win condition;
* team type;
* mods;
* beatmap content hash;
* round lifecycle state.

A best-of-nine match can therefore produce up to nine `Rounds` rows.

Scores reference the round they belong to rather than relying on the current state of the live room.

See [`database.md`](database.md) for the persistent schema.

## Current round and score submission

The room's latest round is deliberately kept when `MATCH_COMPLETE` arrives.

The reason is a race between two independent transports.

```text
Bancho connection
    │
    └── MATCH_COMPLETE
             │
             └── round appears complete

Score submission HTTP request
    │
    └── replay + score
             │
             └── may arrive before or after MATCH_COMPLETE
```

The two requests have no ordering guarantee.

If the latest round were forgotten as soon as `MATCH_COMPLETE` was processed, a score that arrives shortly afterward
would have no round to attach to, even though it belongs to the round that just finished.

Instead:

```text
start round
    │
    └── latest round = round 1
              │
              ├── score submission → round 1
              │
              ├── MATCH_COMPLETE
              │
              └── late score submission → still round 1
                         │
                         ▼
                  next !mp start
                         │
                         └── latest round = round 2
```

The latest round therefore changes only when the next round starts.

A score is attached to the latest round only when it was played on that round's beatmap. A score that arrives after
the next round has already started on a different beatmap is therefore not attached to the newer round. Two
consecutive rounds on the same beatmap cannot be told apart this way; such a late score attaches to the newer round.

This removes the race window without requiring score submission and Bancho packet processing to share a connection or
transaction.

## Match mutation concurrency

`MatchSession.Lock` protects mutations to live match state.

Operations that modify the room must use the same synchronization boundary regardless of their transport.

For example:

```text
Bancho packet ────────┐
                      │
!mp command ──────────┼──> shared match service ──> MatchSession.Lock
                      │
HTTP API ─────────────┘
```

This prevents two different entry points from concurrently modifying:

* slots;
* host;
* referees;
* match settings;
* other mutable live-room state.

An HTTP administrative operation therefore follows the same concurrency rules as the equivalent Bancho operation.

### Round-end persistence

Persisting that a round ended is a database write, and a database write can be slow enough (SQLite lock contention,
retry/backoff) that doing it while holding `MatchSession.Lock` would block every other operation on that match for
the duration.

Round-*end* writes are therefore queued and persisted outside the lock, on a single shared, ordered background
queue: the caller enqueues the fact (match id, round number, end time, whether it was an abort) under the lock, without
waiting for the write to land, and a dedicated background consumer drains the queue and persists each entry in the
order it was enqueued. A match's own round-end writes are always enqueued one at a time under that match's own lock,
so two writes for the same match can never be persisted out of order, even though the queue itself is shared across
every match.

Round-*start* does not need a synchronous write either: the room numbers the round itself, so score submission can
reference it by match and number as soon as the round starts, without waiting for the round to be persisted.

A permanently failed round-end write (retry budget exhausted) is logged with every fact needed to reconstruct it by
hand rather than silently dropped. This is an accepted gap, not a correctness bug: match reports are generated on
demand from whatever made it to the database, not from an in-memory source of truth.

## Empty-room lifecycle

A match with no seated players is automatically closed after fifteen minutes.

This applies regardless of how the room was created.

A room can initially have no seated player, for example when:

* an IRC referee creates it;
* an IRC user creates it and has no game session;
* a game-session creator is already seated in another match;
* the room is created through the API.

There is no separate permanent-room state.

The empty-room timer:

1. starts when the room has no seated players;
2. after ten minutes still empty, announces a 5-minute warning;
3. sends the warning to referees who are not currently in the room's channel;
4. closes the room five minutes later if nobody joins;
5. is cancelled when a player joins, whether before or after the warning.

The room's creator still retains creator/referee authority even when no player is seated.

See [`irc.md`](irc.md) for the relationship between IRC sessions, referees, and match channels.

## Report generation

`MatchReportService` constructs the report when requested.

For a closed match:

```text
Matches
   │
   ├── Rounds
   │      │
   │      └── Scores
   │
   ▼
MatchReportService
   │
   ▼
complete tournament report
```

For an active match:

```text
Database history ───────┐
                        ├──> MatchReportService ──> report
Live MatchSession ──────┘
```

The live session contributes information that has not yet become part of the persistent tournament history.

This lets the same endpoint represent both:

* a match currently being played;
* a completed historical match.

Clients do not need separate "live report" and "final report" APIs.

## Winner calculation

The winning side is derived from completed round scores.

* For a team-based round, scores are grouped by team and compared.

* For a non-team round, the highest individual score determines the winner.

The winner is therefore a projection of persisted score data rather than another mutable field that must be kept
synchronized whenever a score changes.

Conceptually:

```text
Scores
  │
  ├── team round ──> aggregate by team ──> winning team
  │
  └── individual ──> highest score ──────> winning player
```

This also means a report cannot become inconsistent because a separately stored winner was not updated.

## Invariants

The multiplayer/report model depends on several invariants:

* `Matches.Id` is the stable external match identity; the Bancho room id is not.
* A `Round` represents one beatmap played in a match, identified by its match and its number.
* Rounds are numbered from `1` within their match; an aborted round keeps its number.
* A score played on the beatmap of the room's latest round is associated with that round.
* The latest round stays set after `MATCH_COMPLETE` until the next round starts.
* Match mutations are synchronized through `MatchSession.Lock`.
* Creator, referee, and host are independent roles.
* Creator status is permanent for the lifetime of the match.
* Referee status survives disconnects and leaving the room.
* Host status exists only while a player is seated.
* An `IrcSession` cannot occupy a multiplayer slot.
* The report is derived rather than persisted as a separate document.
* The winner is derived from score data rather than stored independently.
* Closing a live room does not destroy the persistent match history.
* A match's round-end writes are persisted in the order they ended, even though they run outside `MatchSession.Lock`.
* A round closes at most once: `MatchCompleteHandler` treats `MatchSession.InProgress` as the round's open/closed
  flag and no-ops immediately if it is already `false`, so a duplicate or late-arriving completion for an
  already-closed round cannot re-enqueue that round's end a second time. Every slot stays `Complete` once a round
  closes (nothing resets it until the next start), so without this check the "no slot still playing" condition
  alone would let a repeat completion straight through.
* A `GameSession` is removed from the session registry only after its match slot has already been cleared, both
  under the same match's lock (`PlayerLogoutService.LogoutAsync` → `MatchMembershipService.LeaveAsync`'s
  `slot.Reset(...)`, then `gameRegistry.Remove(...)` after the lock is released). `PlayerLogoutService` is the
  single removal path (verified: no other call site removes a `GameSession` from the registry), so a match's
  `CloseAsync` sweep, itself running under the same lock, can never observe a slot whose `PlayerId` still points
  to a session the registry no longer has.

## Related code

* [`Basil.Application/Services/Multiplayer/MatchReportService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchReportService.cs): report construction and winner calculation
* [`Basil.Application/Services/Multiplayer/MatchMembershipService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchMembershipService.cs): player membership, slots, and empty-room lifecycle
* [`Basil.Application/Backgrounds/MatchRoundEndOutbox.cs`](../../src/Basil.Application/Backgrounds/MatchRoundEndOutbox.cs): ordered, outside-the-lock round-end persistence
* [`Basil.Web/Routing/Api/MatchRoutes.cs`](../../src/Basil.Web/Routing/Api/MatchRoutes.cs): match-level API routes
* [`Basil.Web/Routing/Api/MatchSubResourceRoutes.cs`](../../src/Basil.Web/Routing/Api/MatchSubResourceRoutes.cs): match sub-resource routes
* [`Basil.Application/Sessions/GameSession.cs`](../../src/Basil.Application/Sessions/GameSession.cs): live gameplay state
* [`Basil.Application/Sessions/IrcSession.cs`](../../src/Basil.Application/Sessions/IrcSession.cs): IRC-only session state

## See also

* [`bancho.md`](bancho.md): packet-level match creation and mutation
* [`irc.md`](irc.md): IRC sessions, match channels, referees, and empty-room behavior
* [`sse.md`](sse.md): live SSE representations of match resources
* [`database.md`](database.md): `Matches`, `Rounds`, and `Scores` persistence
