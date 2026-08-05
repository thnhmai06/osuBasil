# Authentication

## Overview

osu! stable doesn't have a login *packet*. Logging in is a plain HTTP request with no session token attached, and that one request both authenticates the player and boots their entire session (privileges, channel list, everyone else's presence). Everything else in the protocol assumes a session already exists, so this is the flow to understand before touching packet dispatch.

## Why

A normal client/server handshake would open a connection, exchange a hello, then authenticate. osu! stable instead treats "no session token on this request" as the login signal itself: the client's very first `POST /` doubles as both "log me in" and "give me a token to use from now on." There's no separate connect step, so the login body has to carry everything the server needs up front: credentials, client version, and enough hardware detail to catch a banned player creating a new account.

## Contract

- **Request**: `POST /` to the bancho host (`c.`/`ce.`/`c4.`/`c5.`/`c6.<domain>`), no `osu-token` header. The body is the client's raw login block: username, password hash, and a pipe-delimited line of version/locale/hardware fields.
- **Response on success**: a `cho-token` response header carrying the new session token, and a body of concatenated bancho packets: protocol version, login reply, privileges, the auto-join channel list, and the presence/stats of every other online player.
- **Response on failure**: a login-reply packet carrying a failure reason. No `cho-token` header.
- **Every later request** carries the issued token in an `osu-token` header. A request with an unrecognized token gets told to reconnect, since the server has already forgotten that session (a restart, most commonly).

## Concepts

- **One session per account.** Logging in again while already online evicts the previous session, so a player only ever has one active connection. Tourney spectator clients are the one exception: a player can have several of those open alongside their main client.
- **Hardware fingerprint.** Every login records the client's OS path hash, network adapter hashes, uninstall id, and disk signature. This is compared against known banned accounts' fingerprints, but only for accounts that aren't `Verified` yet. An established player is never blocked by a fingerprint match, only a fresh signup.
- **`Verified` is earned, not requested.** The privilege flag is added automatically on a user's first successful login (see [`privileges.md`](privileges.md)); nothing about the login request itself asks for it.

## Lifecycle

```
Client                         Server
  |  POST / (no osu-token)         |
  |------------------------------->|
  |                                | resolve caller IP
  |                                | parse login body
  |                                | reject if adapters are empty
  |                                | evict existing session (unless tourney spectator)
  |                                | verify username + password
  |                                | hardware-ban check (unverified accounts only)
  |                                | build the session + packet bundle
  |  cho-token header + packets    |
  |<-------------------------------|
```

A rejected login (bad credentials, empty hardware adapters, a hardware-ban match) short-circuits before the session is built and returns only a failure packet. No token is ever issued for a login that doesn't succeed.

## Design

- **Why reject empty adapters?** A client reporting no network adapters at all (and not running under Wine) is a strong signal of a doctored client rather than a real install, so it's rejected before any database lookup happens.
- **Why does hardware banning skip verified accounts?** A shared IP or shared hardware (LAN tournament venues are the common case) can plausibly match a banned fingerprint without any wrongdoing. Blocking every match would lock out real players at every event; skipping already-verified accounts keeps the check aimed at fresh signups instead.
- **Why "one session per account, except tourney spectators"?** Tourney spectator clients are meant to run several at once, alongside the player's real client, to watch a match from multiple angles. Every other client type closes its older session on relogin, so nothing accumulates.
- **Why not derive the country from the request?** Basil runs fully offline with no geolocation provider, and tournament variables shouldn't depend on a proxy's guess. The country shown on a player's presence — and on BasilBot — is the one stored on the user record, never one resolved from request headers at login.

## Examples

Raw login body (three logical lines, `\n`-separated):

```
PlayerOne
5f4dcc3b5aa765d61d8327deb882cf99
b20250101|1|1|a1b2c3...|0
```

The third line's pipe-delimited fields are, in order: client version string, UTC offset, "display city" flag, comma-joined hardware hashes, and "accept private messages" flag.

## Related code

- `Basil.Web/Routing/Bancho/BanchoProtocolRoutes.cs`
- `Basil.Application/Services/Authentication/LoginService.cs`
- `Basil.Application/Abstractions/Login/LoginForm.cs`
- `Basil.Domain/Login/Geolocation.cs`

## See also

- [`bancho.md`](bancho.md): what happens after login, once packets start flowing
- [`privileges.md`](privileges.md): the flags a session carries
- [`run-deployment.md`](run-deployment.md): how a new account actually gets created
