# BasilBot chat commands

## Overview

BasilBot is the server's built-in chat command and announcement surface. It is not an actor but the voice the server speaks with: its
account is fixed, the server protects it, and nobody can sign in as it or act for it. Privileges mean nothing to it,
and its privileges only decide how a client shows it. Commands run with the privileges of the user who typed them.

Commands include:

* general BasilBot commands;
* `!mp` commands for multiplayer room management;
* `!channel` commands for managing the general channels.

`!channel list|create|set|delete` does what the HTTP API for channels does, and needs the privilege to manage
channels. Options are written `name=value`: `topic`, `read` and `write` (the privilege masks that may read and write),
`autojoin` and `visible` (`true` or `false`), for example `!channel create #events topic="Big event" autojoin=true`.
`list` shows the channels you can see; with the privilege it also shows the hidden ones.

The `!mp` commands support tournament operations such as creating and configuring rooms, managing referees and
players, and controlling room countdowns.

Commands run using the connection of the person who typed them, so they require that person's own privileges. BasilBot
only replies; its messages and server announcements bypass user privilege checks. The command handler and
announcement handlers are implemented in `Basil.Infrastructure.Bot` and run in the server for the osu! client and IRC.

Room countdowns belong to the room, not to BasilBot. A countdown that starts a round is cancelled when the round's
settings change; countdowns stop when the round starts or is aborted or the room closes. BasilBot announces the
countdown marks.

A message the operator types in the console's chat (posted as BasilBot through the operator's connection) is read as a
command like any other: the typist is the operator, so the command runs with every right. This is the only message
under BasilBot's name that is read as a command. BasilBot's own replies and announcements are never read, so a
reply that starts with `!` cannot start a command; the bot's own osu! and IRC connections are never typists.

BasilBot is shown as an online user to both the osu! client and IRC. Operators also have a console in the server
process (`Basil`): it runs a command as the operator, which is the one entry point that skips privilege checks,
while a chat command always runs with the typist's privileges. See [`../for-technicians/console.md`](../for-technicians/console.md).

---

## Command reference

The complete command reference is generated from BasilBot's command catalog: the same catalog makes the answers of
`!help` and `!mp help`, so the reference, the help and the commands the bot answers cannot drift apart.

It lists every command with its arguments and what it does. A running instance serves it at
`https://api.<domain>/guides/bot` (Markdown at `/guides/bot.md`), and it is published on
[GitHub Pages](https://thnhmai06.github.io/osuBasil/).

The generated reference is the authoritative source for the current command syntax. This page intentionally does not
duplicate the command list. Reply wording is not part of it: the server operator owns the wording (see
[Reply text](#reply-text)).

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

Every reply is a text in the operator-editable file `Configurations/Templates/replies.json`, a JSON object that maps a
reply's key to an
[ICU MessageFormat](https://unicode-org.github.io/icu/userguide/format_parse/messages/) string with named placeholders
such as `{room}`. The file is written with the default wording when it does not exist, is reloaded while the server
runs, and a missing key keeps its default wording. If an edit leaves the file unreadable, the last good wording stays
in use; a single text that is invalid is replaced by its default.

What each command is said to do in `!help` is a reply too (keys such as `bot.helpRoll`, `mp.helpMods`), so the
operator can reword it; the published reference shows the default wording.

Because an operator can rewrite any reply, clients, tournament tooling, and automated integrations must not assume that a
human-readable reply text is a stable command result. Use the HTTP API for machine-readable state. See
[`../for-technicians/configuration.md`](../for-technicians/configuration.md) for the data directories.

---

## See also

* [`api/overview.md`](api/overview.md): Basil's HTTP API
* [`osu-stable/protocol.md`](osu-stable/protocol.md): osu! (stable) protocol used to send and receive chat messages
* [`osu-stable/authentication.md`](osu-stable/authentication.md): authenticating an osu! stable client
* [`../for-developers/working-scopes.md`](../for-developers/working-scopes.md): commands and features included in Basil's scope
