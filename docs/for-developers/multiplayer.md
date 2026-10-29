# Multiplayer and match reports

## Overview

A `Room` is the live multiplayer state. It owns its slots, current round, settings, authority, members; Chat keeps its room channel. A `MatchRecord` and its round and score records preserve history after the room closes. A match report is derived from match history; Basil does not keep a second mutable report.

This separation keeps current play state in one place while retaining the facts needed to understand completed matches.

## Room behavior and authority

### Room-scoped actions

An action that depends on a caller starts from the room and the caller's real `Connection`:

```csharp
var scopedRoom = room.ScopeTo(by);
```

`ScopedRoom` holds the room, caller, room roles and derived `RoomPermissions` for one request or command. Use its parts—`Slots`, `Rounds`, `Settings`, `Authority` and `Members`—rather than asking a service to perform a player's action. Do not retain a scope after the request: its authority is a snapshot.

Each operation returns a `RoomResult` for ordinary outcomes such as success, missing authority or a suspended permission. The caller does not pass permissions through each operation, and connection type alone never grants authority.

The `!mp` command entry point resolves the room and the typist's connection, then calls `room.ScopeTo(by)`; BasilBot only posts the replies.

### Creator, referee and host

These roles have different owners and lifetimes:

| Role | Held by | Authority |
|---|---|---|
| Creator | The user recorded as the match creator | Permanent room authority; may manage referees |
| Referee | A user added by the creator | Room-management authority until removed |
| Host | The seated osu! connection selected as host | In-client host operations; does not make its user a manager |

Every room has a creator. The creator is not in the referee list and ranks above referees. A room created in game seats its creator as the first host. Tournament-room creation seats the creator only when their game connection is online and not already in a room. A referee keeps authority when disconnected or unseated. There can be at most eight referees in addition to the creator.

### Room roles and permissions

`RoomRoles` are calculated for the scoped caller. A caller may hold more than one role:

| `RoomRoles` value | How it is held | `RoomPermissions` granted by the role |
|---|---|---|
| `Player` | An osu! connection occupies a slot | Post in room chat; invite users |
| `Host` | The connection is the room's current host | Change settings, lock slots, kick players, transfer host, start or abort rounds, manage countdowns |
| `Referee` | The caller's user is a referee and is not the creator | All room permissions except managing referees |
| `Creator` | The caller's user is the creator | All room permissions, including managing referees |

`RoomPermissionsGroups.GetPermissions` combines permissions from the caller's `RoomRoles`. The general account permissions then constrain that set:

* With effective `RoomManageAny`, the scope grants all `RoomPermissions` in any room, joined or not: managing every room acts with the creator's authority and a restriction cannot suspend it.
* Otherwise the effective permissions must include `RoomJoin`; without it the scoped room has no role-derived room permissions.
* Restrictions remove suspended permissions before the scope is created. A user who manages an area keeps that area's permissions while restricted; for multiplayer, that manager permission is `RoomManageAny`.

This is why a creator or referee whose room permissions are suspended cannot manage a room, while an effective room manager keeps multiplayer authority. `RoomResult.Silenced` distinguishes a suspended permission from `RoomResult.NotAuthorized`.

### System-owned operations

Some room work has no user caller. The `Lobby` and the `Room` keep system operations such as releasing a connection from the rooms, removing a suspended member or referee, recovering unfinished matches (`Lobby`) and taking a submitted score for a round (`Room.ReceiveScore`). Their summaries say that the server itself calls them and that they need no rights; they do not create a caller scope. Caller-authorized room behavior belongs to `ScopedRoom` and its parts.

## Membership, slots and settings

The room owns membership; Chat keeps its channel in step with the room's events. A join checks the caller's effective `RoomJoin` permission, bans, whether the caller is already playing or observing, the password and available slots. A successful player join also joins the room channel. When a same-user connection replaces a closed seat, the new join must pass its checks first; an open old connection prevents replacement.

There are 16 slot positions, numbered 1 through 16. Room size is the number of usable positions, from 1 through 16, not a limit on slot numbers: a player may occupy any slot. The room keeps enough unlocked empty slots for its configured size and locks the others. When a host leaves, the next seated player in slot order becomes host. Locking a room (`!mp lock`) only freezes the slots: players cannot move to another slot or change team, but joining is unaffected, as in osu!.

A room can be private. A private match is hidden from the lobby and from the public match lists, and its history and
report can be viewed only by (a) a user who took part in it (a `PlayerJoined` event targets them), (b) its managers: the
creator and the referees that are still referees (a `RefAdded` event not followed by a `RefRemoved` for the same user), and
(c) a user with the effective permission `RoomManageViewPrivate` (`RoomManageAny` includes it). A public match can be viewed by
everyone. Private never changes who can join: whoever knows the room's id can still find it (`Lobby.Find`) and join it, with
the password if it has one, subject to the usual `RoomJoin` and ban rules. There is no invite-only mode, and an
invitation only tells a user where the room is.

The view restriction covers everything the match owns: the match record, its rounds, its events, its report and the
relation that says which scores were played in which round. It does not cover independent resources the room only links
to: a score with its replay and leaderboards is read by its own id as before, and beatmaps and users stay as they are.
`ScopedLobby.CanViewAsync` is the single gate for the match-owned data.

A match listing is narrowed to its asker with `MatchQuery.LimitTo(effective, viewer)`, and a single match is checked with
`ScopedLobby.CanViewAsync`; both apply the same rule (`IMatchRepository.IsVisibleToAsync` is the check for one match).

The room channel is named `#mp_{room id}` and opens and closes with its room. A room's settings change as one validated operation; settings cannot change during a round. Changing the team type reassigns teams, and changing mode removes mods that mode does not allow. The room's password is never included in its events.

## Rounds and score association

A round is created when play starts. Rounds are numbered from 1 in start order; an aborted round keeps its number. The latest round remains available after it ends until another round starts.

Scores and rounds are two separate businesses. The score module stores every score the same way, solo or multiplayer, and publishes `ScoreSubmitted`; it knows nothing about rounds and the client names no room. When a round starts, the room snapshots the players who take part with the team of each slot. The lobby offers every `ScoreSubmitted` to the rooms; a room takes the score as the score of its latest round when its player takes part, the beatmap is the round's, the round is not yet decided or aborted, and the player has no score in it yet. A player who leaves before completing is struck off and no score is awaited from them; a player who completed and then left is still awaited. Taking a score publishes `RoomRoundScoreSubmitted`, which a handler stores as the relation between the score and the round.

A round is decided once, automatically, as soon as it has ended and every awaited player has a score, or after `ServerSettings.Multiplayer.Rounds.ScoreWait` (10 seconds by default) with the scores that came in (the wait is total, not per player). A round with no participants is not decided, and an aborted round is never decided. BasilBot announces the end of the round only when someone completed, then either that all scores arrived or the names of the players still missing, in the room channel. A room that closes while its round waits decides it at once. The decision (`RoundVerdict`) is stored on the round when it is decided, so a later change of the rules leaves old decisions as they were. The report only reads the stored decision; it never decides and never writes.

## Countdown

The countdown is room behavior, not a BasilBot-owned timer. `ScopedRoom.Rounds` starts or cancels it; the room owns its timing and state, and a countdown may start a round when it expires. `RoomCountdown` provides the start time, length and announcement marks. The room emits countdown events, and BasilBot announces the marks.

## Room lifecycle and concurrency

The lobby opens and closes rooms. A room created in game closes when its last player leaves. An empty tournament room remains open for 15 minutes; the lobby announces the scheduled close when it starts and five minutes before it, and a player joining cancels it. If the room becomes empty again, the delay starts over.

Room state is mutable and shared. Every transition that reads and then mutates is a synchronous method of `Room` (`Join`, `Kick`, `StartRound`, `Configure` and the rest) that takes the room's own lock, checks the state, changes it and publishes its events before releasing the lock; publishing only queues the event, so nothing is awaited under the lock. A closed room answers `RoomClosed`. `ScopedRoom` only checks the caller's permissions, does any asynchronous lookup that must come first, and calls the room's method. Never hold two room locks at once. Do not add another synchronization mechanism for room state.

A player is in at most one room. A room's own check cannot see another room's seating that is still under way, so the shell of a join claims the player through `Lobby.TryClaim` before calling the room and releases the claim afterwards; a second room that meets a claim answers `InAnotherRoom`.

## Invariants

* The creator, referees and host are separate roles; host status alone grants no management authority.
* Caller-authorized behavior is reached through `room.ScopeTo(by)` and uses effective permissions.
* A scoped room is short-lived; its role and room-permission snapshot is for one request or command.
* Every room state transition that reads and mutates runs inside one method of `Room` under the room's lock.
* The 16 positions are numbered 1–16; room size controls usable positions, not the highest slot number.
* The next host is selected by slot order.
* Round numbers start at 1, and an aborted round keeps its number.
* The latest round remains available until the next one starts; a score is taken for it by the player, the beatmap and the round's state, never by the score module.
* A closed room's match history remains; a round's decision is stored when it is decided and reports read it.
* Performance points do not affect multiplayer rules or match winners.

## Related code

* [`Room.cs`](../../src/Application/Multiplayer/Runtime/Rooms/Room.cs): live room and `ScopeTo`
* [`ScopedRoom.cs`](../../src/Application/Multiplayer/Runtime/Rooms/ScopedRoom.cs): caller, role and room-permission scope
* [`ScopedRoomMembers.cs`](../../src/Application/Multiplayer/Runtime/Rooms/ScopedRoomMembers.cs), [`ScopedRoomSlots.cs`](../../src/Application/Multiplayer/Runtime/Rooms/ScopedRoomSlots.cs), [`ScopedRoomRounds.cs`](../../src/Application/Multiplayer/Runtime/Rooms/ScopedRoomRounds.cs), [`ScopedRoomSettings.cs`](../../src/Application/Multiplayer/Runtime/Rooms/ScopedRoomSettings.cs), [`ScopedRoomAuthority.cs`](../../src/Application/Multiplayer/Runtime/Rooms/ScopedRoomAuthority.cs): caller-authorized room parts
* [`Lobby.cs`](../../src/Application/Multiplayer/Runtime/Lobby.cs), [`Room.cs`](../../src/Application/Multiplayer/Runtime/Rooms/Room.cs): system-owned room operations
* [`RoomRoles.cs`](../../src/Domain/Multiplayer/RoomRoles.cs), [`RoomPermissions.cs`](../../src/Domain/Multiplayer/RoomPermissions.cs), [`RoomPermissionsGroups.cs`](../../src/Domain/Multiplayer/RoomPermissions.cs): room rights
* [`RoomCountdown.cs`](../../src/Application/Multiplayer/Runtime/Rooms/RoomCountdown.cs): countdown state and marks
* [`Lobby.cs`](../../src/Application/Multiplayer/Runtime/Lobby.cs), [`ScopedLobby.cs`](../../src/Application/Multiplayer/Runtime/ScopedLobby.cs): lobby-owned lifetime and caller-scoped operations

## See also

* [`chat.md`](chat.md): channel ownership and room chat
* [`privileges.md`](privileges.md): account permissions, restrictions and room-manager rights
* [`sse.md`](sse.md): live-stream design
* [`database.md`](database.md): match history persistence
