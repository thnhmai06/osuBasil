# Authentication

## Overview

osu! stable does not use a separate login packet.

Login is performed through a normal HTTP request. The first request to the Bancho server authenticates the player and creates the player's session.

After a successful login, the server returns a session token. The client sends this token with subsequent requests.

---

## Login request

Send:

```http
POST /
```

to one of the Bancho hosts:

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

The response body contains Bancho protocol packets that initialize the client session, including:

* protocol version;
* login result;
* account privileges;
* automatically joined channels;
* online-player presence;
* online-player statistics.

The client must store the `cho-token` and send it with subsequent Bancho requests.

---

## Subsequent requests

After login, include:

```http
osu-token: <session-token>
```

on every Bancho request that requires an authenticated session.

For example:

```http
POST /
osu-token: <session-token>
```

The session token is issued by Basil and is not the user's password or admin key.

If Basil no longer recognizes the token, the client must reconnect and authenticate again.

This normally happens after the server restarts because active sessions are stored in memory.

---

## Failed login

A failed login returns a login-reply packet containing the failure reason.

No `cho-token` is returned.

Common reasons include:

* invalid username or password;
* invalid or incomplete client information;
* the account being blocked by a hardware-ban check.

A failed login does not create an authenticated session.

---

## Session behaviour

### One active session per account

Normally, an account can have only one active game session.

Logging in again with the same account replaces the previous session.

Tourney spectator clients are an exception and may run alongside the player's normal game session.

### Session lifetime

Sessions are held in server memory.

A server restart therefore invalidates all existing session tokens. Clients must log in again after reconnecting.

---

## Hardware information

The login request contains hardware information used by the server for account protection.

Basil can compare the supplied hardware information with hardware associated with banned accounts.

This check is primarily intended to prevent a newly created account from bypassing an existing hardware ban.

Established verified accounts are not rejected solely because their hardware matches a banned account. This avoids incorrectly blocking legitimate players using shared hardware or networks, such as players at LAN tournaments.

---

## Account verification

A successful first login automatically gives the account the `Verified` privilege.

The client does not request this privilege during login.

See [`privileges.md`](../../for-developers/privileges.md) for the available privilege flags.

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
  |                                   | Check account restrictions
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

* [`protocol.md`](protocol.md): Bancho protocol after authentication
* [`getting-started.md`](getting-started.md): connecting an osu! client to Basil
* [`privileges.md`](../../for-developers/privileges.md): account privilege flags
