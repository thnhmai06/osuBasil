# IRC and the Dual-Session Model

## Overview

An account can connect to Basil two different ways at once: a real osu! game client, and a real IRC client (any standard IRC program, or a bouncer). These are not the same kind of connection wearing different clothes — a game client carries gameplay state (match, spectating, stats, a packet queue) that an IRC connection has none of, and never should. Before this design, both were represented by one concrete session type, which let an IRC-only connection accidentally end up looking like a seated multiplayer player. This doc is about the fix: two separate session types, and the rules for how they coexist, disconnect, and show up in chat and `!mp` output.

## Why two session types instead of one with a flag?

A `bool IsIrc` flag on one concrete class doesn't stop a code path from reading `Match`/`Spectating`/`Status` on a session that has none — the compiler can't catch it, so the bug just waits for someone to call the wrong method on the wrong kind of connection. That is exactly the original report: `!mp make` from an IRC connection seated the referee in a multiplayer slot, because nothing at the type level said an IRC connection *couldn't* be seated.

Splitting into sibling types closes that hole at compile time. Every match-seating method (`JoinAsync`, `ForceJoinAsync`, `OccupySlot`) takes a `GameSession`, not the shared base — there is no longer any way to even write the call that seats an `IrcSession`. Type safety uses the compiler instead of the reviewer.

## Contract

- **One account, up to two live sessions.** A user can have a `GameSession`, an `IrcSession`, both, or neither, all under the same account id. Neither kind ever evicts the other on login — a fresh IRC login doesn't touch an existing `GameSession` and vice versa.
- **Only a `GameSession` can occupy a multiplayer slot.** An `IrcSession` can create a room, referee it, chat in it, and run every `!mp` subcommand, but it physically cannot be seated — the type system forbids it.
- **BasilBot is a synthetic `GameSession`**, not an `IrcSession` and not "no session." It never connects over TCP; it exists so `SpectatorService` can expose its input over the `api.` host's SSE `/spec/{id}` channel the same way a real player's would be. This is a compatibility shape for the current implementation, not a claim that a bot "is" a game client.
- **`!mp settings` reports three independent lists** — Players (who's seated), Refs (who has referee authority), and IRC (who has a live `IrcSession` in the room's own channel). One account can appear in all three at once: seated *and* a referee *and* running a separate IRC connection are three separate facts, not one.

## Concepts

- **`UserSession` (abstract) holds identity and chat** — id, name, privilege, silence state, channel membership, and an abstract `IrcConnection` every chat message routes through. `GameSession` and `IrcSession` are its only two implementations.
- **`GameSession` holds everything gameplay-related**: the packet queue, `Match`, `Spectating`/`Spectators`, `Status`, per-mode stats. Its `IrcConnection` is a `BanchoIrcBridgeConnection` that translates an IRC-shaped `PRIVMSG` into a bancho `SendMessage` packet — it is deliberately a one-way bridge: JOIN/PART/QUIT and numerics have no bancho equivalent and are silently dropped there, since the osu! client already learns channel membership through `ChannelInfo` packets, not per-user join/part events.
- **`IrcSession` holds a real `IIrcConnection`** (a live TCP socket) and nothing gameplay-related at all.
- **The registry (`IUserSessionRegistry`) never returns an ambiguous "some session" for a UserId.** Lookups are typed: `GetGameByUserId`/`GetGameByName` for a seatable session, `GetIrcByUserId`/`GetIrcByName` for a chat-only one, `GetSessionsByUserId` when either kind is acceptable (e.g. "deliver this DM to every live connection"). Registration is atomic per kind (`TryAddGameSession`/`TryAddIrcSession`), so two logins racing for the same account resolve to exactly one winner without a separate check-then-add step.
- **A match's `HostId` is never "no one and never has been."** Every match starts at `MatchSession.NoHostId`; a `GameSession` becomes host only once it actually occupies a slot. An `IrcSession` creator, or a `GameSession` creator already seated in a different match, leaves the new room with nobody seated — referee-only, exactly like a referee using `!mp make` from chat with no client behind them at all. That room isn't abandoned: its 5-minute empty-room auto-close timer (see [`multiplayer.md`](multiplayer.md)) starts immediately.

## Lifecycle

**A user with both an active `GameSession` and a live IRC connection, in the same channel:**

```
Channel roster tracks UserIds, not sessions — one UserId with two live
sessions in a channel still counts as one member for JOIN/PART purposes.

GameSession joins #osu   -> roster: {alice: 1 session}  -> broadcasts JOIN (alice entered)
IrcSession joins #osu    -> roster: {alice: 2 sessions}  -> no broadcast (already present);
                                                             the IrcSession still gets its own
                                                             JOIN echo + /NAMES reply
IrcSession parts #osu    -> roster: {alice: 1 session}   -> no broadcast (still present via GameSession)
GameSession parts #osu   -> roster: {}                   -> broadcasts PART (alice truly left)
```

**Disconnect: PART vs. QUIT depends only on whether the UserId survives elsewhere, never on which session type disappeared:**

```
Session disconnects (game logout OR irc connection closed OR ghost-reap)
   |
   +-- Same UserId has another session in THIS channel?
   |      yes -> nothing broadcast here, roster count just decrements
   |
   +-- Same UserId has another session ANYWHERE (this channel or another)?
   |      yes -> PART for this channel only
   |
   +-- No other session anywhere
          -> exactly one QUIT to every IRC member across every channel shared,
             never a PART as well
```

- **A `GameSession` logout still broadcasts the bancho `Logout` packet** to every other `GameSession` — that is independent of the PART/QUIT decision above, which only governs the IRC wire.
- **An `IrcSession` disconnecting never sends a bancho `Logout` packet, never touches match or spectator state.** Those would be meaningless for a connection that was chat/commands only; only a `GameSession` was ever a "player" to another osu! client in the first place.

## Design

**Why not just add an `IsIrc` check everywhere `Match`/`Spectating` is read?** That was the original bug. A flag is something every call site has to remember to check; a type is something the compiler enforces once, at the method signature, and every call site downstream inherits the guarantee for free.

**Why does BasilBot get to be a `GameSession` when it's not a real client?** Because `SpectatorService.AddSpectator`/`RemoveSpectator` need `Spectating`/`Spectators`, which only exist on `GameSession`. A cleaner long-term shape — an `ISpectatorObserver` that isn't a session at all — is a real option, but it's a separate refactor from this one and isn't done here.

**Why is kick presence-gated but ban is not?** Kicking removes someone who is currently in the room, so it only makes sense against whoever is actually there right now — resolved by walking every live session for that UserId (`GetSessionsByUserId`), game or IRC, and removing whichever of them is seated or in the room's chat channel. A ban is a standing rule about a UserId, independent of whether anyone matching it is online at all: `!mp ban somebody-currently-offline` still works, resolved through the user repository rather than the session registry, and blocks that UserId's next join attempt whenever it happens.

**Why can referees and BasilBot never be kicked or banned?** A referee losing their seat is not the same as losing their authority — kicking or banning one outright would silently strip match control from whoever is supposed to be running it. `!mp removeref` is the deliberate, visible way to demote a referee first; kick/ban simply refuse the request otherwise. BasilBot is excluded from kick/ban/addref/invite entirely regardless of match state, since it is never a participant to begin with.

## Related code

- `Basil.Application/Sessions/UserSession.cs`, `GameSession.cs`, `IrcSession.cs`
- `Basil.Application/Sessions/IUserSessionRegistry.cs`, `Basil.Infrastructure/Sessions/InMemoryUserSessionRegistry.cs`
- `Basil.Application/Sessions/Channels/ChannelMembershipService.cs` (JOIN/PART/QUIT roster and delivery rules)
- `Basil.Application/Sessions/Irc/BanchoIrcBridgeConnection.cs`
- `Basil.Application/Sessions/PlayerLogoutService.cs` (game-vs-IRC logout branching)
- `Basil.Application/Services/Irc/IrcAuthenticationService.cs` (the PASS/NICK/USER handshake)
- `Basil.Application/Services/Multiplayer/MatchMembershipService.cs` (`NoHostId`, empty-room auto-close)
- `Basil.Application/Services/Multiplayer/MatchControlService.cs` (kick/ban/addref/invite guards)
- `Basil.Application/Services/Bot/MpCommandService.cs` (`!mp settings`' three lists, read-only subcommands)
- `Basil.Infrastructure/Irc/TcpIrcListener.cs`, `TcpIrcConnection.cs`

## See also

- [`chat.md`](chat.md): the shared chat dispatch core both transports funnel through
- [`multiplayer.md`](multiplayer.md): match creation, rounds, and the empty-room auto-close timer
- [`run-deployment.md`](run-deployment.md): the IRC gateway's TCP port and why it needs no TLS or SAN entry
