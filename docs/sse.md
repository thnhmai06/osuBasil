# Live updates (Server-Sent Events)

## Overview

Tournament overlays, dashboards, and OBS browser sources all want to know "what's happening in this match right now" without hammering the API with polling requests. Basil answers that with Server-Sent Events (SSE): a client opens one connection per resource and gets pushed every change as it happens.

## Why SSE instead of polling or WebSockets?

Polling every second per client, per resource, adds load that scales with viewer count for no real benefit: most seconds nothing changes. A push model fixes that directly.

WebSockets would also work, but they're bidirectional and stateful in ways this use case doesn't need: every one of these channels is server-to-client only, and HTTP/2 already lets a browser multiplex many SSE connections over one underlying connection without hitting the old per-origin connection ceiling. SSE gets the "push updates" behavior WebSockets would provide, with a plain HTTP response instead of a second protocol to support.

## Contract

- **Every live channel is a dedicated path ending in `/live`**, sitting next to a plain-JSON base resource with the same shape (`GET /matches/{id}` and `GET /matches/{id}/live`, `GET /matches/{id}/settings` and `.../settings/live`, and so on). No route ever branches on the `Accept` header, so a client always knows which kind of response it's going to get from the URL alone.
- **The first event on any connection is a full snapshot.** Every event after that is a partial update carrying only the fields that changed (an [RFC 7396 JSON Merge Patch](https://www.rfc-editor.org/rfc/rfc7396) against the previous event). This is computed per connection, so a client that connects late still gets a full snapshot first, regardless of what earlier clients have already received.
- **A `/live` route on a resource that isn't currently live returns `409 Conflict`** as a normal enveloped JSON error, instead of ever opening the stream.
- Some resources have no meaningful one-shot form at all. `GET /matches/{id}/live/{slotIndex}` is SSE-only, since "whoever is currently sitting in this slot" doesn't have a sensible snapshot outside of a live connection.
- **A match's chat (`GET /matches/{id}/chat/live`) is the one channel that carries events rather than state**, and the only one with neither a snapshot nor a JSON sibling: chat is never stored, so a subscriber receives what is said from the moment it connects and nothing earlier. Every line reaching the room is carried, whoever said it and however they're connected — an osu! client, an IRC client, or BasilBot answering a command. It is also the only live channel gated on the admin key, which is worth knowing because the key travels in the `Authorization` header and a browser's built-in `EventSource` cannot set one.

## Concepts

- **`IMatchLiveEvents`** and **`IPlayerInputEvents`** are the two publish points every live channel is built on: plain C# events, one scoped to match state, one scoped to a single player's spectator input. Publishing is just raising an event; each SSE connection owns a small buffer that a non-blocking write lands in, so a publisher (which may be holding `MatchSession.Lock` at the time, see [`bancho.md`](bancho.md)) never blocks on a slow reader.
- **The envelope skips SSE routes.** Every other `api.` host JSON response gets wrapped in the standard envelope (see [`response-envelope.md`](response-envelope.md)), but a live stream's `Content-Type` is never `application/json`, so the enveloping middleware recognizes the route by its `/live` path segment and passes it straight through unbuffered. Buffering a stream until the response "completes" would silently turn a live push into one that never delivers anything until the connection closes.
- **A synchronous error before the stream opens is not an SSE payload.** A `409`/`404` returned before any event is sent is a genuine JSON body and gets hand-enveloped, since the middleware's buffering skip only applies to routes that are already streaming.

## Lifecycle

```
Client                               Server
  | GET /matches/{id}/live              |
  |------------------------------------>|
  |                                     | subscribe to IMatchLiveEvents for this match
  |                                     | read the current state -> full snapshot
  | event: full snapshot                |
  |<------------------------------------|
  |                                     | (later) a packet handler mutates match state
  |                                     | publish -> merge patch against last-sent state
  | event: {changed fields only}        |
  |<------------------------------------|
```

## Design

**Why merge patches instead of resending the full state every time?** Most changes touch one or two fields (a slot's ready flag, the countdown's remaining seconds). Sending the whole object on every tick wastes bandwidth for no benefit to a client that already has everything else.

**Why compute the patch per connection instead of broadcasting one shared patch?** A client that connects mid-match has no prior state to diff against: its first event has to be a full snapshot regardless of what any other client received. Computing the diff per connection, against that connection's own last-sent state, is what makes "connect anytime, always get a correct starting point" possible without a separate reconnection protocol.

**Why 409 instead of just not registering the route?** The route always exists: a match id is valid or it isn't, live or not, independent of whether a stream can currently be served from it. Returning a normal JSON error keeps that distinction visible to a caller instead of a plain connection failure.

## Related code

- `Basil.Web/Routing/Api/LiveSseRoutes.cs`
- `Basil.Web/Middleware/EnvelopeMiddleware.cs`
- `Basil.Application/Sessions/Multiplayer/IMatchLiveEvents` (in `MatchLiveSnapshotBuilder.cs`)
- `Basil.Application/Sessions/Spectating/IPlayerInputEvents`

## See also

- [`multiplayer.md`](multiplayer.md): the tournament match report these channels stream live updates for
- [`response-envelope.md`](response-envelope.md): the JSON shape every non-SSE route follows
- [`bancho.md`](bancho.md): the packet handlers that publish the state these channels stream
