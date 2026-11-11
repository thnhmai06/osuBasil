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

`ScopedRoom` holds the room, the caller and the `RoomPrivileges` the caller holds for one request or command. Use its parts—`Slots`, `Rounds`, `Settings`, `Authority` and `Members`—rather than asking a service to perform a player's action. Do not retain a scope after the request: its authority is a snapshot.

Each operation returns a `RoomResult` for ordinary outcomes such as success, missing authority or a suspended privilege. The caller does not pass privileges through each operation, and connection type alone never grants authority.

The `!mp` command entry point resolves the room and the typist's connection, then calls `room.ScopeTo(by)`; BasilBot only posts the replies.

### Creator, referee and host

These roles have different owners and lifetimes:

| Role | Held by | Authority |
|---|---|---|
| Creator | The user recorded as the match creator | Permanent room authority; may manage referees |
| Referee | A user added by the creator | Room-management authority until removed |
| Host | The seated osu! connection selected as host | In-client host operations; does not make its user a manager |

Every room has a creator. The creator is not in the referee list and ranks above referees. A room created in game seats its creator as the first host. A room opened through the API or BasilBot seats the creator as host only when their game connection is online and not already in a room; the source that opens a room decides this, not a kind of room. A referee keeps authority when disconnected or unseated. There can be at most eight referees in addition to the creator.

### Room roles and privileges

`RoomPrivileges` is what a user may do in a room. It is a concept separate from the account privileges (`UserPrivileges`): room privileges are derived per room from the roles the caller holds, never stored, and no account privilege is derived from them. A role is only a named group of room privileges; a caller holding several roles holds the union of their groups:

| Group | How it is held | Room privileges in the group |
|---|---|---|
| `RoomPrivileges.Player` | An osu! connection occupies a slot | Post in room chat; invite users |
| `RoomPrivileges.Host` | The connection is the room's current host | Change settings, lock slots, kick players, transfer host, start or abort rounds |
| `RoomPrivileges.Referee` | The caller's user is a referee and is not the creator | Every room privilege except managing referees |
| `RoomPrivileges.Creator` | The caller's user is the creator | Every room privilege, including managing referees |

The account's privileges then constrain what the scope holds:

* With effective `Tournament`, the scope holds `RoomPrivileges.Creator` in any room, joined or not: managing every room acts with the creator's authority.
* Otherwise the account privileges must include `Player`; without it the scoped room holds no room privileges.

This is why a creator or referee who has lost `Player` cannot manage a room, while a `Tournament` holder keeps multiplayer authority. A refusal for lack of a room privilege is the outcome `RoleDenied`; it carries no privilege mask.

### System-owned operations

Some room work has no user caller. The `Lobby` and the `Room` keep system operations such as releasing a connection from the rooms, removing a suspended member or referee, recovering unfinished matches (`Lobby`) and taking a submitted score for a round (`Room.ReceiveScore`). Their summaries say that the server itself calls them and that they need no rights; they do not create a caller scope. Caller-authorized room behavior belongs to `ScopedRoom` and its parts.

## Membership, slots and settings

The room owns membership; Chat keeps its channel in step with the room's events. A join checks the caller's effective `Player` privilege, bans, whether the caller is already playing or observing, the password and available slots. A successful player join also joins the room channel. When a same-user connection replaces a closed seat, the new join must pass its checks first; an open old connection prevents replacement.

There are 16 slot positions, numbered 1 through 16. Room size is the number of usable positions, from 1 through 16, not a limit on slot numbers: a player may occupy any slot. The room keeps enough unlocked empty slots for its configured size and locks the others. When a host leaves, the next seated player in slot order becomes host. Locking a room (`!mp lock`) only freezes the slots: players cannot move to another slot or change team, but joining is unaffected, as in osu!.

A room can be private. A private match is hidden from the lobby and from the public match lists, and its history and
report can be viewed only by (a) a user who took part in it (a `PlayerJoined` event targets them), (b) its managers: the
creator and the referees that are still referees (a `RefAdded` event not followed by a `RefRemoved` for the same user), and
(c) a user with the effective privilege `Tournament` (`Tournament` includes it). A public match can be viewed by
everyone. Private never changes who can join: whoever knows the room's id can still find it (`Lobby.Find`) and join it, with
the password if it has one, subject to the usual `Player` and ban rules. There is no invite-only mode, and an
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

A round is decided once, automatically, as soon as it has ended and every awaited player has a score, or after `ServerSettings.Multiplayer.Rounds.ScoreWait` (10 seconds by default) with the scores that came in (the wait is total, not per player). A round with no participants is not decided, and an aborted round is never decided. BasilBot announces the end of the round only when someone completed, then either that all scores arrived or the names of the players still missing, in the room channel. A room that closes while its round waits decides it at once. The decision (`RoundResult`) is stored when it is decided and restored as stored when the round is loaded, never decided again, so a later change of the rules leaves old decisions as they were. The report only reads the stored decision; it never decides and never writes.

## Countdown

The room countdown belongs to BasilBot, not to the room. A host (the `!mp start <seconds>` and `!mp timer` commands, or the API and console commands of the same name) asks for a countdown with `StartRoomCountdown` and cancels it with `CancelRoomCountdown`; both are answered by BasilBot. It needs the right to start rounds, which BasilBot asks the room about with `CheckRoundStart` for the connection that asked, so a countdown can be started by whoever may start a round and by nobody else. A room has at most one countdown; starting another replaces it and says how much of the old one was left.

BasilBot keeps the timer and announces the marks in the room channel: a timer announces 60, 30, 10 and 5 seconds, a countdown that starts a round announces every whole minute left and then 30, 10, 5, 4 and 3 seconds, and a mark is announced only while it is shorter than the countdown. When a countdown that starts a round elapses, BasilBot sends the same `StartRound` message a person would, with the connection that started the countdown; if that connection is gone or no longer has the right, the countdown is cancelled with a message instead.

BasilBot listens to the room's events to cancel a pending countdown: the room closing, a round starting or being aborted, and, for a countdown that starts a round, a change of the beatmap, mode, mods, teams or win condition. The room knows nothing of countdowns: it only starts, aborts and decides rounds. `RoomPrivileges` has no countdown privilege, so the bit it once used stays unused and is never reassigned.

```text
!mp start 30 / API start --StartRoomCountdown--> BasilBot (rights check, timer)
                                                   | marks            -> room channel
                                                   | elapsed          --StartRound--> Room
Room events (closed, round started/aborted, settings changed) -------> BasilBot cancels
```

## Room lifecycle and concurrency

The lobby opens and closes rooms. "Tournament Room" is no longer a concept of the lifecycle: every room follows the same two limits. An empty room remains open for 15 minutes; the lobby announces the scheduled close when it starts and five minutes before it, and a player joining cancels it. If the room becomes empty again, the delay starts over. Each creator may have a limited number of rooms open at once. What still differs is the **source** that opens the room: opening in game needs the creator's own osu! client (or refuses with `AlreadyInRoom`), while opening through the API or BasilBot does not. The osu!tourney client and observers remain a connection type, not a kind of room.

The empty-room close is a background worker, not a timer of the lobby: the lobby records when a room became empty and the worker asks it every 30 seconds to close the rooms that stayed empty for 15 minutes, and to announce the closing of those five minutes from it. The lobby's `CloseEmptyRooms` needs no rights; the room closes as if it closed itself, with no user as actor. The per-creator limit (`Tournament` lifts it) counts every room the creator has open, whichever source opened it.

Room state is mutable and shared. Every transition that reads and then mutates is a synchronous method of `Room` (`Join`, `Kick`, `StartRound`, `Configure` and the rest) that takes the room's own lock, checks the state, changes it and publishes its events before releasing the lock; publishing only queues the event, so nothing is awaited under the lock. A closed room answers `RoomClosed`. `ScopedRoom` only checks the caller's privileges, does any asynchronous lookup that must come first, and calls the room's method. Never hold two room locks at once. Do not add another synchronization mechanism for room state.

A player is in at most one room. A room's own check cannot see another room's seating that is still under way, so the shell of a join claims the player through `Lobby.TryClaim` before calling the room and releases the claim afterwards; a second room that meets a claim answers `InAnotherRoom`.

## Invariants

* The creator, referees and host are separate roles; host status alone grants no management authority.
* Caller-authorized behavior is reached through `room.ScopeTo(by)` and uses effective privileges.
* A scoped room is short-lived; its role and room-privilege snapshot is for one request or command.
* Every room state transition that reads and mutates runs inside one method of `Room` under the room's lock.
* The 16 positions are numbered 1–16; room size controls usable positions, not the highest slot number.
* The next host is selected by slot order.
* Round numbers start at 1, and an aborted round keeps its number.
* The latest round remains available until the next one starts; a score is taken for it by the player, the beatmap and the round's state, never by the score module.
* A closed room's match history remains; a round's decision is stored when it is decided and reports read it.
* Performance points do not affect multiplayer rules or match winners.

## Related code

* [`Room.cs`](../../src/Application/Multiplayer/Objects/Rooms/Room.cs): live room and `ScopeTo`
* [`ScopedRoom.cs`](../../src/Application/Multiplayer/Capabilities/Rooms/ScopedRoom.cs): caller, role and room-privilege scope
* [`ScopedRoomMembers.cs`](../../src/Application/Multiplayer/Capabilities/Rooms/ScopedRoomMembers.cs), [`ScopedRoomSlots.cs`](../../src/Application/Multiplayer/Capabilities/Rooms/ScopedRoomSlots.cs), [`ScopedRoomRounds.cs`](../../src/Application/Multiplayer/Capabilities/Rooms/ScopedRoomRounds.cs), [`ScopedRoomSettings.cs`](../../src/Application/Multiplayer/Capabilities/Rooms/ScopedRoomSettings.cs), [`ScopedRoomAuthority.cs`](../../src/Application/Multiplayer/Capabilities/Rooms/ScopedRoomAuthority.cs): caller-authorized room parts
* [`Lobby.cs`](../../src/Application/Multiplayer/Registry/Lobby.cs), [`Room.cs`](../../src/Application/Multiplayer/Objects/Rooms/Room.cs): system-owned room operations
* [`RoomPrivileges.cs`](../../src/Domain/Multiplayer/RoomPrivileges.cs): room privileges and the role groups
* [`CheckRoundStart.cs`](../../src/Application/Multiplayer/Messages/CheckRoundStart.cs): asks whether a connection may start rounds, without starting one
* [`StartRoomCountdown.cs`](../../src/Application/Multiplayer/Messages/StartRoomCountdown.cs), [`CancelRoomCountdown.cs`](../../src/Application/Multiplayer/Messages/CancelRoomCountdown.cs): the countdown requests, answered by BasilBot
* [`RoomCountdowns.cs`](../../src/Infrastructure/Bot/Workers/RoomCountdowns.cs), [`RoomCountdown.cs`](../../src/Application/Bot/Objects/RoomCountdown.cs): the countdown timers and their marks
* [`Lobby.cs`](../../src/Application/Multiplayer/Registry/Lobby.cs), [`ScopedLobby.cs`](../../src/Application/Multiplayer/Capabilities/ScopedLobby.cs): lobby-owned lifetime and caller-scoped operations

## See also

* [`chat.md`](chat.md): channel ownership and room chat
* [`privileges.md`](privileges.md): account privileges and room-manager rights
* [`sse.md`](sse.md): live-stream design
* [`database.md`](database.md): match history persistence
