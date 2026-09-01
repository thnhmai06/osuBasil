# Live updates pipeline

## Overview

Basil's live API uses **Server-Sent Events (SSE)** to push changes to clients without polling.

The client-facing wire contract is documented in [`sse.md`](../for-client/api/sse.md). This page describes the
server-side implementation: where live events are published, how connections maintain state, how patches are generated,
and how the response pipeline keeps SSE streams unbuffered.

The implementation has two event sources:

* [`IMatchLiveEvents`](../../src/Basil.Application/Sessions/Multiplayer/IMatchLiveEvents.cs) for match-scoped state.
* [`IPlayerInputEvents`](../../src/Basil.Application/Sessions/Spectating/IPlayerInputEvents.cs) for a player's spectator input.

Each SSE connection subscribes to the relevant event source and maintains its own last-sent state.

## Architecture

The live pipeline is:

```text
State mutation
      │
      ▼
publish event
      │
      ▼
IMatchLiveEvents / IPlayerInputEvents
      │
      ├──────────────► connection A buffer
      │
      ├──────────────► connection B buffer
      │
      └──────────────► connection C buffer
                           │
                           ▼
                     SSE response
```

A publisher never writes directly to an HTTP response.

Each connection owns its own delivery buffer, allowing state mutation to remain independent from network I/O.

This is particularly important for multiplayer state changes because a publisher may execute while holding
[`MatchSession.Lock`](../../src/Basil.Application/Sessions/Multiplayer/MatchSession.cs). A slow SSE consumer must never extend the critical section.

## Event sources

### `IMatchLiveEvents`

`IMatchLiveEvents` publishes changes associated with a specific match.

Typical publishers are services that mutate match state, such as:

* slot changes;
* host or referee changes;
* room settings;
* match timers;
* bans;
* other state exposed by the match live API.

Publishing an event only signals that the underlying state changed. The event does not contain a precomputed JSON
response or a connection-specific patch.

### `IPlayerInputEvents`

`IPlayerInputEvents` publishes spectator input associated with a single player.

The event source is scoped to the player whose input is being observed rather than to a match as a whole.

As with match events, publishers do not perform HTTP or SSE writes.

## Shared per-match state

The patch a mutation produces is computed once per match, not once per connection: a
[`SnapshotChannel<T>`](../../src/Basil.Application/Services/SnapshotChannel.cs) holds the one "last published" state for
each stream, and every publish diffs against that shared state and broadcasts the same patch bytes to every subscriber
of that stream.

The lifecycle is:

```text
connection established
        │
        ▼
read the stream's current shared snapshot
        │
        ▼
send full snapshot
        │
        ▼
event received (shared patch, already computed)
        │
        ▼
forward patch as-is
```

The first event a connection receives is therefore always a complete snapshot, read directly from the shared state at
subscribe time rather than reconstructed from patches. Subsequent events are the same merge patch every other
subscriber of that stream receives.

## Why a shared diff is safe

A merge patch is only meaningful relative to the state it was computed from. What makes a *shared* patch safe, despite
different connections joining at different times, is that a connection never has to apply a patch to a baseline it
doesn't have:

```text
Client A connected earlier ──┐
                              ├── both read the SAME shared snapshot at subscribe time
Client B connected later  ───┘
```

Every connection's very first event is a fresh read of the shared snapshot, not a patch. From that point on, both
clients hold the same state as the shared publisher, so the same subsequent patch applies cleanly to both. There is no
per-connection baseline to reconcile.

A publish that produces no observable change (nothing in the diffed state actually differs) returns no patch at all,
so a stream with no real activity emits no events — earlier revisions of this pipeline broadcast an empty `{}` patch
on every publish regardless of whether anything changed; that is no longer the behavior.

## Subscriber registry and match close

Every match-scoped live connection registers with that match's
[`SseSubscriberRegistry`](../../src/Basil.Application/Services/SseSubscriberRegistry.cs) for as long as it is open. The
registry has two states, OPEN and CLOSED, and the transition is atomic with respect to registration: a connection that
subscribes concurrently with the match closing either joins the OPEN registry and is completed moments later, or finds
the registry already CLOSED and is completed immediately — there is no window where a subscription is silently missed.

When a match closes, its registry completes every currently registered connection, ending their SSE streams without
waiting for the client to disconnect on its own. This is what makes a match's live streams bounded in lifetime by the
match itself rather than by client behavior.

The per-player spectator input stream (`/spec/{playerId}/input`) is not match-scoped and is deliberately not registered
with any match's registry — its lifetime is the player's session, not any one match.

## Snapshot consistency

A connection reads the current state before subscribing to or consuming subsequent updates according to the live route's
subscription protocol.

The implementation must preserve the snapshot-first invariant:

```text
first event = complete current state
subsequent events = changes relative to that state
```

The snapshot is not merely an optimization. It establishes the baseline from which every later patch is interpreted.

A connection must never start with a partial state reconstructed from individual events.

## Non-blocking publication

Publishing a live event must not wait for network consumers.

The event path is intentionally separated from SSE I/O:

```text
state mutation
    │
    ├── publish
    │     └── enqueue/non-blocking delivery
    │
    └── return
```

A slow or disconnected client is handled by its own connection lifecycle rather than by the publisher.

This prevents a slow HTTP connection from affecting unrelated match operations.

In particular, code executing under `MatchSession.Lock` must never await an SSE client.

## SSE response handling

SSE responses are intentionally excluded from [`EnvelopeMiddleware`](../../src/Basil.Web/Middleware/EnvelopeMiddleware.cs).

Normal `api.` JSON responses use the response envelope, but an SSE response must remain a streaming response:

```text
HTTP response
    │
    ├── JSON
    │      └── EnvelopeMiddleware
    │
    └── /live SSE
           └── streamed directly
```

The middleware recognizes live routes by their `/live` path segment.

It does not use the `Accept` header as the routing decision.

This distinction matters because buffering a streaming response until completion would prevent live events from reaching
the client while the connection remains open.

## Errors before streaming

A live route can fail before the SSE stream is established.

For example:

```text
GET /matches/999/live
        │
        ▼
match does not exist
        │
        ▼
404 JSON response
```

This is still a normal JSON response and therefore goes through the envelope middleware.

The streaming bypass applies once the route is being served as a live stream, not to every possible error returned by a
`/live` route.

The same rule applies to other synchronous errors such as `409 Conflict`.

## Route semantics

Live routes are available independently of the current state of the resource.

For example, a match can have a valid identifier but still be unsuitable for a live stream because of its current state.

The route therefore reports an application-level error rather than relying on connection failure to communicate the
condition.

This keeps resource existence and stream availability as separate concepts.

## Lifecycle

A typical match live connection follows this flow:

```text
GET /matches/{id}/live
        │
        ▼
validate match
        │
        ├── invalid/unavailable
        │       └── normal JSON error
        │
        ▼
subscribe to match events
        │
        ▼
build current snapshot
        │
        ▼
send snapshot
        │
        ▼
wait for event
        │
        ▼
build current state
        │
        ▼
diff against connection state
        │
        ├── no effective change
        │       └── continue waiting
        │
        └── changed
                │
                ▼
          send merge patch
                │
                ▼
          update connection state
                │
                └── repeat
```

Disconnecting the HTTP request terminates the connection's subscription and releases its resources.

## Design constraints

### Publishers must not know about HTTP

Application services should publish domain/application events rather than writing SSE messages directly.

This keeps packet handlers, multiplayer services, and other mutation paths independent of the transport used to observe
their state.

The same state mutation can therefore serve:

* SSE consumers;
* future live transports;
* internal observers.

### Patches must be generated from state

Publishers should not construct merge patches themselves.

A publisher knows that state changed, not what the patch relative to the previous published state should look like.

Patch generation belongs to the stream's shared [`SnapshotChannel`](../../src/Basil.Application/Services/SnapshotChannel.cs), which owns the previous snapshot for that stream.

### Backpressure is a per-stream decision

A stream that only ever needs the *latest* state does not need a bound: a slow connection just falls behind on
intermediate values and catches up when it reads. The `/matches/{id}/live/{slotIndex}` stream is different — it
carries a mix of high-frequency score/input events and lower-frequency slot events on one connection, so it is bounded
and evicts the oldest queued events once full, emitting a `gap` SSE event in their place so the client can tell that a
drop happened instead of silently missing updates.

Whether a given stream needs this treatment is decided per stream, not applied uniformly.

### Do not perform network I/O inside match locks

Any live notification mechanism must remain non-blocking from the perspective of match mutation.

If a change occurs while `MatchSession.Lock` is held, publishing must not wait for:

* socket writes;
* client consumption;
* HTTP flushes;
* disconnected-client cleanup.

## Related code

* [`Basil.Web/Routing/Api/LiveSseRoutes.cs`](../../src/Basil.Web/Routing/Api/LiveSseRoutes.cs): live SSE endpoints and connection lifecycle
* [`Basil.Web/Middleware/EnvelopeMiddleware.cs`](../../src/Basil.Web/Middleware/EnvelopeMiddleware.cs): JSON response wrapping and SSE bypass
* [`Basil.Application/Sessions/Multiplayer/IMatchLiveEvents.cs`](../../src/Basil.Application/Sessions/Multiplayer/IMatchLiveEvents.cs): match live event source
* [`Basil.Application/Sessions/Spectating/IPlayerInputEvents.cs`](../../src/Basil.Application/Sessions/Spectating/IPlayerInputEvents.cs): spectator input event source
* [`Basil.Application/Services/Multiplayer/MatchLiveSnapshotBuilder.cs`](../../src/Basil.Application/Services/Multiplayer/MatchLiveSnapshotBuilder.cs): live match state construction
* [`Basil.Application/Services/SnapshotChannel.cs`](../../src/Basil.Application/Services/SnapshotChannel.cs): shared per-stream snapshot + diff
* [`Basil.Application/Services/SseSubscriberRegistry.cs`](../../src/Basil.Application/Services/SseSubscriberRegistry.cs): per-match subscriber lifecycle, completed on match close
* [`Basil.Application/Services/BoundedSseChannel.cs`](../../src/Basil.Application/Services/BoundedSseChannel.cs): bounded-stream eviction and gap marker

## See also

* [`sse.md`](../for-client/api/sse.md): client-facing SSE contract
* [`multiplayer.md`](multiplayer.md): match state and tournament report
* [`response-envelope.md`](../for-client/api/response-envelope.md): JSON response format
* [`bancho.md`](bancho.md): protocol handlers that mutate and publish match state
