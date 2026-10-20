# BasilBot chat commands

## Overview

BasilBot is the server's built-in chat command and announcement surface. It is not an actor but the voice the server speaks with: its
account is fixed, the server protects it, and nobody can sign in as it or act for it. Permissions mean nothing to it,
and its privileges only decide how a client shows it. Commands run with the rights of the user who typed them.

Commands include:

* general BasilBot commands;
* `!mp` commands for multiplayer room management.

The `!mp` commands support tournament operations such as creating and configuring rooms, managing referees and
players, and controlling room countdowns.

Commands run using the connection of the person who typed them, so they require that person's own rights. BasilBot
only replies; its messages and server announcements bypass user permission checks. The command handler and
announcement handlers are implemented in `Basil.Bot.Application`, but host integration is not migrated yet.

Room countdowns belong to the room, not to BasilBot. A countdown that starts a round is cancelled when the round's
settings change; countdowns stop when the round starts or is aborted or the room closes. BasilBot announces the
countdown marks.

## Planned

The following host-phase work is not implemented yet:

* showing BasilBot as an online user on the transports (the server already keeps its connection);
* integration of the new command and announcement handlers with the migrated hosts;
* the planned operator console in `Basil.Cli`.

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
