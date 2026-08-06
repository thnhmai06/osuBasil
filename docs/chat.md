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
- **Referee and host are separate roles** for `!mp` purposes. See the BasilBot Commands reference (`api.<domain>/docs/basil-bot/`) for the full command list and the referee/host distinction in detail.

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

**Why does a referee-less action fail silently instead of replying with an error?** Most `!mp` subcommands require the sender to be a referee of the targeted match. A silent no-op (rather than an error reply) avoids leaking match state or referee membership to someone who isn't authorized to see it. The command simply does nothing observable.

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
