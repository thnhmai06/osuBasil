# IRC transport

## Overview

### Why

Basil's chat is one model shared by every transport. IRC exists so that referees, operators and bots (osu-ahr style
tools, bouncers, desktop IRC clients) can read chat, run BasilBot commands and referee rooms next to, or instead of,
the osu! stable client. The osu! client does not use IRC; it receives chat as Bancho packets.

The IRC transport adds no rules of its own. It decodes IRC lines, calls the Application operation of the object being
acted on with the IRC connection as the actor, and encodes the typed result as IRC lines. Authority is decided by the
user's effective permissions, never by the fact that the request came over IRC.

The wire behaviour follows [Modern IRC](https://modern.ircdocs.horse/) and the [IRCv3](https://ircv3.net/irc/)
specifications that are listed below. Where this page and a specification differ, this page is the contract.

### What

* A user can hold a Bancho connection **and** several IRC connections at the same time. Neither evicts the other.
* An IRC connection is a full chat participant: it joins channels, sends and receives messages, runs BasilBot commands
  and referees or creates rooms to the extent the user's permissions allow.
* An IRC connection is never a player: it cannot be seated in a room slot and cannot spectate.
* IRC is read-and-chat only. Nothing is *managed* through IRC: topics, channel modes, kicks and invitations are refused
  with `482`. Channels are managed through the HTTP API or by messaging BasilBot (`!channel list|create|set|delete`).
* The server keeps **no chat history**, so there is no `chathistory`.

## Public contract

### Endpoint

`irc.<domain>:6667`, plain TCP, **no TLS**, so that existing IRC software works unchanged. There is no IRCv3 `sts` and
no SASL `EXTERNAL`. The port is `Server:Ports:Irc`.

Because the connection is not encrypted, **the IRC key crosses the network in clear text on every connection**. Whoever
captures it can chat as the key's owner until the key is revoked. Keys are per device, so revoke the one that leaked
(closing that IRC session in the HTTP API revokes exactly that key) and prefer connecting from a trusted network or
through a tunnel. [`https.md`](../for-technicians/https.md) explains why HTTPS and IRC differ.

### Identity

* A **nickname** is the account's username with spaces replaced by `_`. It cannot be changed: `NICK` after registration
  gets `462`. Nicknames compare case-insensitively with ASCII case folding (`CASEMAPPING=ascii`).
* The visible mask of every user is `nick!nick@<domain>`. The real address of a client never appears in `WHO`, `WHOIS`,
  `USERHOST`, `NAMES` or any prefix.
* An **IRC key** is a long-lived secret that belongs to one device. It is issued once through the HTTP API
  (`POST /v1/users/{id}/sessions/irc`), stored only as a hash, and is the only credential IRC accepts: the account
  password is never accepted. Every connection made with a key is recorded as an IRC session of that key.
* A key that is wrong, revoked or whose account is deleted is refused with `464`.

### Registration

```text
client                                  server
CAP LS 302            ->                :irc.<domain> CAP * LS :... sasl=PLAIN ...
CAP REQ :sasl ...     ->                :irc.<domain> CAP * ACK :sasl ...
PASS <key>            ->               (or SASL PLAIN below)
NICK <nick>           ->
USER <u> 0 * :<real>  ->
CAP END               ->                001 .. 005, 375/372/376 (MOTD)
```

Registration completes when `NICK`, `USER`, a credential and the end of capability negotiation are all present
(negotiation is "ended" if it never started). Before that, every command except `CAP`, `PASS`, `NICK`, `USER`,
`AUTHENTICATE`, `PING`, `PONG` and `QUIT` gets `451`.

| Credential | Rule |
| --- | --- |
| `PASS <key>` | The key must be valid. `NICK` must equal the nickname of the key's owner (case-insensitive), else `464`. |
| SASL `PLAIN` | `authcid` is the username, the password is the key. A wrong pair is `904`. If `NICK` differs from the account's nickname the server renames the client with a `NICK` message before the welcome. |

A failed registration is answered with `464` and the connection is closed with `ERROR`. Repeated failures from one
address, or for one account from one address, are limited by the server's login-failure settings, and the number of
simultaneous connections from one address by its connection limit; both are refused with `ERROR :Closing Link:`.

A key can be used by one socket at a time; a second socket with the same key is closed with `ERROR`.

On success the server sends `001`-`004`, `005` (ISUPPORT, enough lines for the tokens below) and the message of the day
(`375`, `372`..., `376`, or `422` when none is set). The user joins the channels set to auto-join, and the client
receives `JOIN` (and topic/`NAMES` replies) for each of them.

ISUPPORT tokens: `CASEMAPPING=ascii`, `CHANTYPES=#`, `PREFIX=(ov)@+`, `CHANMODES=,,,`, `NETWORK=Basil`,
`NICKLEN=15`, `MODES=1`, `MAXTARGETS=1`, `LINELEN=512`.

### Capabilities

Only these IRCv3 capabilities are offered. `cap-notify` is implied by `CAP LS 302`; the list never changes at runtime.

| Capability | Behaviour |
| --- | --- |
| `cap-notify` | Accepted with `CAP LS 302`. |
| `sasl` | Value `PLAIN`. `AUTHENTICATE PLAIN`, then the base64 of `authzid NUL authcid NUL password`. `900`/`903` on success, `904` on failure, `906` when aborted with `*`, `907` when repeated. |
| `server-time` | Relayed messages carry `time=` (UTC, millisecond precision). |
| `message-tags` | Clients may send tags. The server does not relay client-only tags and does not support `TAGMSG`. |
| `account-tag` | Relayed messages carry `account=<username>`. |
| `account-notify` | Accepted. An account never changes during a connection, so `ACCOUNT` is never sent. |
| `away-notify` | `AWAY` from an IRC client is relayed to the other IRC clients that share a channel. An away message set from the osu! client is not relayed. |
| `extended-join` | `JOIN` carries the account and the real name (the nickname). |
| `multi-prefix` | `NAMES` and `WHO` list every status prefix of a member. |
| `userhost-in-names` | `NAMES` lists `nick!user@host`. |
| `echo-message` | The sender receives its own `PRIVMSG`/`NOTICE` back. |
| `standard-replies` | Errors the server raises on its own (such as flood control) are `FAIL`; without it they are a `NOTICE`. |

`CAP REQ` with any unknown capability is refused as a whole with `NAK`. Not offered: `invite-notify`, `batch`,
`labeled-response`, `chathistory`, `sts`, `sasl` `EXTERNAL`.

### Commands

| Command | Behaviour |
| --- | --- |
| `CAP`, `AUTHENTICATE`, `PASS`, `NICK`, `USER` | Registration as above. After registration `NICK`, `PASS` and `USER` get `462`. |
| `PING` / `PONG` | `PING` is answered with `PONG` and the same token. Any received line counts as activity; the server also sends a `PING` every 2 minutes, and a connection with no traffic for 5 minutes is closed. |
| `QUIT` | Closes the connection with `ERROR :Closing Link: <domain> (Quit: <reason>)`. |
| `JOIN` / `PART` | Comma-separated channel lists are accepted. See [Channels](#channels). |
| `PRIVMSG` / `NOTICE` | To a channel, to a nickname, or to BasilBot. A `NOTICE` never triggers a command or an away reply. |
| `NAMES` / `LIST` / `WHO` / `WHOIS` / `ISON` / `USERHOST` | Read-only queries. |
| `AWAY` | Sets (`306`) or clears (`305`) the away message, which all of the user's connections share. |
| `TOPIC` / `MODE` | Reading only. |
| `MOTD` | Repeats the message of the day. |
| `KICK`, `INVITE` | Always `482` (for an unknown channel `403`). |
| anything else, including `OPER`, `VERSION`, `TIME`, `LUSERS` | `421`. |

A missing parameter is `461`; an empty `PRIVMSG` target is `411`, empty text `412`. A line longer than 8703 bytes
(8191 for tags plus 512 for the body), or a body longer than 512 bytes, gets `417` and is dropped.

`TOPIC #c` answers `331`/`332` for a channel the client is on (else `442`, unknown `403`); `TOPIC #c :text` is `482`.
`MODE #c` answers `324` with no modes; any change or list query is `482`. `MODE <ownnick>` answers `221 +`, another
nickname `502`, and a change `501`. There are no user modes and no `+o`/`+v`: nobody is ever made an operator.

### Channels

| Channel | IRC name | Rules |
| --- | --- | --- |
| General | the configured name, for example `#osu` | Read and write rights come from the channel's configured privileges. |
| Room | `#mp_{room id}` | Opens and closes with the room. |
| Spectator | `#spec_{user id}` | Never joinable from IRC: `473`. |
| Private message | the nickname, no `#` | A message to a nickname goes to that user's private-message channel, which every connection of the user shares. |

* `JOIN` of an unknown or closed channel is `403`; of a channel the connection may not read `473`. Joining a channel the
  connection is already in does nothing.
* A room channel can be read by a connection that takes part in the room: a seated player, an observing osu!tourney
  client, the creator or a referee whose permissions include `RoomJoin`, or an account with `RoomManageAny` or
  `RoomObserve`. Writing needs the same involvement or `ChatManagePostAnyRoom`. `JOIN #mp_5` is `473` when the room exists and
  the caller may not enter, `403` when it does not exist. `LIST` lists only the room channels the connection could join.
* A creator or referee joins the room channel with an ordinary `JOIN` and stays in it after leaving a slot.
* `LIST` shows the visible general channels the connection may read, and the joinable room channels.

**Status prefixes** mirror Basil's rights and are computed per channel, never granted by IRC. `@` marks authority:
the creator and the referees of a room (`#mp_*`), and any user with `ChatManageChannels` in a general channel. `+` marks
a member who may write. A read-only member has no prefix. `PREFIX=(ov)@+` is advertised only to say which symbols
exist.

### Sending and receiving

* A message goes through `ScopedChannel.PostAsync` for a channel or a nickname; the result is mapped below. IRC does not
  echo a message to its sender unless `echo-message` is on. A message reaches every other member of the channel,
  including the same user's Bancho client, and an osu! user's message reaches IRC members the same way.
* A message longer than 450 characters is cut to 450, as for the osu! client. Chat is limited per account by the
  server's chat rate limit, shared with the osu! client; when it is exceeded the message is dropped and the client
  gets `FAIL PRIVMSG RATE_LIMITED` (with `standard-replies`) or a `NOTICE`.
* A message may contain CR or LF, which a line cannot. Every message the server delivers (chat, BasilBot replies,
  away replies) is split at line breaks into one `PRIVMSG` or `NOTICE` per non-empty line, and a line longer than the
  protocol allows is split further. An announcement from BasilBot is a `NOTICE`.
* A private message to a user whose "away" message is set makes the sender receive `301`.

| Application result | IRC reply |
| --- | --- |
| `Ok`, `AlreadyMember` (join) | nothing extra |
| `Closed` | `403` |
| `AccessDenied` on join | `473` |
| `NotMember` on part | `442` |
| `AccessDenied`, `NotMember`, `Blocked`, `TargetSilenced` on send, or a missing `ChatPost`/`ChatPostPrivate` | `404` with a text that names the reason |
| unknown nickname | `401` |
| `Empty` | `412` |
| `InvalidText` | the message is dropped |

### Presence

Channel membership in the Application is per connection: a user with a Bancho and an IRC connection has two members in
`#osu`. Other IRC clients should see the user, not the connection:

```text
a connection joins a channel
  the user already has another connection in it?   no line for other clients
  otherwise                                        JOIN to the channel's IRC members

a connection leaves a channel
  the user keeps another connection in it?         nothing
  else the user is still online elsewhere?         PART to the channel's IRC members
  else (the user went offline)                     nothing here; one QUIT instead

a user goes offline
  one QUIT to every IRC client that shared a channel with the user
```

A client never receives both `PART` and `QUIT` for one disconnect. A connection removed from a channel by the server
(the channel closed, or its read privileges were raised) receives a `KICK` from the server. The joining connection always
receives its own `JOIN`, the topic and `NAMES`.

### Shutdown

On shutdown or restart the server refuses new IRC connections, sends every client a `NOTICE` ("Server is shutting
down" or "Server is restarting"), then `ERROR :Closing Link: <domain> (Server shutting down)` (or `restarting`) and
closes the socket.

## Design / Implementation

### Host rules

* The host takes the actor from the connection that opened the socket and works through scopes
  (`channel.ScopeTo(connection)`); it holds no chat rule of its own.
* One handler class per event type delivers chat to IRC (message, join, part, closed channel, configured channel,
  closed connection), like the Bancho host. Delivery is done by the event dispatcher, not by the command that caused it,
  so a command only calls the Application and answers errors. The one exception is `away-notify`: changing the away
  message publishes no event, so the `AWAY` command relays it to the interested clients itself.
* The IRC host writes each client's lines from one writer queue, so lines of different events never interleave inside a
  line.
* The wire format is in `Basil.Protocol.Irc`: lines, IRCv3 tags (escaping per the message-tags specification),
  numerics and user prefixes. It gives no meaning and does not limit line length.

### Rationale

* **One model, thin transport.** A rule added to the Application holds on every transport.
* **Keys, not passwords.** A client library or bot stores its credential in plain text and sends it unencrypted; a
  per-device key limits what a leak costs and can be revoked alone.
* **No management over IRC.** Channels and rooms already have an API and BasilBot commands with permission checks;
  duplicating them as IRC modes would be a second, weaker surface.
* **BasilBot is a user.** The bot is account 1 with a `BotConnection`, so it appears in `WHO`, `NAMES` and `WHOIS` and
  can be messaged like any user. It has no socket, and nobody can log in as it.

### Invariants

* No IRC line, to any client, contains a real address.
* An IRC connection is never seated in a room and never spectates.
* The credential for IRC is an IRC key, never the password.
* A client sees one `JOIN`/`PART`/`QUIT` per user, not per connection.

## Related code

* [`Connection.cs`](../../src/Application/Users/Connections/Connection.cs): connection kinds
* [`UserRegistry.cs`](../../src/Application/Users/Users/UserRegistry.cs): `LoginWithTokenAsync`, idle sweep
* [`ScopedUserSessions.cs`](../../src/Application/Users/Users/ScopedUserSessions.cs): issuing an IRC key
* [`Channel.cs`](../../src/Application/Chat/Runtime/Channel.cs), [`ScopedChannel.cs`](../../src/Application/Chat/Runtime/ScopedChannel.cs): membership, posting, recipients
* [`RoomChannel.cs`](../../src/Application/Chat/Runtime/RoomChannel.cs): room channel access
* [`IrcMessage.cs`](../../src/Protocol/Irc/IrcMessage.cs), [`IrcTag.cs`](../../src/Protocol/Irc/IrcTag.cs), [`IrcNumericReply.cs`](../../src/Protocol/Irc/IrcNumericReply.cs), [`IrcUserPrefix.cs`](../../src/Protocol/Irc/IrcUserPrefix.cs): wire format
* [`src/Hosts/Irc`](../../src/Hosts/Irc): listener, session, commands and delivery

## See also

* [`multiplayer.md`](multiplayer.md): room authority, seating and the empty-room timer
* [`../for-client/irc-client.md`](../for-client/irc-client.md): how a user connects an IRC client
* [`../for-technicians/https.md`](../for-technicians/https.md): why IRC does not use HTTPS/TLS
