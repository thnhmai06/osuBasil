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
| Port | `6697`, **TLS on** |
| Nickname | your username, with spaces written as `_` (case does not matter) |
| Password / SASL password | your account password |

### Signing in

* Most clients: put your account password in the **server password** field, and set the nickname to your username.
* Clients with SASL: choose **PLAIN**, with your username as the login and your password as the password.

Each sign-in is a new session, and an account holds one IRC session at a time. While another IRC session of the
account is in use, a new sign-in is refused; once that session has been silent for 30 seconds, the new sign-in ends it.

The connection is TLS only. A client or bot that can only speak plain text IRC (port `6667`) cannot connect to Basil.
The client must trust the server's certificate: a server on the Internet uses a certificate from a public authority, and
a LAN server may use the self-signed `basil.local` certificate, which you add to the client's trust (see the examples
below, and [`https.md`](../for-technicians/https.md) for the operator side). The name `irc.<domain>` must resolve to the
server like the other Basil names; see
[Make the Basil domain resolve](osu-stable/getting-started.md#3-make-the-basil-domain-resolve).

If the password is wrong, the nickname is not yours, or the account is already signed in on IRC, the server answers
`464` and closes the connection.

---

## Examples

Both examples connect to `irc.basil.local` with TLS and sign in with SASL `PLAIN`. Replace the name with your server's
domain, `yourname` with your username (spaces written as `_`) and `yourpassword` with your account password. For the
self-signed `basil.local` certificate, ask the operator for the file `basil.local.crt` (a server on the Internet with a
public certificate needs none of the trust steps).

### Irssi

Add the network and the server, trusting the certificate file:

```text
/network add -sasl_mechanism PLAIN -sasl_username yourname -sasl_password yourpassword Basil
/server add -network Basil -tls -tls_cafile ~/basil.local.crt -auto irc.basil.local 6697
/connect Basil
```

`/save` keeps both. Drop `-tls_cafile` for a certificate from a public authority.

### WeeChat

Add the server with TLS and SASL:

```text
/server add basil irc.basil.local/6697 -tls -autoconnect
/set irc.server.basil.nicks "yourname"
/set irc.server.basil.sasl_mechanism plain
/set irc.server.basil.sasl_username "yourname"
/set irc.server.basil.sasl_password "yourpassword"
```

For the self-signed certificate, WeeChat refuses the connection until it trusts it. Either copy `basil.local.crt` to
`tls/CAs.pem` in WeeChat's configuration folder and run `/tls reload`, or pin the certificate by its fingerprint
(`openssl x509 -in basil.local.crt -noout -fingerprint -sha256`, written without colons):

```text
/set irc.server.basil.tls_fingerprint "<fingerprint>"
```

Then `/connect basil`.

Other clients (HexChat, mIRC, Textual, Halloy) take the same four values: the server name, port `6697`, TLS on, and your
credentials.

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
