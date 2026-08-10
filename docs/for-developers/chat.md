# Chat & commands

## Overview

Basil supports chat through two transports:

* osu! client Bancho packets;
* native IRC connections.

Both transports feed into the same chat dispatch pipeline. This keeps channel delivery, direct messages, blocking, and command handling consistent regardless of how a message entered the server.

BasilBot uses the same pipeline as a normal chat sender and provides tournament-related commands, including `!mp`.

## Unified dispatch

Chat must not be implemented separately for Bancho and IRC.

Both transports eventually produce the same logical operation:

```text
message
   │
   └── ChatDispatchService
            │
            ├── channel message
            ├── DM to BasilBot
            └── regular DM
```

This gives Basil one place to define:

* channel membership and delivery;
* blocking;
* command detection;
* command dispatch;
* bot replies.

Transport-specific code is responsible only for translating its protocol into the common chat model and delivering the resulting messages back to the client.

## Session and IRC connections

Every session has an [`IIrcConnection`](../../src/Basil.Application/Sessions/Irc/IIrcConnection.cs), regardless of which session type it represents.

### `GameSession`

A [`GameSession`](../../src/Basil.Application/Sessions/GameSession.cs) represents an osu! client connection.

Its `IIrcConnection` implementation is a bridge to the Bancho client and primarily forwards `PRIVMSG` operations through Bancho packets.

The client does not establish a real IRC connection.

### `IrcSession`

An [`IrcSession`](../../src/Basil.Application/Sessions/IrcSession.cs) represents a native IRC connection.

Its `IIrcConnection` implementation communicates directly with the IRC client and supports IRC-specific operations such as presence numerics and:

* `JOIN`;
* `PART`;
* `QUIT`.

The distinction between `GameSession` and `IrcSession` is intentional. See `irc.md` for the session architecture.

## IRC authentication

IRC authentication uses the same account password as osu! client authentication.

There is no separate IRC password.

The connection flow is:

```text
TCP connection
      │
      ▼
PASS + NICK + USER
      │
      ▼
IrcAuthenticationService
      │
      ├── validate account credentials
      │
      └── create IrcSession
              │
              ├── join auto-join channels
              └── send welcome numerics
```

An `IrcSession` has no Bancho game connection. It participates only in chat and commands.

## Chat dispatch

[`ChatDispatchService`](../../src/Basil.Application/Services/Chat/ChatDispatchService.cs) is the common entry point for messages from both transports.

Conceptually, every message follows:

```text
Bancho SEND_MESSAGE ─┐
                     ├──► ChatDispatchService
IRC PRIVMSG ─────────┘
                            │
                            ├── channel message
                            │
                            ├── regular DM
                            │
                            └── command
```

The dispatcher determines the message's destination and whether it should be interpreted as a command.

## Commands

The configured command prefix is `!` by default.

A message beginning with the prefix is treated as a command regardless of transport:

```text
Bancho SEND_MESSAGE "!mp help"
          │
          ▼
ChatDispatchService
          │
          ▼
ICommandDispatcher
          │
          ▼
command handler
```

The same applies to:

```text
IRC PRIVMSG "!mp help"
```

### BasilBot DMs

A direct message to BasilBot is always treated as a command, even without the prefix.

For example:

```text
DM BasilBot: mp help
```

is command input rather than ordinary chat.

This avoids ambiguity: a direct message addressed specifically to the bot has already established the sender's intent.

Unrecognized top-level commands remain silent. This prevents BasilBot from replying to arbitrary text sent directly to it.

## Command routing

After command detection, the message enters [`ICommandDispatcher`](../../src/Basil.Application/Abstractions/Bot/ICommandDispatcher.cs).

General commands use the common command table. Multiplayer commands are handled by [`MpCommandService`](../../src/Basil.Application/Services/Bot/MpCommandService.cs):

```text
ICommandDispatcher
       │
       ├── general commands
       │
       └── !mp
             │
             └── MpCommandService
```

The command layer is independent of whether the original message came from Bancho or IRC.

## `!mp` roles

Multiplayer commands distinguish three separate concepts:

* **creator**: the user who created the match;
* **referee**: a user with referee permissions for the match;
* **host**: the current multiplayer host.

These roles are not interchangeable.

The match creator:

* retains full `!mp` authority for the lifetime of the room;
* can add or remove referees through `!mp addref` and `!mp removeref`;
* cannot be removed from the referee list.

Referee permissions are scoped to the relevant multiplayer match.

See the generated BasilBot Commands reference at `api.<domain>/docs/basil-bot/` for the complete command and permission model.

## Command scope

A command may be associated with a specific multiplayer match.

[`CommandDispatcher`](../../src/Basil.Application/Services/Bot/CommandDispatcher.cs) resolves this scope independently of the transport and then decides where the response should be delivered.

### Match channel

When a scoped `!mp` command is issued in the match's own channel, the response is sent publicly there.

```text
match channel
      │
      ▼
public command reply
```

### Other channels and DMs

A scoped command issued outside its match channel must not expose match-specific information to unrelated users.

For example:

* `#osu`;
* `#lobby`;
* another shared channel;
* a direct message.

In these contexts, the scoped response is delivered to the sender by DM instead.

When appropriate, the room can still receive an unprefixed copy so that referees operating on the match remotely remain aware of the command.

The principle is:

> Match-specific command output must not leak into shared channels.

### `!mp in`

`!mp in` exists to let a referee scope commands to a match without physically being in that match's channel.

It is therefore restricted to DMs.

Allowing `!mp in` in a public channel would expose the sender's match scope to everyone in that channel.

## Failed commands

A recognized command that fails validation or permission checks produces an error response.

This is particularly important for `!mp` commands, where many operations require referee permissions.

For example:

```text
!mp start
      │
      ▼
permission / validation failure
      │
      ▼
error response
```

The error follows the same scope rules as other command responses:

* in the target match channel, it is posted publicly;
* elsewhere, it is sent to the sender by DM.

This makes permission failures and malformed commands observable without exposing match-specific errors to unrelated users.

Unrecognized top-level commands remain silent.

## BasilBot

BasilBot is represented internally as a synthetic `GameSession`.

It is initialized during server startup without an underlying client connection.

Because BasilBot does not send normal client traffic, it is exempt from the idle-disconnect sweep that would otherwise remove sessions that stop sending pings.

From the chat system's perspective, BasilBot is therefore another sender with a normal `IIrcConnection`:

```text
BasilBot
   │
   ▼
synthetic GameSession
   │
   ▼
IIrcConnection
   │
   ▼
normal chat delivery
```

This allows bot responses to use the same delivery infrastructure as ordinary users.

## Bot message delivery

BasilBot replies through the normal channel broadcast path rather than writing protocol packets directly.

The relevant path is:

```text
command
   │
   ▼
BasilBot response
   │
   ▼
ChannelMembershipService.BroadcastPrivmsg
   │
   ├── Bancho clients
   └── IRC clients
```

This is important because a bot command can be issued from either transport, while its response may need to reach users connected through the other transport.

BasilBot therefore does not need separate Bancho and IRC delivery logic.

## End-to-end lifecycle

A normal chat message:

```text
osu! SEND_MESSAGE ─┐
                   │
IRC PRIVMSG ───────┤
                   ▼
          ChatDispatchService
                   │
          ┌────────┴────────┐
          │                 │
       command           normal chat
          │                 │
          ▼                 ├── channel broadcast
 ICommandDispatcher         └── DM
          │
     ┌────┴─────┐
     │          │
 general       !mp
 command       command
     │          │
     │          ▼
     │   MpCommandService
     │          │
     └────┬─────┘
          ▼
      response
          │
          ▼
   common chat delivery
```

The transport is therefore only relevant at the edges of the system. Command semantics and message routing remain transport-independent.

## Related code

* [`Basil.Application/Services/Chat/ChatDispatchService.cs`](../../src/Basil.Application/Services/Chat/ChatDispatchService.cs): unified chat entry point
* [`Basil.Application/Services/Bot/CommandDispatcher.cs`](../../src/Basil.Application/Services/Bot/CommandDispatcher.cs): command detection, dispatch, and scope
* [`Basil.Application/Services/Bot/MpCommandService.cs`](../../src/Basil.Application/Services/Bot/MpCommandService.cs): multiplayer command handling
* [`Basil.Application/Services/Irc/IrcAuthenticationService.cs`](../../src/Basil.Application/Services/Irc/IrcAuthenticationService.cs): IRC authentication and session creation
* [`Basil.Infrastructure/Irc/TcpIrcListener.cs`](../../src/Basil.Infrastructure/Irc/TcpIrcListener.cs): embedded IRC TCP listener

## See also

* [`bancho.md`](bancho.md): how Bancho `SEND_MESSAGE` packets enter the chat pipeline
* [`irc.md`](irc.md): IRC transport and `IrcSession` architecture
* [`working-scopes.md`](working-scopes.md): chat features and `!mp` commands that are in or out of scope
* BasilBot Commands (`api.<domain>/docs/basil-bot/`): generated command reference
