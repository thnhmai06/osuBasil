# Chat & Commands

## Overview

Basil has two ways to send a chat message (the osu! client's own bancho packets, and a real IRC connection), plus one bot account that understands tournament commands. All three funnel through the same dispatch layer, so a message behaves the same regardless of where it came from.

## Why a unified core instead of separate paths?

Without a shared layer, "who can see this message" and "does this start with `!`" would need to be implemented twice: once for bancho clients, once for IRC. That's the kind of duplication that drifts the moment one path gets a bug fix the other doesn't. Routing every message, whether bancho or IRC, through one dispatcher keeps channel membership, blocking, and command parsing defined in exactly one place.

## Contract

- **Every session has an `IIrcConnection`**, regardless of which of the two session types it is. A `GameSession` (a real osu! client) gets a bridge implementation that only forwards `PRIVMSG`; an `IrcSession` (a real IRC client) gets a full implementation that also handles presence numerics (`JOIN`/`PART`/`QUIT`). See [`irc.md`](irc.md) for why the two are separate types.
- **IRC login uses the account password**, the same one used for the osu! client, not a separate IRC-only password.
- **A message starting with the configured command prefix (`!` by default) is a command**, not a chat message, whether it arrived as a bancho `SEND_MESSAGE` packet or an IRC `PRIVMSG`.
- **A direct DM to BasilBot never needs the prefix:** every DM to the bot is already unambiguously a command.

## Concepts

- **`ChatDispatchService`** is the one entry point every chat message passes through, whichever transport it arrived on. It decides three things: is this a channel message, a DM to the bot, or a regular DM; then routes accordingly.
- **The IRC gateway is embedded:** no separate process, no Docker container. `TcpIrcListener` binds a raw TCP port (6667 by default) directly inside the same server process.
- **BasilBot is a synthetic `GameSession`**, bootstrapped at startup with no client connection behind it. It's exempt from the idle-disconnect sweep that would otherwise reap a session that never sends a ping.
- **Referee, host, and creator are three separate roles** for `!mp` purposes. The creator — whoever ran `!mp make`/`!mp makeprivate` or created the room from the client — holds full `!mp` authority for the room's lifetime regardless of referee status, is the only one who can run `!mp addref`/`!mp removeref` from chat, and can never be removed from the referee list. See the BasilBot Commands reference (`api.<domain>/docs/basil-bot/`) for the full command list and the referee/host/creator distinction in detail.

## Lifecycle

**A chat message, from either transport:**

```
osu! client SEND_MESSAGE  or  IRC PRIVMSG
                |
        ChatDispatchService.SendPrivmsgAsync
                |
   starts with "!" (or is a bot DM)?
        /                    \
      yes                     no
       |                       |
ICommandDispatcher      deliver as a normal chat message
       |                (channel broadcast, or DM if online)
 general command table
       or
 MpCommandService (!mp <subcommand>)
```

**An IRC client connecting:**

```
TCP connect -> PASS + NICK + USER
        |
IrcAuthenticationService.AuthenticateAsync
        |
verify password (same check as osu! client login)
        |
create an IrcSession (no bancho socket, chat/commands only)
        |
join auto-join channels, send welcome numerics
```

## Design

**Why route bot replies through the same broadcast path as regular chat instead of writing packets directly?** A reply from BasilBot needs to reach real IRC clients in the channel too, not just the bancho client that triggered it. Using the same `ChannelMembershipService.BroadcastPrivmsg` path every regular message uses means the bot never needs its own delivery logic. It's just another sender.

**Why is a failed `!mp` command reported as an error instead of staying silent?** Most `!mp` subcommands require the sender to be a referee of the targeted match. Earlier versions did nothing observable on failure; now the bot answers with an error so a typo or a missing permission is visible instead of being hard to debug. The error is posted publicly only in the match's own channel; from a DM, `#osu`, `#lobby`, or any other channel it is DM'd back to the sender instead, so shared channels never get spammed with someone else's failed command — the same routing a scoped reply already uses. Unrecognized top-level commands stay silent: every DM to the bot is treated as a command, so answering those would make the bot talk back to every casual message.

**Why does a scoped `!mp` reply move to DM the moment it isn't typed in the match's own channel?** `#osu`, `#lobby`, and every other ordinary channel are shared by everyone who can read them — broadcasting a room's settings or slot list into one just because the sender happens to be scoped to (or seated in) that match would leak it to bystanders with no standing in the room. `CommandDispatcher` resolves the scope the same way regardless of where the command came from, then routes the reply based on where it landed: the match's own channel gets a normal public reply, and everywhere else — a DM, `#osu`, `#lobby` — gets the DM treatment (`[#id]`-prefixed, with an unprefixed copy still mirrored into the room so referees running it remotely stay visible to it). The same reasoning is why `!mp in` itself only runs from a DM: it exists to let a referee reach a match they aren't physically in, and letting it be set from a public channel would announce that scoping to everyone else there.

## Related code

- `Basil.Application/Services/Chat/ChatDispatchService.cs`
- `Basil.Application/Services/Bot/CommandDispatcher.cs`
- `Basil.Application/Services/Bot/MpCommandService.cs`
- `Basil.Application/Services/Irc/IrcAuthenticationService.cs`
- `Basil.Infrastructure/Irc/TcpIrcListener.cs`

## See also

- [`bancho.md`](bancho.md): how a bancho `SEND_MESSAGE` packet reaches this dispatcher
- [`working-scopes.md`](working-scopes.md): which chat features and `!mp` subcommands exist versus were cut on purpose
- BasilBot Commands (`api.<domain>/docs/basil-bot/`): the full, generated command reference
