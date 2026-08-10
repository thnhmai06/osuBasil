# Bancho protocol

## Overview

After authentication, osu! stable communicates with Basil through the Bancho HTTP protocol.

The protocol uses **HTTP long-polling** rather than a persistent TCP connection. The client sends binary packets in HTTP requests and receives binary packets in HTTP responses.

See [`authentication.md`](authentication.md) for how to obtain a session token.

---

## Long-polling

osu! stable does not maintain a persistent socket connection to the Bancho server.

Instead, the client repeatedly sends:

```http id="v2l7fq"
POST /
osu-token: <session-token>
```

to one of the Bancho hosts:

```text id="k0sj7m"
c.<domain>
ce.<domain>
c4.<domain>
c5.<domain>
c6.<domain>
```

Each request may contain zero or more packets.

The server responds with packets that are currently queued for the client.

The client repeats this request continuously while it is connected.

An empty response body means that there are currently no packets waiting for the client.

---

## Request format

The request body consists of zero or more Bancho packets concatenated together.

Each packet has the following structure:

```text id="q4g2jc"
uint16  Packet ID
uint8   Reserved
uint32  Payload length
bytes   Payload
```

Multiple packets are placed directly next to each other:

```text id="qf1g3v"
[packet][packet][packet]...
```

The packet ID determines how the payload should be interpreted.

The payload format depends on the packet type.

---

## Response format

The response uses the same packet framing as the request.

A response may contain multiple packets:

```text id="l0c5av"
[packet][packet][packet]...
```

The packets represent events and state updates for the client, including things such as:

* chat messages;
* channel updates;
* player presence;
* player statistics;
* multiplayer state;
* spectating updates.

The client must process every packet in the response in order.

An empty response body is valid and means that there are no pending packets.

---

## Session token

Every request after authentication must include the session token returned by the server during login:

```http id="l9khb4"
osu-token: <session-token>
```

The token identifies the client's active session.

Do not confuse the `osu-token` with:

* the account password;
* the admin key;
* the `cho-token` response header used when establishing the session.

See [`authentication.md`](authentication.md) for the login flow.

---

## Unknown or expired sessions

Sessions are stored in server memory.

If the server no longer recognizes the supplied `osu-token`, the client is instructed to reconnect and authenticate again.

This is expected after a Basil server restart because active sessions do not survive the restart.

A client should not continue polling with an invalid token indefinitely.

---

## Restricted accounts

Restricted accounts may have access to fewer protocol operations than unrestricted accounts.

Unsupported or disallowed operations do not prevent the rest of a packet batch from being processed.

See [`privileges.md`](../../for-developers/privileges.md) for account privilege behaviour.

---

## Protocol flow

```text id="f8m2ys"
Client                              Basil
  |                                   |
  | POST /                            |
  | osu-token: <token>                |
  | binary packets                    |
  |---------------------------------->|
  |                                   |
  | binary packets                    |
  |<----------------------------------|
  |                                   |
  | POST /                            |
  | osu-token: <token>                |
  |---------------------------------->|
  |                                   |
  |              ...                  |
```

The client repeats the polling cycle for as long as the session remains active.

---

## Related documentation

* [`authentication.md`](authentication.md): obtaining and using a session token
* [`getting-started.md`](getting-started.md): connecting osu! stable to Basil
* [`overview.md`](../api/overview.md): Basil's HTTP API
* [`sse.md`](../api/sse.md): receiving match updates through Server-Sent Events
* [`bancho.md`](../../for-developers/bancho.md): server-side Bancho packet handling
