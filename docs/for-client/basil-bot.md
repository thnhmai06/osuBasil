# BasilBot chat commands

## Overview

BasilBot is Basil's in-game chat bot, providing chat commands similar to BanchoBot.

Commands include:

* general BasilBot commands;
* `!mp` commands for multiplayer room management.

The `!mp` commands can be used for tournament operations such as creating and configuring rooms, managing referees and players, and controlling match timers.

Match timers (`!mp start <seconds>`, `!mp timer`, `!mp aborttimer`) are run by BasilBot itself, not by the server.
Only the room's creator and referees can start or abort one. When a timer that starts the match ends, BasilBot starts
the round on behalf of the user who set it, so the server checks that user's authority at that moment. A timer that
would start the match is cancelled when the round's settings change, and any timer stops when a round starts or is
aborted or the room closes.

---

## Command reference

The complete command reference is generated from Basil's OpenAPI documentation.

It includes:

* available commands;
* `!mp` subcommands;
* command arguments;
* required arguments;
* command behaviour;
* exact reply text.

### Running server

Open:

```text
https://api.<domain>/docs/basil-bot/
```

### Online documentation

The same documentation is available without a running Basil server:

[BasilBot documentation](https://thnhmai06.github.io/osuBasil/)

The generated reference is the authoritative source for the current command syntax and reply text. This page intentionally does not duplicate the command list.

---

## Using commands

BasilBot commands are entered through an osu! chat channel.

For example:

```text
!help
```

Multiplayer commands use the `!mp` prefix:

```text
!mp <subcommand> <arguments>
```

The exact subcommands and argument syntax are defined by the generated BasilBot documentation.

---

## Reply text

BasilBot's replies are part of the client-visible command contract.

Clients, tournament tooling, and automated integrations should not assume that arbitrary human-readable text represents a stable command result unless that behaviour is explicitly documented.

For the exact current reply text, use the generated BasilBot reference.

---

## See also

* [`api/overview.md`](api/overview.md): Basil's HTTP API
* [`bancho/protocol.md`](bancho/protocol.md): Bancho protocol used to send and receive chat messages
* [`bancho/authentication.md`](bancho/authentication.md): authenticating an osu! stable client
* [`../for-developers/working-scopes.md`](../for-developers/working-scopes.md): commands and features included in Basil's scope
