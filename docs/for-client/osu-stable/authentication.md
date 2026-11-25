# Authentication

## Overview

osu! stable does not use a separate login packet.

Login is performed through a normal HTTP request. The first request to the osu! (stable) server authenticates the player and creates the player's session.

After a successful login, the server returns a session token. The client sends this token with subsequent requests.

---

## Login request

Send:

```http
POST /
```

to one of the osu! (stable) hosts:

```text
c.<domain>
ce.<domain>
c4.<domain>
c5.<domain>
c6.<domain>
```

The login request **must not** contain an `osu-token` header.

The request body contains three newline-separated lines:

```text
username
password
client information
```

Example:

```text
PlayerOne
5f4dcc3b5aa765d61d8327deb882cf99
b20250101|1|1|a1b2c3...|0
```

The third line contains client and hardware information separated by `|`:

```text
client version
UTC offset
display city flag
hardware hashes
private-message preference
```

The exact request format is part of the osu! stable protocol and should not be changed without considering client compatibility.

---

## Successful login

A successful login returns:

```http
cho-token: <session-token>
```

The response body contains osu! (stable) protocol packets that initialize the client session, including:

* protocol version;
* login result;
* account privileges;
* automatically joined channels;
* online-player presence;
* online-player statistics.

The client must store the `cho-token` and send it with subsequent osu! (stable) requests.

---

## Subsequent requests

After login, include:

```http
osu-token: <session-token>
```

on every osu! (stable) request that requires an authenticated session.

For example:

```http
POST /
osu-token: <session-token>
```

The session token is issued by Basil and is not the user's password.

If Basil no longer recognizes the token, the client must reconnect and authenticate again.

This normally happens after the server restarts, because the connections of osu! clients are held in memory.

---

## Failed login

A failed login returns a login-reply packet containing the failure reason.

No `cho-token` is returned.

Common reasons include:

* invalid username or password;
* invalid or incomplete client information;
* too many failed attempts for the account;
* the account already being signed in elsewhere;
* a tournament client (`osu!tourney`) on an account without the privilege to use one;
* the server stopping.

A failed login does not create an authenticated session.

---

## Session behaviour

### One active session per account

Normally, an account can have only one active game session.

Logging in again with the same account is refused while the previous session is still in use ("User already logged
in."); once the previous session has been silent for 30 seconds, the new login ends it and takes its place.

Tourney spectator clients are an exception and may run alongside the player's normal game session.

### Session lifetime

An osu! session lives in server memory. It ends after 60 seconds without a request from the client, and with a server
restart. A client that presents a token the server does not hold, as after a restart, or whose session has ended, is told
to restart and logs in again with its saved password.

---

## Hardware information

The login request carries the client version and the client's hardware hashes.

Basil keeps them with the session, as part of the login history that moderators can read. It does not compare them with
other accounts and never refuses a login because of them: Basil has no hardware ban and does not judge who owns which
account, so players on shared hardware or networks, such as a LAN tournament, sign in as usual.

---

## Account verification

Basil has no account-verification step. A new account receives the default privilege set when it is created, and the
client does not request or receive a verification privilege during login.

See [`privileges.md`](../../for-developers/privileges.md) for the roles the client displays.

---

## Country

The country displayed for a player is taken from the account's stored country value.

It is not determined from the client's IP address during login.

This means that the country shown by the client remains the value configured for the account, regardless of the network from which the player connects.

---

## Login flow

```text
Client                              Basil
  |                                   |
  | POST /                            |
  | no osu-token                      |
  |---------------------------------->|
  |                                   |
  |                                   | Authenticate credentials
  |                                   | Validate client information
  |                                   | Check the account silence
  |                                   | Create session
  |                                   |
  | cho-token + login packets         |
  |<----------------------------------|
  |                                   |
  | POST /                            |
  | osu-token: <session-token>        |
  |---------------------------------->|
  |                                   |
  |              ...                  |
```

If authentication fails:

```text
Client                              Basil
  |                                   |
  | POST /                            |
  | no osu-token                      |
  |---------------------------------->|
  |                                   |
  | login failure packet              |
  |<----------------------------------|
```

No session token is issued for a failed login.

---

## See also

* [`protocol.md`](protocol.md): osu! (stable) protocol after authentication
* [`getting-started.md`](getting-started.md): connecting an osu! client to Basil
* [`privileges.md`](../../for-developers/privileges.md): account privilege flags
