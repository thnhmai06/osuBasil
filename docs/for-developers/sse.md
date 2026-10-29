# Live updates (SSE)

## Overview

Basil pushes one-way, observable state to HTTP clients with **Server-Sent Events**. There is no WebSocket route: the
API manages state, it does not carry chat, and a client that needs to watch something opens an SSE stream.

The client-facing wire contract is the generated OpenAPI page. This page explains the server design behind it.

## Why one hub

Every live route has the same shape: a root owns some state (a player, a room, a match, the server), several clients
watch it, and the state changes independently of those clients. Building one pipeline per route would duplicate
bundling, backpressure, connection limits and shutdown. `Basil.Host.Rest/Sse/SseHub.cs` is that one pipeline.

## How a stream works

```text
state changes            a root collects events
                             │
                             ▼
                     once a second: bundle, serialize once
                             │
                             ▼
              every stream of that root gets the same bytes
```

* A **root** is identified by a key such as `room:5`, `match:12`, `spectate:8` or `diagnostics:all`. The first stream
  creates it; the last stream to leave removes it and its buffer.
* The first event on every stream is `event: snapshot` with the full current state.
* Later events are `event: bundle` whose `data` is a JSON array of everything the root collected during that second.
  Data is therefore up to about one second old.
* A second with nothing to send produces an SSE comment (`: keep-alive`), not an empty bundle.
* Every frame carries `retry: 3000`. An `id:` is set to the last id of the bundle, and only events that have one (for
  example a match entry) supply it. The server never resumes from `Last-Event-ID`; ids let a client spot a gap or
  deduplicate.

The hub bundles and serializes on a one-second timer driven by `TimeProvider`, which the tests replace with a fake
clock.

## Routes

| Route | Token | Stream |
|---|---|---|
| `GET /v1/matches/{id}` | optional | the full match report, entries in time order; a private match needs a viewer who may see it |
| `GET /v1/rooms/{id}/live` | required | the room view; ends with `{"type":"closed"}` when the room closes |
| `GET /v1/users/{id}/spectate` | required | a player's gameplay, as an osu! spectator sees it |
| `GET /v1/diagnostics` and `/{category}` | required | live server measurements |

An optional-token route authenticates when it can and otherwise behaves as an anonymous viewer, which is subject to the
same visibility rules as the JSON API.

### Matches

The first event is the full report with its rounds and events. A live match keeps sending bundles with new entries. An
ended match never changes, so its stream is the snapshot and then ends; reconnecting with the `Last-Event-ID` of the last
entry answers `204`, which stops a browser from reconnecting.

### Spectate

Opening the route spectates the player as an osu! client would: the player sees a spectator, and the spectating
permission, blocks and restrictions apply. The stream only watches; the Api connection joins no chat channel. Frames and
score updates are decoded once in the Bancho host and published as Application events; every Api spectator of the player
gets the same bundle. The stream ends when the player goes offline or the spectator stops watching.

### Diagnostics

The first event is the current sample; every later second sends a new sample for the requested category, or for every
category.

## Slow consumers and limits

Each stream has a bounded queue of 8 ready-to-send chunks. A stream that falls more than 3 ticks behind is closed
instead of slowing the bundle for everyone else; the client reconnects and gets a fresh snapshot. The number of open
streams per user, or per IP for a public stream, is limited by `ServerSettings.RateLimits.Connections.PerIp`; beyond it
the route answers `429` in the JSON envelope.

## Errors before streaming

A live route validates before it starts streaming. A missing or invisible match or room, an offline player, a missing
token or too many streams produce a normal JSON response through the API envelope. Once the stream has started, an
error closes it.

## Shutdown

When the server stops taking work, new streams answer `503` with `Retry-After: 5`. On shutdown or restart the hub sends
every open stream one final event at once:

```text
event: server
data: {"state":"shuttingDown"}
```

`state` is `restarting` when the server is coming back. The hub then ends the streams. The final event is not delayed to
the next tick.

## Invariants

* Every live route uses the one hub; there is no second streaming mechanism.
* The first event of a stream is a complete snapshot; later bundles are relative to it.
* A bundle is serialized once and shared by every stream of that root.
* A root exists while it has a stream and disappears with its last stream.
* Slow streams are cut off, never allowed to delay the root or other streams.
* An open stream counts as activity for its Api session.
* No stream carries chat history or any replayable history; the server stores none.

## Related code

* `src/Hosts/Rest/Sse/SseHub.cs` — roots, bundling, streams, limits, shutdown announcement.
* `src/Hosts/Rest/Sse/SseResult.cs` — writes a stream and releases it.
* `src/Hosts/Rest/Sse/MatchRoutes.cs`, `SpectateRoutes.cs`, `DiagnosticsRoutes.cs` — the live roots.
* `src/Hosts/Rest/Routes/RoomRoutes.cs` — room live route.
* `src/Hosts/Rest/Http/RestPipeline.cs` — 503 while stopping and the shared error envelope.

## See also

* [`../for-client/api/sse.md`](../for-client/api/sse.md) — client-facing SSE usage
* [`multiplayer.md`](multiplayer.md) — match report and room state
* [`architecture.md`](architecture.md) — transports and the dependency rule
