# IRC and the dual-session model

## Overview

An account can have up to two independent live sessions:

* a `GameSession` from an osu! stable client;
* an `IrcSession` from a native IRC client or bouncer.

These are deliberately different session types.

A game client carries gameplay state such as multiplayer membership, spectating, status, statistics, and the Bancho packet queue. An IRC client has none of that state and must never be treated as a multiplayer player.

Both session types share `UserSession` for identity and chat-related state.

The separation is enforced by the type system rather than by a runtime `IsIrc` flag.

## Session model

```text
UserSession
├── GameSession
│   ├── gameplay state
│   ├── multiplayer membership
│   ├── spectating
│   ├── player status
│   ├── per-mode statistics
│   └── BanchoIrcBridgeConnection
│
└── IrcSession
    └── real IRC connection
```

### `UserSession`

`UserSession` is the shared base for both session types.

It contains state that is meaningful for any connected account:

* user id;
* name;
* privileges;
* silence state;
* channel membership;
* `IIrcConnection`.

Chat is therefore expressed in terms of the shared session abstraction, while gameplay operations require the more specific `GameSession` type.

### `GameSession`

`GameSession` represents an osu! client connection.

It owns gameplay-specific state, including:

* Bancho packet queue;
* `Match`;
* `Spectating`;
* `Spectators`;
* player `Status`;
* per-mode statistics.

Only a `GameSession` can occupy a multiplayer slot.

Match-seating methods such as `JoinAsync`, `ForceJoinAsync`, and `OccupySlot` therefore accept `GameSession` rather than `UserSession`.

This is intentional type safety:

```text
GameSession ──► JoinAsync(...)
     │
     └── valid

IrcSession ──► JoinAsync(...)
     │
     └── does not compile
```

### `IrcSession`

`IrcSession` represents a native IRC connection.

It contains no gameplay state.

It owns a real `IIrcConnection` backed by the TCP connection accepted by the IRC gateway.

An IRC session can:

* join channels;
* send and receive chat;
* run commands;
* referee matches;
* create matches;
* appear in match-related IRC output.

It cannot become a seated multiplayer player.

## Session coexistence

An account may have:

* no session;
* only a `GameSession`;
* only an `IrcSession`;
* both simultaneously.

Logging in through one transport never evicts the other session.

```text
UserId 42
│
├── GameSession ─── osu! client
│
└── IrcSession  ─── IRC client
```

The two sessions share the same account identity but remain independent connections.

This is important for tournament workflows where a referee may play through osu! while simultaneously connecting through IRC for remote administration.

## Session registries

The two session types have separate typed registries:

```text
ISessionRegistry<GameSession>
ISessionRegistry<IrcSession>
```

A lookup is therefore explicit about what kind of session the caller expects.

For example:

```text
gameRegistry.GetByUserId(id)
ircRegistry.GetByUserId(id)
```

Consumers that intentionally accept either session type can combine the registries explicitly:

```text
GameSession? game = gameRegistry.GetByUserId(id);
IrcSession? irc = ircRegistry.GetByUserId(id);
```

There is no ambiguous `GetSessionByUserId` returning an arbitrary session.

Registration is atomic through `TryAdd`, so concurrent logins of the same session type cannot race through a check-then-add sequence.

## BasilBot

BasilBot is represented as a synthetic `GameSession`.

It is not an `IrcSession` and does not establish a TCP connection.

The current implementation uses `GameSession` because spectator functionality expects `GameSession` state such as `Spectating` and `Spectators`.

This allows `SpectatorService` to expose BasilBot through the same SSE spectator path used for real players.

This is an implementation compatibility shape, not a claim that BasilBot is an osu! client.

BasilBot is also exempt from the idle-disconnect sweep because it has no real client connection that can send keepalive traffic.

## IRC connection model

Every `UserSession` has an `IIrcConnection`.

The implementation depends on the session type.

### Bancho clients

A `GameSession` uses `BanchoIrcBridgeConnection`.

This translates IRC-shaped chat operations into Bancho packets:

```text
IRC-shaped PRIVMSG
       │
       ▼
BanchoIrcBridgeConnection
       │
       ▼
Bancho SEND_MESSAGE
       │
       ▼
osu! client
```

The bridge does not emulate a full IRC connection.

Operations that have no Bancho equivalent, such as `JOIN`, `PART`, and `QUIT`, are not sent through the bridge. The osu! client receives channel membership through Bancho `ChannelInfo` packets instead.

### Native IRC clients

An `IrcSession` uses the actual TCP-backed IRC connection.

The IRC gateway is embedded directly in the Basil process. It does not require a separate service or container.

```text
IRC client
    │
    │ TCP
    ▼
TcpIrcListener
    │
    ▼
IrcSession
```

## IRC protocol surface

The IRC gateway implements the subset required by standard IRC clients and Basil's chat model.

| Command                                | Parameters              | Behavior                                                                                                 |
| -------------------------------------- | ----------------------- | -------------------------------------------------------------------------------------------------------- |
| `PASS` / `NICK` / `USER`               | password, nick          | Registration handshake. `USER` is accepted and ignored; `PASS` and `NICK` authenticate the account.      |
| `CAP`                                  | subcommand              | No capabilities are offered. Capability negotiation is refused; `CAP END` requires no response.          |
| `PRIVMSG`                              | target, text            | Routes the message through the shared chat system. A command prefix therefore invokes BasilBot commands. |
| `NOTICE`                               | target, text            | Uses normal message delivery but never invokes commands or away replies.                                 |
| `JOIN` / `PART`                        | channel                 | Joins or leaves a channel and returns the appropriate membership responses.                              |
| `LIST`                                 | optional channel        | Lists channels visible to the caller. Match-room visibility is permission-gated.                         |
| `NAMES`                                | optional channel        | Returns channel membership, or visible channels when no channel is specified.                            |
| `TOPIC`                                | channel, optional topic | Reads the topic. Topic changes are refused.                                                              |
| `MODE`                                 | channel, optional mode  | Reads channel modes. Mode changes are refused. Nick targets are ignored because Basil has no user modes. |
| `WHO`                                  | channel or nick         | Returns matching users.                                                                                  |
| `WHOIS`                                | nick                    | Returns hostmask, visible channels, away state, idle time, and sign-on time.                             |
| `ISON`                                 | nicks                   | Reports which supplied nicknames are online using their stored spelling.                                 |
| `MOTD` / `VERSION` / `TIME` / `LUSERS` | none                    | Returns the MOTD, gateway version, server time, and online counts.                                       |
| `AWAY`                                 | optional message        | Sets or clears the caller's away message.                                                                |
| `PING`                                 | optional token          | Returns `PONG` with the same token.                                                                      |
| `QUIT`                                 | none                    | Closes the connection and runs normal logout handling.                                                   |

Unknown commands are refused rather than silently ignored.

Before registration, unsupported commands receive the appropriate not-registered error. After registration, they receive an unknown-command error.

`CAP`, `PONG`, and `AUTHENTICATE` are protocol exceptions: `CAP` participates in capability negotiation, `PONG` completes the client's keepalive exchange, and SASL `AUTHENTICATE` is accepted only as an unsupported authentication flow rather than being offered by the server.

## IRC mutability

IRC commands do not provide administrative control over Basil's channel configuration.

The client may change only its own presence state.

Allowed mutations include:

* joining;
* parting;
* sending chat;
* setting or clearing `AWAY`.

The following are server-controlled and cannot be changed through IRC:

* nickname;
* channel topics;
* channel modes;
* account password;
* `USER` registration data.

For example:

```text
TOPIC #osu :new topic
MODE #osu +m
NICK another-name
```

are refused.

## Channel status prefixes

IRC status prefixes are calculated per channel.

They do not represent a user's global privilege.

For a particular channel:

* `@` represents channel authority;
* `+` represents a member allowed to write;
* no prefix represents a member who may only read.

For normal channels, channel authority comes from staff privileges.

For a match room, channel authority comes from referee status.

Therefore, a referee may have `@` in the match channel while appearing without `@` in `#osu`.

The same channel-specific rule is used for:

* `/NAMES`;
* `/WHO`.

A `/WHO` query for a bare nickname has no channel context, so it cannot determine a channel-specific status prefix.

## Match-room visibility

A multiplayer match room has stricter channel access than ordinary channels.

A match channel is visible and joinable only to:

* referees of that match;
* players currently seated in that match.

`ChannelMembershipService.CanJoinMatchChannel` applies the same authorization rule to both `JOIN` and `LIST`.

Consequently:

```text
JOIN #mp_5
```

returns `ErrInviteOnlyChan` when the caller lacks permission.

A genuinely nonexistent channel instead returns `ErrNoSuchChannel`.

The distinction is intentional: the existence of a match room is not hidden, but unauthorized users cannot enter it or discover it through `/LIST`.

### Internal joins

Some internal paths deliberately bypass the IRC channel gate because they have already performed their own authorization.

These include:

* `MatchMembershipService.OccupySlot`;
* `TourneyMatchJoinChannelHandler`.

The former has already passed the multiplayer seat gate. The latter has already validated the tournament observer's required privileges.

The match creator always counts as a referee for channel access, regardless of their current referee-list representation. This prevents a creator from locking themselves out of their own match.

If a referee loses referee status while not seated, Basil proactively parts them from the match channel rather than leaving them in the channel until they manually leave.

## Match command state

`!mp settings` reports three independent categories:

```text
Players
Refs
IRC
```

They represent different facts:

* **Players**: users currently seated in the match;
* **Refs**: users with referee authority;
* **IRC**: users with a live `IrcSession` in the match channel.

A single account can appear in all three categories.

For example:

```text
Alice
├── seated
├── referee
└── connected through IRC
```

These properties must not be collapsed into a single "match member" concept.

## Match host state

Every match starts with:

```text
MatchSession.NoHostId
```

This means that nobody is currently seated as host. It does not mean that the match has never had a host.

A `GameSession` becomes host only after actually occupying a slot.

An `IrcSession` can create a match without becoming its host. The same applies when a seated game client creates another match while already belonging to a different match.

The resulting room can therefore initially have:

* a creator/referee;
* no seated players;
* no host.

Such a room is still valid and is subject to the normal empty-room auto-close timer.

## Session disconnects and IRC presence

IRC channel rosters are tracked by `UserId`, not by session instance.

This means that two sessions belonging to the same account count as one IRC member.

For example:

```text
GameSession joins #osu
    roster: Alice = 1 session
    → broadcast JOIN

IrcSession joins #osu
    roster: Alice = 2 sessions
    → no broadcast to other users
    → IRC session still receives its own JOIN/NAMES responses

IrcSession parts #osu
    roster: Alice = 1 session
    → no broadcast

GameSession parts #osu
    roster: Alice = 0 sessions
    → broadcast PART
```

### PART versus QUIT

Whether IRC sends `PART` or `QUIT` depends on whether the account still has another live session.

The session type that disconnected does not determine the result.

```text
session disconnects
       │
       ▼
another session for this UserId in this channel?
       │
   ┌───┴───┐
  yes      no
   │        │
   ▼        ▼
nothing   another session anywhere?
             │
         ┌───┴───┐
        yes      no
         │        │
         ▼        ▼
       PART      QUIT
```

More precisely:

* another session in the same channel → no IRC presence change;
* another session elsewhere → `PART` from this channel;
* no other session anywhere → one `QUIT` to every IRC member sharing a channel with the user.

A user must never receive both `PART` and `QUIT` for the same disconnect.

### Bancho logout

The IRC presence rules are independent of Bancho logout.

When a `GameSession` logs out, Basil still broadcasts the Bancho `Logout` packet to other `GameSession`s.

An `IrcSession` disconnect does not produce a Bancho `Logout` packet and does not modify match or spectator state.

An IRC-only connection was never a Bancho player.

## Kick and ban semantics

Kick and ban operate on different concepts.

### Kick

A kick applies to someone currently present in the match.

The implementation resolves all live sessions for the target `UserId` across both registries and removes whichever session is currently:

* seated in the match;
* present in the match channel.

This is presence-dependent.

### Ban

A match ban is a rule attached to a `UserId`, not to a live session.

The target does not need to be online:

```text
!mp ban offline-player
```

can still succeed.

The user repository is therefore used to resolve the account, while the session registries are not required.

The ban applies to the user's future attempts to join the match.

## Referee and BasilBot protection

Referees cannot be kicked or banned while they retain referee authority.

Removing a referee is an explicit operation:

```text
!mp removeref
```

Kick and ban must not silently remove match authority as a side effect.

BasilBot is excluded from:

* kick;
* ban;
* `addref`;
* invite.

BasilBot is not a participant in the match and therefore does not belong to these participant-management operations.

## Design rationale

### Why use types instead of `IsIrc`?

A runtime flag does not prevent code from accessing gameplay state on an IRC-only connection.

With separate types, the compiler enforces the boundary:

```text
JoinAsync(GameSession)
```

cannot accidentally receive an `IrcSession`.

This moves an entire class of bugs from runtime behavior to compile-time errors.

### Why is BasilBot a `GameSession`?

The current spectator implementation requires `GameSession` state.

`SpectatorService.AddSpectator` and `RemoveSpectator` operate on gameplay-session concepts such as `Spectating` and `Spectators`.

A future `ISpectatorObserver` abstraction could remove this compatibility requirement, but that would be a separate architectural refactor.

### Why can a referee not simply be kicked?

Referee authority and physical presence are separate concepts.

A referee may be responsible for controlling a match without currently occupying a player slot. Kicking the user directly would therefore silently remove both their presence and their authority.

The explicit `!mp removeref` operation makes the authority change intentional and visible.

## Related code

* [`Basil.Application/Sessions/UserSession.cs`](../../src/Basil.Application/Sessions/UserSession.cs)
* [`Basil.Application/Sessions/GameSession.cs`](../../src/Basil.Application/Sessions/GameSession.cs)
* [`Basil.Application/Sessions/IrcSession.cs`](../../src/Basil.Application/Sessions/IrcSession.cs)
* [`Basil.Application/Sessions/ISessionRegistry.cs`](../../src/Basil.Application/Sessions/ISessionRegistry.cs)
* [`Basil.Infrastructure/Sessions/GameSessionRegistry.cs`](../../src/Basil.Infrastructure/Sessions/GameSessionRegistry.cs)
* [`Basil.Infrastructure/Sessions/IrcSessionRegistry.cs`](../../src/Basil.Infrastructure/Sessions/IrcSessionRegistry.cs)
* [`Basil.Application/Sessions/Channels/ChannelMembershipService.cs`](../../src/Basil.Application/Sessions/Channels/ChannelMembershipService.cs): channel roster and JOIN/PART/QUIT behavior
* [`Basil.Application/Sessions/Irc/BanchoIrcBridgeConnection.cs`](../../src/Basil.Application/Sessions/Irc/BanchoIrcBridgeConnection.cs): Bancho-to-IRC bridge
* [`Basil.Application/Sessions/PlayerLogoutService.cs`](../../src/Basil.Application/Sessions/PlayerLogoutService.cs): game and IRC logout handling
* [`Basil.Application/Services/Irc/IrcAuthenticationService.cs`](../../src/Basil.Application/Services/Irc/IrcAuthenticationService.cs): IRC registration and authentication
* [`Basil.Application/Services/Irc/IrcQueryService.cs`](../../src/Basil.Application/Services/Irc/IrcQueryService.cs): IRC read-only query responses
* [`Basil.Application/Services/Multiplayer/MatchMembershipService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchMembershipService.cs): seating, host state, and empty-room lifecycle
* [`Basil.Application/Services/Multiplayer/MatchControlService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchControlService.cs): kick, ban, referee, and invite guards
* [`Basil.Application/Services/Bot/MpCommandService.cs`](../../src/Basil.Application/Services/Bot/MpCommandService.cs): `!mp` command behavior
* [`Basil.Infrastructure/Irc/TcpIrcListener.cs`](../../src/Basil.Infrastructure/Irc/TcpIrcListener.cs)
* [`Basil.Infrastructure/Irc/TcpIrcConnection.cs`](../../src/Basil.Infrastructure/Irc/TcpIrcConnection.cs)

## See also

* [`chat.md`](chat.md): the shared chat dispatch pipeline used by Bancho and IRC
* [`multiplayer.md`](multiplayer.md): match creation, rounds, and empty-room auto-close
* [`../for-technicians/https.md`](../for-technicians/https.md): IRC gateway networking and why IRC does not use HTTPS/TLS
