# Connecting an IRC client

Basil has an IRC server that lets standard IRC clients, bots and bouncers use the same chat as the osu! client:
read channels, talk to other players, message BasilBot and run its commands. You can use common clients such as
HexChat, mIRC, Irssi, WeeChat or Textual.

The wire contract (commands, numerics, supported IRCv3 capabilities) is in
[`../for-developers/irc.md`](../for-developers/irc.md). This page is what a user needs to connect.

---

## What you need

| Setting | Value |
| --- | --- |
| Server | `irc.<domain>` (the same domain as the osu! server) |
| Port | `6667`, plain text, **no TLS** |
| Nickname | your username, with spaces written as `_` (case does not matter) |
| Password / SASL password | an **IRC key** (not your account password) |

### Getting an IRC key

An IRC key belongs to one device or bot. Create it with the HTTP API (`POST /v1/users/{id}/sessions/irc`, with a
label such as "osu-ahr VPS"); the key is shown once. Ending that IRC session revokes only that key. Your account
password is never accepted by IRC.

> The IRC port is not encrypted: the key travels in clear text each time you connect. Anyone who captures it can chat
> as you until you revoke it. Use a key per device and revoke it if it leaks.

### Signing in

* Most clients: put the key in the **server password** field, and set the nickname to your username.
* Clients with SASL: choose **PLAIN**, with your username as the login and the key as the password.

If the key is wrong, revoked, or the nickname is not yours, the server answers `464` and closes the connection.

---

## Using IRC

* Join the channels you are allowed to read; you automatically join the ones the server marks as auto-join.
* Room chat is the channel `#mp_<room id>`. You can join it if you take part in the room as its creator or a referee,
  or hold the rights to observe every room.
* Message **BasilBot** (`/msg BasilBot !help`) or type commands in a channel (`!mp ...`). Commands run with your own
  rights, as in the osu! client.
* Channels, topics and modes are managed through the API or `!channel` in a private message to BasilBot, never through
  IRC commands (`TOPIC`, `MODE`, `KICK` and `INVITE` answer `482`).
* The server keeps no chat history.

---

## See also

* [`api/overview.md`](api/overview.md): Basil's HTTP API
* [`basil-bot.md`](basil-bot.md): BasilBot commands
* [`bancho/authentication.md`](bancho/authentication.md): osu! client authentication
