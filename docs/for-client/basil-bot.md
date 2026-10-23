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
announcement handlers are implemented in `Basil.Bot`, but host integration is not migrated yet.

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
* command behaviour.

### Running server

Open:

```text
https://api.<domain>/docs/basil-bot/
```

### Online documentation

The same documentation is available without a running Basil server:

[BasilBot documentation](https://thnhmai06.github.io/osuBasil/)

The generated reference is the authoritative source for the current command syntax. This page intentionally does not duplicate the command list. Reply wording is not part of it: the server operator owns the wording (see [Reply text](#reply-text)).

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

BasilBot's replies are part of the client-visible command contract, but their wording belongs to the server operator.

Every reply is a text in the operator-editable file `Data/Bot/replies.json`, a JSON object that maps a reply's key to an
[ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/) string with named placeholders
such as `{room}`. The file is written with the default wording when it does not exist, is reloaded while the server
runs, and a missing key keeps its default wording. If an edit leaves the file unreadable, the last good wording stays
in use; a single text that is invalid is replaced by its default.

Because an operator can rewrite any reply, clients, tournament tooling, and automated integrations must not assume that a
human-readable reply text is a stable command result. Use the HTTP API for machine-readable state. See
[`../for-technicians/configuration.md`](../for-technicians/configuration.md) for the data directories.

---

## See also

* [`api/overview.md`](api/overview.md): Basil's HTTP API
* [`bancho/protocol.md`](bancho/protocol.md): Bancho protocol used to send and receive chat messages
* [`bancho/authentication.md`](bancho/authentication.md): authenticating an osu! stable client
* [`../for-developers/working-scopes.md`](../for-developers/working-scopes.md): commands and features included in Basil's scope
