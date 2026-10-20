# Multiplayer and match reports

## Overview

A `Room` is the live multiplayer state. It owns its slots, current round, settings, authority, members and room channel. A `MatchRecord` and its round and score records preserve history after the room closes. A match report is derived from match history; Basil does not keep a second mutable report.

This separation keeps current play state in one place while retaining the facts needed to understand completed matches.

## Room behavior and authority

### Room-scoped actions

An action that depends on a caller starts from the room and the caller's real `Connection`:

```csharp
var scopedRoom = room.ScopeTo(by);
```

`ScopedRoom` holds the room, caller, room roles and derived `RoomPermissions` for one request or command. Use its parts—`Slots`, `Rounds`, `Settings`, `Authority` and `Members`—rather than asking `IRoomService` to perform a player's action. Do not retain a scope after the request: its authority is a snapshot.

Each operation returns a `RoomResult` for ordinary outcomes such as success, missing authority or a suspended permission. The caller does not pass permissions through each operation, and connection type alone never grants authority.

**Planned:** the `!mp` command entry point will resolve the room and typist's connection, then call `room.ScopeTo(by)`. The command integration is not yet on this scoped model.

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

* Effective permissions must include `RoomJoin`; otherwise the scoped room has no role-derived room permissions.
* With effective `RoomJoin` and `RoomManageAny`, the scope grants all `RoomPermissions` in any room.
* Restrictions remove suspended permissions before the scope is created. A user who manages an area keeps that area's permissions while restricted; for multiplayer, that manager permission is `RoomManageAny`.

This is why a creator or referee whose room permissions are suspended cannot manage a room, while an effective room manager keeps multiplayer authority. `RoomResult.Silenced` distinguishes a suspended permission from `RoomResult.NotAuthorized`.

### System-owned operations

Some room work has no user caller. `IRoomService` retains system operations such as cleaning up room membership and recording submitted scores. These operations do not create a caller scope. Caller-authorized room behavior belongs to `ScopedRoom` and its parts.

## Membership, slots and settings

The room owns membership and its channel. A join checks the caller's effective `RoomJoin` permission, bans, whether the caller is already playing or observing, the password and available slots. A successful player join also joins the room channel. When a same-user connection replaces a closed seat, the new join must pass its checks first; an open old connection prevents replacement.

There are 16 slot positions, numbered 1 through 16. Room size is the number of usable positions, from 1 through 16, not a limit on slot numbers: a player may occupy any slot. The room keeps enough unlocked empty slots for its configured size and locks the others. When a host leaves, the next seated player in slot order becomes host.

The room channel is named `#mp_{room id}` and opens and closes with its room. A room's settings change as one validated operation; settings cannot change during a round. Changing the team type reassigns teams, and changing mode removes mods that mode does not allow. The room's password is never included in its events.

## Rounds and score association

A round is created when play starts. Rounds are numbered from 1 in start order; an aborted round keeps its number. The latest round remains available after it ends until another round starts, because score submission can arrive before or after round completion is recorded.

The room emits round events for start, load and skip progress, completion, abort and score submission. Infrastructure records submitted scores as system work, outside caller-authorized room operations. The report and winner are projections of match, round and score data, not additional mutable room state.

## Countdown

The countdown is room behavior, not a BasilBot-owned timer. `ScopedRoom.Rounds` starts or cancels it; the room owns its timing and state, and a countdown may start a round when it expires. `RoomCountdown` provides the start time, length and announcement marks. The room emits countdown events for a client to announce.

**Planned:** BasilBot's command integration will request countdown operations through the typist's room scope and announce the room's marks. It will not own the countdown or act with its own authority.

## Room lifecycle and concurrency

The lobby opens and closes rooms. A room created in game closes when its last player leaves. An empty tournament room remains open for 15 minutes; the lobby announces the scheduled close when it starts and five minutes before it, and a player joining cancels it. If the room becomes empty again, the delay starts over.

Room state is mutable and shared. A read followed by a mutation must hold the exclusive scope from `room.EnterAsync()` across the complete transition. If the room is already closed, `EnterAsync()` returns `null`. Never hold two room scopes at once, and do not hold a room scope across a long-lived or unrelated wait. Do not add another synchronization mechanism for room state.

## Invariants

* The creator, referees and host are separate roles; host status alone grants no management authority.
* Caller-authorized behavior is reached through `room.ScopeTo(by)` and uses effective permissions.
* A scoped room is short-lived; its role and room-permission snapshot is for one request or command.
* Every room state transition that reads and mutates holds one room scope throughout.
* The 16 positions are numbered 1–16; room size controls usable positions, not the highest slot number.
* The next host is selected by slot order.
* Round numbers start at 1, and an aborted round keeps its number.
* The latest round remains available until the next one starts; score association uses the latest round's beatmap.
* A closed room's match history remains; reports and winners are derived rather than maintained separately.
* Performance points do not affect multiplayer rules or match winners.

## Related code

* [`Room.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/Room.cs): live room and `ScopeTo`
* [`ScopedRoom.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/ScopedRoom.cs): caller, role and room-permission scope
* [`ScopedRoomMembers.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/ScopedRoomMembers.cs), [`ScopedRoomSlots.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/ScopedRoomSlots.cs), [`ScopedRoomRounds.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/ScopedRoomRounds.cs), [`ScopedRoomSettings.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/ScopedRoomSettings.cs), [`ScopedRoomAuthority.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/ScopedRoomAuthority.cs): caller-authorized room parts
* [`IRoomService.cs`](../../src/Application/Services/Contracts/Multiplayer/IRoomService.cs): system-owned room operations
* [`RoomRoles.cs`](../../src/Domain/Multiplayer/RoomRoles.cs), [`RoomPermissions.cs`](../../src/Domain/Multiplayer/RoomPermissions.cs), [`RoomPermissionsGroups.cs`](../../src/Domain/Multiplayer/RoomPermissionsGroups.cs): room rights
* [`RoomCountdown.cs`](../../src/Application/Services/Contracts/Multiplayer/Rooms/RoomCountdown.cs): countdown state and marks
* [`Lobby.cs`](../../src/Application/Services/Contracts/Multiplayer/Lobby.cs), [`ScopedLobby.cs`](../../src/Application/Services/Contracts/Multiplayer/ScopedLobby.cs): lobby-owned lifetime and caller-scoped operations

## See also

* [`chat.md`](chat.md): channel ownership and room chat
* [`privileges.md`](privileges.md): account permissions, restrictions and room-manager rights
* [`sse.md`](sse.md): live-stream design
* [`database.md`](database.md): match history persistence
