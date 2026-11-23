# Connecting an IRC client

Basil has an IRC server that lets standard IRC clients, bots and bouncers use the same chat as the osu! client:
read channels, talk to other players, message BasilBot and run its commands. You can use common clients such as
HexChat, mIRC, Irssi, WeeChat or Textual.

The wire contract (commands, numerics, supported IRCv3 capabilities) is in
[`../for-developers/irc.md`](../for-developers/irc.md). This page is what a user needs to connect.
The commands and capabilities the server answers are listed in the generated IRC guide, at `https://api.<domain>/docs/irc/`
on a running instance.

---

## What you need

| Setting | Value |
| --- | --- |
| Server | `irc.<domain>` (the same domain as the osu! server) |
| Port | `6667`, plain text, **no TLS** |
| Nickname | your username, with spaces written as `_` (case does not matter) |
| Password / SASL password | your account password |

### Signing in

* Most clients: put your account password in the **server password** field, and set the nickname to your username.
* Clients with SASL: choose **PLAIN**, with your username as the login and your password as the password.

Each sign-in is a new session, and an account holds one IRC session at a time. While another IRC session of the
account is in use, a new sign-in is refused; once that session has been silent for 30 seconds, the new sign-in ends it.

> The IRC port is not encrypted: your password travels in clear text each time you connect. Connect from a trusted
> network or through a tunnel.

If the password is wrong, the nickname is not yours, or the account is already signed in on IRC, the server answers
`464` and closes the connection.

---

## Using IRC

* Join the channels you are allowed to read; you automatically join the ones the server marks as auto-join.
* Room chat is the channel `#mp_<room id>`. You can join it if you take part in the room as its creator or a referee,
  or hold the privileges to observe every room.
* Message **BasilBot** (`/msg BasilBot !help`) or type commands in a channel (`!mp ...`). Commands run with your own
  privileges, as in the osu! client.
* Channels, topics and modes are managed through the API or `!channel` in a private message to BasilBot, never through
  IRC commands (`TOPIC`, `MODE`, `KICK` and `INVITE` answer `482`).
* The server keeps no chat history.

---

## See also

* [`api/overview.md`](api/overview.md): Basil's HTTP API
* [`basil-bot.md`](basil-bot.md): BasilBot commands
* [`osu-stable/authentication.md`](osu-stable/authentication.md): osu! client authentication
