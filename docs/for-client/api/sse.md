# Live updates (SSE) for clients

Basil pushes one-way state with **Server-Sent Events** (`text/event-stream`). There is no WebSocket endpoint. The
authoritative generated reference for each route is at `https://api.<domain>/docs/`.

## Routes

| Route | Token | Carries |
|---|---|---|
| `GET /v1/matches/{id}` | optional | the match report: the match and its entries (rounds and events) in time order |
| `GET /v1/rooms/{id}/live` | required | the room view: name, settings, slots, host, referees |
| `GET /v1/users/{id}/spectate` | required | a player's in-game frames, score and status, as an osu! spectator sees them |
| `GET /v1/diagnostics` | required | server measurements for every category |
| `GET /v1/diagnostics/{category}` | required | server measurements for one category |

Only the match report works without a token. A private match is still `404` unless the caller may view it; sending a
token lets the server apply that rule.

## Events

The first event is always `snapshot` with the complete current state:

```text
event: snapshot
retry: 3000
data: {...}
```

After that, a `bundle` arrives about once a second:

```text
event: bundle
id: 12
retry: 3000
data: [{...}, {...}]
```

`data` is a JSON **array** of everything that changed during that second. Apply the items in order. A second with no
change sends a comment (`: keep-alive`) instead of an empty bundle. Data is up to about one second old.

An `id:` is present only when the payloads have one (match entries carry the index of the last entry). It is a
per-connection position, not a durable sequence: do not persist it or expect the server to replay from it, except for the
one case below.

`retry: 3000` tells a disconnected client to wait 3 seconds before reconnecting.

## Match reports

The first snapshot contains the match and every entry so far. A match that has ended never changes: its stream sends the
snapshot and ends. If you reconnect to an ended match with the `Last-Event-ID` of its last entry, the server answers
`204 No Content`, which stops a browser from reconnecting. Reconnecting without it gives the full report again.

Entries are `round` and `event` objects. A round carries its beatmap, mode, mods, team type, timestamps, verdict and the
scores received; an event carries its type, time, actor, target and detail.

## Spectate

`GET /v1/users/{id}/spectate` follows a player who is online in game. The player sees a spectator, and the usual
spectating permission, blocks and restrictions apply. The stream only watches: it joins no chat channel. The first
snapshot has the player's id, name, country and current status. Bundles then carry:

* `{ "type": "frames", "action": ..., "sequence": ..., "frames": [[button, taiko, x, y, time], ...], "score": {...} }`
* `{ "type": "status", "status": { "activity", "text", "beatmapId", "mods", "mode" } }`

The stream ends when the player goes offline or stops being spectated. Spectating a player who is not in game answers
`409` with `targetOffline`.

## Rooms and diagnostics

A room stream re-sends the room when anything visible changes: `{"type":"room","room":{...}}`. When the room closes it
sends `{"type":"closed"}` and ends. A diagnostics stream sends a new sample every second for the requested category.

## Errors, limits and shutdown

* A failure before the stream starts is a normal JSON envelope: `401` without a token, `404` for an invisible or missing
  resource, `409` for an offline player, `429` for too many open streams from one user or IP, `503` while the server is
  shutting down (with `Retry-After: 5`).
* Every open stream counts toward the server's connection limit for that user or IP.
* A client that falls behind is disconnected rather than slowing the stream for others; reconnect and take a new
  snapshot.
* On shutdown or restart every stream receives one final event and ends:

```text
event: server
data: {"state":"shuttingDown"}
```

`state` is `restarting` when the server comes back. There is nothing to resume; reconnect after the wait in `retry` or
after the server is back.

## What is not streamed

Chat has no SSE route. The API neither sends nor receives chat, and the server keeps no chat history. Match gameplay is
streamed only through spectate; the match report carries round and score summaries, not frame data.

## See also

* [`overview.md`](overview.md) — the API surface and authentication
* [`response-envelope.md`](response-envelope.md) — the JSON envelope used by errors
* [`../../for-developers/sse.md`](../../for-developers/sse.md) — the server-side design
