# IRC transport

> **Status: the IRC host is pending migration** (see [`plans/handoff-20261022.md`](../../plans/handoff-20261022.md)).
> `src/Hosts/Irc` still uses the previous Application API and is outside the solution build. This page describes the
> **design the host must implement on the current Application model**. The Application behaviour it relies on exists
> today (`Basil.Application.Runtime`); the wire-level rules are the contract the migrated host must keep.
> Sections marked "host rule" are not implemented against the new model yet.

## Overview

### Why

Basil's chat is one model shared by every transport. IRC exists so that referees and operators can use standard IRC
clients, bouncers and bots next to (or instead of) the osu! stable client: read chat, run BasilBot commands and
manage rooms without a game client.

The IRC transport therefore adds no rules of its own. It decodes IRC lines, calls the Application operation of the
object being acted on with the IRC connection as the actor, and encodes the typed result as IRC lines. Authority is
decided by the user's effective permissions, never by the fact that the request came over IRC.

### What

* A user can hold a Bancho connection **and** an IRC connection at the same time. Neither evicts the other.
* The IRC connection is a full chat participant: it joins channels, sends and receives messages, runs BasilBot
  commands, and referees or creates rooms to the extent the user's permissions allow.
* The IRC connection is never a player: it cannot be seated in a room slot and cannot spectate.

### Messages and line breaks

A chat message may contain CR and LF. `Message.Content` accepts any text that is not empty and holds no control
character other than tab, CR, LF and U+0001 (the action wrapper); only empty text or other control characters are
refused. An IRC line cannot carry CR or LF, and `IrcMessage.ToString` throws on them. **Host rule:** before writing a
message to the wire, the IRC host splits it at line breaks and sends one `PRIVMSG`/`NOTICE` per non-empty line (or
escapes the breaks). This applies to every message it delivers: chat, BasilBot replies (multi-line output such as
`!mp settings` or `!mp listrefs`) and away replies.

## Connection model

### Public contract

```text
User (one account, online)
├── BanchoConnection   osu! stable client       (at most one)
├── IrcConnection      IRC client or bouncer    (at most one)
├── BotConnection      the server's own, BasilBot only
├── TourneyConnection  osu!tourney clients      (several)
└── ApiConnection      HTTP API clients         (several)
```

| Statement | Enforced by |
| --- | --- |
| A user may be online through Bancho and IRC at once | `User` holds connections by kind; `UserRegistry.Open` only compares connections of the same kind |
| A second IRC login while the first IRC connection was active within the last 10 seconds is refused (`AlreadyOnline`); an older idle one is closed as `Replaced` | `UserRegistry.Open` |
| An IRC connection is closed after 300 seconds without traffic | `UserRegistry.CloseIdle` and `MarkActive` |
| Only a `BanchoConnection` can be seated in a slot or hold host | the slot, `Room.Seat`, `SeatAsync` and `SetHostAsync` take `BanchoConnection`; `ScopedRoomMembers.JoinAsync` and `LeaveAsync` answer `NotInRoom` for any other connection |
| An IRC connection cannot spectate | `ScopedBanchoConnection.SpectateAsync` throws `ArgumentException` for an `IrcConnection`; the host must not call it |
| A room opened in game needs the creator's `BanchoConnection`; a tournament room can be created by any connection with `RoomCreate` | `ScopedLobby.OpenRoomAsync` throws `ArgumentException` for a non-tournament room opened by another kind |
| BasilBot is the user with id 1 and its connection is a `BotConnection`, not a client; nobody can log in as it | `UserRegistry.EnsureBotAsync`, `ConnectBotAsync`; the login lookup answers `UnknownUser` for it |

The previous design enforced "an IRC client is not a player" with separate `GameSession` and `IrcSession` types. The
current code enforces it through the parameter types of the seating and host operations (`BanchoConnection`) and
through the results of the operations that take a plain `Connection`, not through a session type hierarchy.

### Authority

Every action has a real actor: the `IrcConnection` that sent the line. Objects are scoped to it
(`room.ScopeTo(by)`, `channel.ScopeTo(by)`, `user.ScopeTo(by)`), and the scope checks the user's **effective**
permissions at that moment. A silenced referee therefore cannot manage a room from IRC either. The transport never
adds or removes rights.

What an IRC connection can do:

* join channels it may read and post to channels it may write (`ScopedChannel.Join`, `Part`, `PostAsync`);
* send private messages, including to BasilBot, and set or clear its user's away message (`ScopedUser.SetAway`);
* run BasilBot commands: the bot runs them with the typist's connection and permissions, so `!mp` from IRC uses the
  IRC connection's rights;
* referee a room (kick, ban, referees, settings) when the user is its creator or a referee, or holds
  `RoomManageAny`;
* create a **tournament** room (`RoomCreate`). The creator's Bancho client is seated as host only if the user has an
  open one that may join rooms and is in no room; otherwise the room starts with a creator and no host.

What it cannot do: be seated, hold host, spectate, open a room in game.

### Login and logout

Registration (`PASS`, `NICK`) maps to `UserRegistry.LoginAsync` with `ConnectionType.Irc` and no osu! client. A
failure (`UnknownUser`, `WrongPassword`, `AccountDeleted`, `AlreadyOnline`) is answered with a registration error
numeric. A success opens the connection, which joins its user's private-message channel and, through the connection
handler, the auto-join general channels.

Closing the socket, `QUIT`, a write failure or the idle sweep closes the connection with `UserRegistry.Close`. The
user goes offline only when this was the last connection. Closing an IRC connection does not touch the user's Bancho
connection, its room seat or its spectators, and closing a Bancho connection does not disconnect IRC.

## Wire protocol

`Basil.Protocol.Irc` only turns lines into `IrcMessage` records, numerics and user prefixes and back. It gives no
meaning and does not limit line length; the host keeps lines within the protocol limit. A line holding CR, LF or NUL
is rejected on parse and on format.

### Commands

| Command | Behaviour |
| --- | --- |
| `PASS` / `NICK` / `USER` | Registration needs `PASS` and `NICK`; `USER` is accepted and ignored. Registration does not wait for `CAP`. Before registration any other command gets `451`. |
| `CAP` | No capability is offered: `LS`/`LIST` return an empty list, `REQ` is refused with `NAK`, `END` needs no reply. SASL `AUTHENTICATE` is never offered and gets no reply. |
| `PRIVMSG` / `NOTICE` | Posts through `ScopedChannel.PostAsync`. A `NOTICE` never triggers a command or an away reply. A channel target that does not exist gets `403`; one the connection cannot write to gets `404`. |
| `JOIN` / `PART` | `ScopedChannel.Join` / `Part`. Unknown channel `403`; not allowed to read it `473`. |
| `LIST` / `NAMES` / `WHO` / `WHOIS` / `ISON` | Read-only queries. A channel the connection cannot read is not listed. |
| `TOPIC` / `MODE` | Read only; any change is refused. User modes do not exist. |
| `MOTD` / `VERSION` / `TIME` / `LUSERS` | Informational replies. |
| `AWAY` | Sets or clears the user's away message, which all of the user's connections share. |
| `PING` / `PONG` | `PING` is answered with `PONG` and the same token. The host sends a keepalive `PING` periodically and treats every received line as activity (`MarkActive`). |
| `QUIT` | Closes the connection. |

Missing parameters get `461`. After registration `NICK`, `PASS` and `USER` get `462`: identity is fixed at login. Any
other command gets `421`; nothing is silently ignored except the protocol exceptions above.

A `PRIVMSG` to a nickname is a post into that user's private-message channel. An unknown nickname gets `401`; a
refusal by the channel (`Blocked`, `TargetSilenced`, `AccessDenied`) is answered with the matching numeric, and
`Empty`/`InvalidText` are dropped.

### What IRC cannot change

Nickname, topics, channel modes, the password and the `USER` data are server-controlled. Presence is the only thing a
client changes: joining, parting, chatting and `AWAY`.

### Channel names

| Channel | IRC name | Notes |
| --- | --- | --- |
| General | configured name, such as `#osu` | read and write rights come from the channel's configured privileges |
| Room | `#mp_{room id}` | opens and closes with the room |
| Spectator | `#spec_{user id}` | only the spectated Bancho client and its spectators can read it, so an IRC connection cannot join it |
| Private message | the user's name, no `#` | every connection of the user is in it, osu!tourney excluded |

### Delivery

A posted message goes to `Channel.RecipientsAsync(posted)`: the members who do not block the author, unless the author
has `ChatMessageAnyone`. **Host rule:** the host writes the line to every IRC connection among them except the one
that sent it (IRC does not echo). The user's other connection, such as its Bancho client, is a recipient like any
other member. If the event carries an away reply, it goes to the author's private-message channel.

## Rooms on IRC

### Room channel access

A room channel can be read by a connection that takes part in the room: a seated Bancho player, an observing
osu!tourney client, the creator or a referee whose permissions include `RoomJoin`, or an account with
`RoomManageAny` or `RoomObserve`. Writing needs the same involvement or `ChatPostAnyRoom`.

```text
JOIN #mp_5     -> 473  the room exists, the caller may not enter
JOIN #mp_999   -> 403  there is no such channel
```

The existence of a room is not hidden, but an unauthorized connection can neither join it nor see it in `LIST`.
**Host rule:** `LIST` lists only the room channels the connection could join.

A creator or referee joins the room channel from IRC with an ordinary `JOIN`. They stay in it after leaving a slot,
because leaving a seat parts the channel only for users who do not manage the room. Players are placed in the
channel by the room when they are seated.

### Status prefixes (host rule)

Prefixes are computed per channel, not from a global privilege: `@` for authority in that channel, `+` for a member who
may write, none for read-only. In a room channel authority belongs to the creator and the referees
(`Room.Authority.IsManagedBy`), so a referee can be `@` in `#mp_5` and unprefixed in `#osu`. `NAMES` and `WHO` for a
channel use this rule; `WHO` for a bare nickname has no channel and shows no prefix. How `@` is derived for general
channels is not fixed by the Application model and is decided when the host is ported.

### Host, creator and protection

* A room created from IRC starts with a creator and no seated player, and possibly no host. An empty tournament room
  closes after 15 minutes (with a warning 5 minutes before), whichever transport created it.
* The creator is not in the referee list and ranks above referees. The creator and the referees cannot be kicked or
  banned (`IsManager`); removing a referee is the explicit `!mp removeref`.
* A ban is a rule on the account, not on a connection, so banning an offline user works. A kick removes the target's
  seated Bancho connection; an IRC-only user is not seated, so a kick answers `NotInRoom`.
* BasilBot cannot be the target of account operations (`UserOutcome.Protected`).

## Presence on IRC (host rule)

Channel membership in the Application is per connection: a user with a Bancho and an IRC connection has two members
in `#osu`. Other IRC clients should see the user, not the connection, so the host derives `JOIN`, `PART` and `QUIT`
from `ChannelMemberJoined`, `ChannelMemberParted` and `UserConnectionClosed` (`WentOffline`) with the rule of the
previous design:

```text
a connection joins a channel
  the user already has another connection in it?   no line for other clients
  otherwise                                        JOIN to the channel's IRC members

a connection leaves or closes
  the user keeps another connection in the channel?        nothing
  else the user still has a connection somewhere else?     PART from this channel
  else (the user went offline)                             one QUIT to everyone sharing a channel
```

A client never receives both `PART` and `QUIT` for one disconnect. The joining connection still receives its own
`JOIN` and `NAMES` replies. The Bancho client has its own channel packets and does not depend on these lines.

## Design rationale

* **One model, thin transport.** Rules live on the acted-on object and see the actor's permissions, so a rule is added
  once and holds on every transport.
* **Connections instead of session subtypes.** Gameplay state lives on `BanchoConnection` and the room seat; what all
  connections share (identity, permissions, away message) lives on `User`; Chat keeps a private-message channel per
  online user.
* **BasilBot is a user.** The bot is account 1 with a `BotConnection`, so it appears in `WHO`, `NAMES` and `WHOIS`
  like any user. It has no socket, no idle timeout and no login, and it is not a player.

## Related code

* [`Connection.cs`](../../src/Application/Runtime/Connections/Connection.cs): connection kinds, including `IrcConnection` and `BotConnection`
* [`ConnectionRules.cs`](../../src/Application/Runtime/Connections/ConnectionRules.cs): one-per-kind rule and idle timeouts
* [`UserEvents.cs`](../../src/Application/Runtime/Users/UserEvents.cs): connection opened and closed events
* [`User.cs`](../../src/Application/Runtime/Users/User.cs), [`ScopedUser.cs`](../../src/Application/Runtime/Users/ScopedUser.cs): online user, away message
* [`UserRegistry.cs`](../../src/Application/Runtime/Users/UserRegistry.cs): login, close, idle sweep, BasilBot's connection
* [`Channel.cs`](../../src/Application/Runtime/Chat/Channel.cs), [`ScopedChannel.cs`](../../src/Application/Runtime/Chat/ScopedChannel.cs): membership, posting, recipients
* [`RoomChannel.cs`](../../src/Application/Runtime/Multiplayer/Rooms/RoomChannel.cs), [`Room.Seating.cs`](../../src/Application/Runtime/Multiplayer/Rooms/Room.Seating.cs): room channel access and seating
* [`ScopedRoomMembers.cs`](../../src/Application/Runtime/Multiplayer/Rooms/ScopedRoomMembers.cs), [`ScopedRoomAuthority.cs`](../../src/Application/Runtime/Multiplayer/Rooms/ScopedRoomAuthority.cs): kick, ban, referees, host
* [`ScopedLobby.cs`](../../src/Application/Runtime/Multiplayer/ScopedLobby.cs): room creation
* [`Message.cs`](../../src/Domain/Chat/Message.cs): message content rules
* [`IrcMessage.cs`](../../src/Protocol/Irc/IrcMessage.cs), [`IrcNumericReply.cs`](../../src/Protocol/Irc/IrcNumericReply.cs), [`IrcUserPrefix.cs`](../../src/Protocol/Irc/IrcUserPrefix.cs): wire format
* [`src/Hosts/Irc`](../../src/Hosts/Irc): listener and connection loop, pending migration

## See also

* [`chat.md`](chat.md): the shared chat model used by Bancho and IRC
* [`multiplayer.md`](multiplayer.md): room authority, seating and the empty-room timer
* [`privileges.md`](privileges.md): permissions and restrictions
* [`../for-client/irc-client.md`](../for-client/irc-client.md): how a user connects an IRC client
* [`../for-technicians/https.md`](../for-technicians/https.md): why IRC does not use HTTPS/TLS
