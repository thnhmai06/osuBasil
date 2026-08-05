# Bancho Protocol

## Overview

Once a client has a session token (see [`authentication.md`](authentication.md)), every further interaction (chat, presence, multiplayer, spectating) travels as one or more binary packets inside an HTTP long-poll. This page covers how a request becomes a dispatched packet handler, and the conventions every handler follows.

## Why

osu! stable doesn't hold a persistent socket open. The client polls `POST /` on the bancho host every second or so, carrying its `osu-token` header and any packets it wants to send; the response is whatever packets have queued up for it since the last poll. There's no connection object to track state on, so the session itself (looked up by token on every single request) is the only thing that persists between polls.

## Contract

- **Request**: `POST /` on the bancho host, `osu-token` header set to a token issued at login. The body is zero or more packets, each `uint16` packet id + `uint8` reserved byte + `uint32` payload length + payload, concatenated back to back.
- **Response**: the same packet framing, containing whatever the server has queued for that player since their last poll (chat, presence updates, match state, ...). An empty body just means nothing is waiting.
- **Unknown token**: the client is told to reconnect. This is normal after a server restart, since sessions live only in memory.
- **Restricted players** only reach a subset of handlers: the ones explicitly marked as safe for a restricted account (see Concepts below). Everything else is silently skipped.

## Concepts

- **`PacketDispatcher`** reads the body one packet at a time and looks up a handler by packet id. A packet type with no registered handler for the caller's restriction state is skipped by advancing past its payload. One unrecognized or disallowed packet never blocks the rest of the batch.
- **One handler class per packet**, grouped by feature under `Basil.Application/Packets/`: `Users/` (session lifecycle, presence, stats), `Channels/` (chat), `Spectating/`, and `Multiplayer/` (the largest group, covering match creation through scoring).
- **`AllowedWhenRestricted`** is a flag every handler declares. A restricted account (see [`privileges.md`](privileges.md)) can still log in and poll, but most handlers (chat, match join, score submission) refuse to run for them.

## Lifecycle

```
Client polls POST /  --osu-token-->  PacketDispatcher
                                          |
                                for each packet in body:
                                          |
                          known handler, allowed for this player?
                                    /              \
                                  yes                no
                                   |                  |
                            handler runs      skip payload bytes
                                   |
                     may enqueue packets for other sessions
                                   |
                        queued packets returned on
                        that session's *next* poll
```

A handler doesn't reply to its own request directly. Instead it enqueues packets onto whichever session(s) should receive them (often the caller, sometimes other players in the same match or channel), and each of those sessions picks the packets up on its own next poll.

## Design

**Why packets instead of a request/response API for gameplay?** The bancho protocol predates this project by years. It's the wire format the actual osu! stable client speaks, and Basil implements it as-is rather than replacing it, since the client can't be changed. The one-handler-per-packet-id structure mirrors that format directly instead of adding an abstraction over it.

**Why silently skip a packet instead of erroring?** A batch can contain several packets from one poll. Erroring out partway through would drop every packet after the failure point for no benefit to the client, which has no way to see an error from a skipped packet anyway (the real client never sends an intentionally invalid one).

## Multiplayer as a worked example

Multiplayer is the deepest packet flow in the codebase, and a good illustration of the handler → service → session split:

```
Client sends CREATE_MATCH
        |
PacketDispatcher -> CreateMatchHandler (Packets/Multiplayer/)
        |
MatchMembershipService.CreateAsync (Services/Multiplayer/)
        |
allocates a match id from IMatchRegistry's 64 slots
builds a MatchSession (Sessions/Multiplayer/), registers its chat channel
places the host in slot 0
```

Every later match packet handler (slot changes, ready, start, ...) acquires `MatchSession.Lock` (a per-match semaphore) before reading or mutating slot state, and holds it until the updated state has been broadcast. This lock exists specifically because ASP.NET Core runs a real thread pool: two packets for the same match can be dispatched on different threads at the same time, unlike the original Python server, which relied on a single-threaded event loop to make that impossible. `tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs` reproduces the lost-write race that happens without the lock, and shows it disappears with the lock in place.

Match state changes are also the trigger for Basil's live SSE channels. See [`sse.md`](sse.md) for how a slot mutation under the lock turns into a push to anyone watching that match.

## Related code

- `Basil.Application/Packets/PacketDispatcher.cs`
- `Basil.Application/Packets/Multiplayer/CreateMatchHandler.cs`
- `Basil.Application/Services/Multiplayer/MatchMembershipService.cs`
- `Basil.Application/Sessions/Multiplayer/MatchSession.cs`
- `tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs`

## See also

- [`authentication.md`](authentication.md): how a session gets created in the first place
- [`sse.md`](sse.md): how match state reaches HTTP clients, not just bancho ones
- [`chat.md`](chat.md): the IRC gateway, a second transport into the same chat/command layer
