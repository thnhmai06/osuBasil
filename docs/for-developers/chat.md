# Chat & commands

## Overview

Basil supports chat through two transports:

* osu! client Bancho packets;
* native IRC connections.

Both transports act on the same chat model in `Basil.Application`: the same channels, the same rules
for who may read and write, and the same events. BasilBot takes part through the same model as a
normal user and provides tournament commands, including `!mp`.

> **Pending migration.** The chat model below is the reworked `Basil.Application`. The transports, the
> command dispatcher and BasilBot live outside Application and have not been migrated to it yet; the
> sections about them are marked and describe the intended behaviour, which is a user-visible contract.

## Users, sessions and connections

A user who is online is one `UserSession`. Each place they are logged in from is a `Connection`:

| Connection | Client | Per user | Holds |
|---|---|---|---|
| `BanchoConnection` | osu! game client | one | presence status, last activity, UTC offset, its spectator channel |
| `TourneyConnection` | osu!tourney client | several | nothing of its own |
| `IrcConnection` | IRC client | one | last activity |
| `BotConnection` | BasilBot | one | nothing of its own |

The session holds what is shared by all of a user's clients: the away message and the user's
private-message channel.

`UserRegistry` is the only object that opens and closes connections. It announces `UserConnectionOpened`
(saying whether the user just came online) and `UserConnectionClosed` (saying whether the user went
offline). Other objects react to those events; the session and its connections hold no channels,
rooms or other relations, and emit no events.

### Logging in again

A user can hold one connection of each kind except osu!tourney. When a second connection of the same
kind logs in:

* if the old one has been idle for at least 10 seconds (`UserRegistry.ReplaceAfterIdle`), the old
  connection is closed with reason `Replaced` and the new one opens;
* otherwise the new login is refused with `AlreadyOnline`.

osu!tourney logins need the Player and Supporter privileges. An osu! client sends a logout right after
logging in, so a logout received within one second of an osu! client's login is ignored.

Connections, sessions and channels compare by reference: a connection is one login, a session is one
period online. An old object carried by a late clean-up can therefore never be mistaken for its
replacement.

## Channels

### Kinds and owners

A channel has a Domain model (`Channel`: name and topic) and a runtime model
(`ChannelSession`: members and events). Each kind is managed by whoever owns it:

| Kind | Domain / runtime | Name | Owner, opened and closed with it |
|---|---|---|---|
| General | `GeneralChannel` / `GeneralChannelSession` | configured (`#osu`, `#lobby`, …) | `GeneralChannelRegistry` |
| Room | `RoomChannel` / `RoomChannelSession` | `#mp_{room id}` | the `Room` |
| Spectator | `SpectatorChannel` / `SpectatorChannelSession` | `#spec_{host user id}` | the spectated `BanchoConnection` |
| Private message | `PmChannel` / `PmChannelSession` | the owner's name | the recipient's `UserSession` |

`GeneralChannelRegistry` lists only general channels. A room channel is found through its room
(`lobby.Find(id)?.Channel`); spectator and private-message channels are never looked up by name.

A channel's name tells its kind, as in IRC: a channel several users take part in is named `#name`,
and a private-message channel carries its owner's name without `#`. Each kind of channel follows
this convention when it names itself. Transports resolve the aliases `#multiplayer` and `#spectator`
to the sender's room or spectator channel. A room channel's topic is the room's name and follows it
when the room is renamed.

### Who may read and write

Access follows osu!: a user may join a channel, including with `/join` over IRC, only if an osu!
client doing the matching in-game action would have access.

| Kind | May read | May write |
|---|---|---|
| General | users holding every bit of the channel's read privileges | users holding every bit of its write privileges |
| Room | seated players, osu!tourney observers, the creator, the referees, BasilBot | same as read |
| Spectator | the spectated player and their spectators | same as read |
| Private message | the owner's connections, except osu!tourney | any open connection |

A privilege requirement is met only when the user holds every bit it sets; an empty requirement is
always met.

### Joining and leaving

`channel.Join(by)` adds a connection when it may read the channel. If the same user already has a
member connection of the same kind (osu!tourney excepted), the channel checks that connection: while it
is open the join is refused with `AlreadyMember`; once it is closed, it is removed as an ordinary leave
and the new connection joins. A closed channel refuses joins.

`GeneralChannelRegistry.JoinAutoChannels` joins a connection to every auto-join channel it may read, and
`GeneralChannelRegistry.PartAll` removes a connection from every general channel; both are called when
a connection opens or closes and are safe to call again. A channel named `#lobby` is an ordinary general
channel: whether it exists and whether it is joined automatically is configuration, and it has no tie
to the multiplayer `Lobby`.

### Posting

`channel.Post(by, text)` refuses, in this order:

| Result | When |
|---|---|
| `Closed` | the channel is closed |
| `Silenced` | the sender is silenced |
| `Empty` | the text is empty or whitespace |
| `NotMember` | the sender is not a member (private-message channels do not require membership) |
| `NoWritePermission` | the sender may not write to the channel |
| `TargetSilenced` | the recipient of a private message is silenced |

A message longer than 2,000 characters is cut, and the `ChannelMessagePosted` event says so.

### Private messages and away replies

A private message is a post into the recipient's private-message channel, so it reaches every
connection of the recipient except osu!tourney clients. If the recipient has an away message, the
sender receives it as a message from the recipient in the sender's own private-message channel.

### Spectating

Spectating is membership of the spectated player's spectator channel. The first spectator brings the
player into the channel and the last one to leave takes them out. A spectator watches one player at a
time: to switch, the caller stops spectating the old player before spectating the new one
(`presence.Watching(by)` finds the current one). When the spectated player's connection closes, the
channel closes and every spectator leaves it. Replay frames are relayed to the channel's members and
are not events.

### Events

```text
ChannelEvent
├── ChannelOpened, ChannelClosed
├── ChannelMembershipEvent
│   ├── ChannelMemberJoined, ChannelMemberParted (kicked when the owner closed the channel)
│   └── ChannelSpectatorJoined, ChannelSpectatorLeft (spectator channels)
├── ChannelMessageEvent
│   └── ChannelMessagePosted
└── ChannelSpectatorCantSpectate
```

Each event comes from the channel that was acted on, never from the sender's session.

## IRC authentication

> **Pending migration.** The IRC host still uses the previous session model.

IRC authentication uses the same account password as osu! client authentication; there is no separate
IRC password.

```text
TCP connection
      │
      ▼
PASS + NICK + USER
      │
      ▼
Gateway.ConnectAsync (IRC)
      │
      ├── validate account credentials
      └── UserRegistry opens an IrcConnection ── UserConnectionOpened
                                                │
                                                ▼
                               auto-join channels, welcome numerics
```

An IRC connection has no game client: it takes part in chat and commands only and can never occupy a
multiplayer slot.

## Commands

> **Pending migration.** Command detection and dispatch are being rebuilt outside Application. The
> rules below are the user-visible behaviour they keep.

The command prefix is `!` by default. A message beginning with the prefix is a command, whichever
transport it came from. A direct message to BasilBot is always a command, even without the prefix: a
message addressed to the bot has already established the sender's intent. Unrecognized top-level
commands stay silent, so BasilBot does not answer arbitrary text sent to it.

General commands use the common command table; `!mp` commands act on a room through the room's own
operations, so the room checks permissions itself.

### `!mp` roles

`!mp` is available only to the room's managers: its creator and its referees. Being the host does not
grant `!mp` rights. Only the creator may add or remove referees (`!mp addref`, `!mp removeref`), and
the creator is not part of the referee list. See [`multiplayer.md`](multiplayer.md) for the roles and
the generated BasilBot Commands reference at `api.<domain>/docs/basil-bot/` for every command.

### Command scope

A command may be associated with a specific room.

* Issued in the room's own channel, the reply is posted publicly there.
* Issued anywhere else (`#osu`, `#lobby`, another shared channel, a direct message), the reply goes to
  the sender by direct message, so room-specific output never leaks into shared channels. When
  appropriate, the room still receives an unprefixed copy so referees working on the room remotely stay
  aware of it.

`!mp in` lets a referee scope commands to a room without being in its channel. It is accepted only in
direct messages, because using it in a public channel would expose the sender's room to everyone there.

### Failed commands

A recognized command that fails validation or permission checks produces an error reply, following the
same scope rules: public in the room's channel, by direct message elsewhere. Unrecognized top-level
commands stay silent.

## BasilBot

> **Pending migration.** BasilBot is being rebuilt on the new model.

BasilBot is a permanent `UserSession` holding a `BotConnection`, opened through `UserRegistry` when the
server starts and never closed. Because it is a normal connection, it posts through the same
`channel.Post` as any user, and its replies reach Bancho and IRC users alike. It joins every room's
channel when the room opens and receives private messages through its own private-message channel.

## Related code

* [`Basil.Application/Chat/ChannelSession.cs`](../../src/Basil.Application/Chat/ChannelSession.cs): membership and posting rules shared by every channel
* [`Basil.Application/Chat/GeneralChannelRegistry.cs`](../../src/Basil.Application/Chat/GeneralChannelRegistry.cs): configured channels and auto-join
* [`Basil.Application/Chat/GeneralChannelSession.cs`](../../src/Basil.Application/Chat/GeneralChannelSession.cs), [`PmChannelSession.cs`](../../src/Basil.Application/Chat/PmChannelSession.cs): general and private-message channels
* [`Basil.Application/Multiplayer/RoomChannelSession.cs`](../../src/Basil.Application/Multiplayer/RoomChannelSession.cs): room channels
* [`Basil.Application/Sessions/SpectatorChannelSession.cs`](../../src/Basil.Application/Sessions/SpectatorChannelSession.cs): spectating
* [`Basil.Application/Sessions/UserRegistry.cs`](../../src/Basil.Application/Sessions/UserRegistry.cs), [`Gateway.cs`](../../src/Basil.Application/Sessions/Gateway.cs): logging in and out
* [`Basil.Application/Sessions/UserSession.cs`](../../src/Basil.Application/Sessions/UserSession.cs), [`Connection.cs`](../../src/Basil.Application/Sessions/Connection.cs): sessions and connections
* [`Basil.Domain/Chat/`](../../src/Basil.Domain/Chat): channel models and `Message`

## See also

* [`bancho.md`](bancho.md): how Bancho packets enter the chat model
* [`irc.md`](irc.md): the IRC transport
* [`multiplayer.md`](multiplayer.md): rooms, roles and room channels
* [`working-scopes.md`](working-scopes.md): chat features and `!mp` commands in or out of scope
* BasilBot Commands (`api.<domain>/docs/basil-bot/`): generated command reference
