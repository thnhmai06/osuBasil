# Live updates (Server-Sent Events)

## Overview

Basil provides live match updates through **Server-Sent Events (SSE)**.

SSE allows tournament overlays, dashboards, browser sources, and other HTTP clients to receive changes as they happen without repeatedly polling the API.

Each live resource has its own `/live` endpoint.

---

## Live endpoints

A live endpoint is identified by its URL.

For resources that have both a normal JSON representation and a live representation, the live endpoint is the same resource path with `/live` appended.

For example:

```text id="u0cq6m"
GET /matches/{id}
GET /matches/{id}/live

GET /matches/{id}/settings
GET /matches/{id}/settings/live
```

The client does not need to use the `Accept` header to select SSE.

The `/live` URL always identifies a live SSE endpoint.

---

## Connecting

Open a `GET` request to the desired `/live` endpoint.

For example:

```http id="x4v5l8"
GET /matches/123/live
```

The server keeps the response open and sends events as the resource changes.

The response uses the SSE content type:

```http id="x1z5gc"
Content-Type: text/event-stream
```

---

## Initial snapshot

The **first event** sent after connecting is a complete snapshot of the current resource.

This allows a client to initialize its local state without making a separate request first.

For example, a client connecting to:

```text id="9r0w4u"
GET /matches/123/live
```

receives the current match state as its first event.

A client that connects after a match has already changed still receives a complete snapshot of the current state.

---

## Updates

After the initial snapshot, events contain only fields that changed.

Each update is an [RFC 7396 JSON Merge Patch](https://www.rfc-editor.org/rfc/rfc7396) against the state represented by the previous event.

A client should therefore:

1. parse the initial snapshot;
2. store it as the current state;
3. apply each subsequent event as a JSON Merge Patch;
4. treat the resulting object as the new current state.

For example, if the initial state is:

```json id="8bh5v1"
{
  "name": "Final",
  "status": "running",
  "maxPlayers": 16
}
```

and the next event contains:

```json id="l7y8u3"
{
  "status": "finished"
}
```

the resulting client state becomes:

```json id="yt1f8h"
{
  "name": "Final",
  "status": "finished",
  "maxPlayers": 16
}
```

The same patch is sent to every connection subscribed to that resource. A connection that joins later still gets a
correct starting point, because its own first event is always a full snapshot read at connect time — not a patch — so
it does not matter that the patch wasn't computed specifically for it.

A resource with no actual change does not emit an empty patch. If nothing observable changed, no event is sent.

---

## Gap events

```text id="sl0t1v"
GET /matches/{id}/live/{slotIndex}
```

streams score, input, and slot updates for one match slot on a single connection. If a client falls too far behind on
this stream, the server drops the oldest queued updates rather than growing the backlog without bound, and sends a
`gap` event to mark that a drop happened:

```http id="gap0ev"
event: gap
data:
```

The `gap` event carries no meaningful payload — its event type alone is the signal. A client that sees `gap` should
treat its state on this stream as possibly stale (for example, by re-fetching the current slot state) rather than
assuming every prior event was delivered.

Other live streams are not bounded this way.

---

## Resources without a snapshot

Some live resources exist only as live streams and do not have a corresponding one-shot JSON resource.

For example:

```text id="at3x4b"
GET /matches/{id}/live/{slotIndex}
```

is an SSE-only resource.

Clients should treat these endpoints as streams rather than expecting a normal `GET` representation.

---

## Chat stream

Match chat is a special case:

```text id="xqj0bv"
GET /matches/{id}/chat/live
```

Chat events represent messages rather than a persistent resource state.

There is no initial snapshot because Basil does not retain match chat history.

A subscriber receives messages sent **after it connects**. Messages sent before the connection are not replayed.

Messages can originate from:

* osu! clients;
* IRC clients;
* BasilBot.

The chat live endpoint is also the only live endpoint that requires the admin key.

Send the key using:

```http id="g0m24b"
Authorization: Bearer <key>
```

Because the standard browser `EventSource` API cannot set arbitrary request headers, browser clients that need this endpoint should use an alternative SSE implementation that supports custom headers.

---

## Errors before the stream starts

A live endpoint can return a normal HTTP error instead of opening an SSE stream.

For example, requesting a live endpoint for a resource that is not currently live returns:

```http id="q6c5v0"
409 Conflict
```

The response is a normal JSON response using the standard Basil response envelope.

It is **not** an SSE event.

Likewise, a resource that does not exist returns its normal HTTP error response.

Therefore, clients should check the HTTP response before attempting to parse the response as an SSE stream.

---

## Connection lifecycle

A typical connection looks like:

```text id="4w8o2j"
Client                              Basil
  |                                   |
  | GET /matches/123/live             |
  |---------------------------------->|
  |                                   |
  | SSE: full snapshot                |
  |<----------------------------------|
  |                                   |
  |                                   | Match changes
  |                                   |
  | SSE: JSON Merge Patch             |
  |<----------------------------------|
  |                                   |
  |                                   | Match changes
  |                                   |
  | SSE: JSON Merge Patch             |
  |<----------------------------------|
  |                                   |
  |              ...                  |
```

The connection remains open while the live resource is active.

A match-scoped live connection closes as soon as its match closes — the server ends the stream rather than leaving it
to the client to notice the resource is gone.

Clients should be prepared for the connection to close and should reconnect when appropriate.

---

## Client implementation notes

A client consuming a live resource should:

* treat the first event as the complete initial state;
* apply later events as RFC 7396 JSON Merge Patches;
* not assume that every event contains all resource fields;
* handle HTTP errors before starting SSE parsing;
* reconnect if the SSE connection is unexpectedly closed;
* avoid making a separate initial `GET` solely to obtain state when the `/live` endpoint already provides the initial snapshot.

---

## See also

* [`response-envelope.md`](response-envelope.md): JSON response format for non-SSE endpoints
* [`overview.md`](overview.md): the `api.<domain>` HTTP surface
* [`multiplayer.md`](../../for-developers/multiplayer.md): match state exposed by the live endpoints
* [`sse.md`](../../for-developers/sse.md): server-side SSE implementation
