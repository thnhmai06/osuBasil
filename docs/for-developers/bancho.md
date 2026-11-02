# Bancho dispatch

## Overview

Basil's Bancho transport receives batches of binary packets through the osu! stable HTTP long-poll endpoint.

The client-facing packet format is documented in [`protocol.md`](../for-client/bancho/protocol.md). This document describes
how `Basil.Host.Bancho` turns those packets into operations on the Application objects, and how the answers get back to
the client.

The pipeline for one request is:

```text
HTTP request on c.<domain> (or ce, c4, c5, c6)
    ↓
no osu-token?  ──── yes ──► BanchoLogin (a login) ──► cho-token header + first reply
    ↓ no
open osu!/tourney connection for the token?  ── no ──► RestartServer packet (the client reconnects)
    ↓ yes
PacketDispatcher ──► PacketHandlers ──► scoped Application objects
    ↓
Outbox of that connection drained ──► HTTP response
```

The host holds no business rule, permission rule or state of its own. A handler reads the packet, takes the actor from the
connection that sent it, and calls an operation on a scope of the object acted on (`x.ScopeTo(connection)`).

---

## Why long polling shapes the design

Bancho is a long-poll protocol rather than a request/response API. A client sends the packets it has and receives the
packets that were waiting for it. Whatever the server wants to tell a client has to be queued until that client's next
request. Nothing a handler does can assume that a packet it queues for another player is delivered during the current
HTTP request.

---

## PacketDispatcher

`PacketDispatcher` processes the request body sequentially.

1. It splits the body into packets (`PacketReader.Split` of `Basil.Protocol.Bancho`). A body that is malformed ends the
   request quietly; nothing is applied.
2. It gives each packet to `PacketHandlers`, in order.
3. If handling a packet throws, the error is logged with the packet type and the user, and the next packet still runs.

An unknown packet type, or a packet the connection may not send, is skipped. The principle is:

```text
one bad packet ≠ failed batch
```

This does not hide programming errors: the failure is logged at error level. Ordinary refusals are not exceptions; they
are the outcomes of the Application operations (see below).

---

## PacketHandlers

`PacketHandlers` is one class (split into partial files by feature: chat, multiplayer, spectating) whose `HandleAsync` is a
`switch` on the packet type. It is not one class per packet: a handler is a few lines that read the payload and call one
operation, so a class each would only add files.

* Packets any osu! or tournament connection may send (logout, stats and presence requests, tournament match info and
  channel joins) are handled first.
* Packets of a regular osu! client (`BanchoConnection`: status, friends, away message, channels, messages, lobby, spectating,
  multiplayer) follow; a tournament client does not send them.
* The multiplayer packets map onto the room's operations (`room.ScopeTo(connection)` and its parts: members, settings,
  authority, slots, rounds).

### Restricted accounts and permissions

The handlers do not know about restrictions. A restricted account has its **effective** permissions reduced, and the
Application objects check them: a scoped operation returns a `Result<TOutcome>` whose missing mask says what the caller
lacks. The handler turns a refusal into the packet the client expects (or ignores it). Permissions are never decided by
the kind of connection. See [`identity-permissions.md`](../for-agents/identity-permissions.md) and
[`privileges.md`](privileges.md).

---

## Outgoing packets

Handlers do not construct the HTTP response directly. Packets for a client are queued in the `Outbox`, a per-connection
queue, and the next request of that connection drains it.

```text
Incoming packet
      ↓
Handler ──► scoped Application operation ──► state change, event published
                                                   ↓
                              Delivery handler for that event
                                                   ↓
                              Outbox.Send(recipients, write)
                                                   ↓
                              next poll of each recipient ──► HTTP response
```

* Application publishes the event (a user came online, a member joined a channel, a room changed). The `Delivery` handlers of
  the host (`UserDelivery`, `ChatDelivery`, `RoomDelivery`) are the only consumers that know the packet form; they build the
  bytes once and queue them for every open osu! client among the recipients.
* `RoomAudience` says who hears about a room: its players, its tournament observers and, for the lobby view, the clients
  watching the lobby.
* Connections that are not osu! clients (API, IRC) are skipped by the `Outbox`; they get the same events through their own
  transports.

---

## Handler conventions

When adding or modifying a handler:

* keep packet reading and the packet-specific reply in the handler;
* call an operation on a scope of the object acted on; do not add a rule that belongs to the object;
* take the actor from the connection of the request, never from the packet;
* queue answers through the `Outbox` or a `Delivery` handler;
* do not assume that outgoing packets are delivered immediately;
* preserve the packet ordering the client relies on.

If the operation changes shared multiplayer state, follow the room rules below.

---

## Room concurrency

`Room` is mutable shared state, and Basil runs on the thread pool, so two packets for the same room can run at once.

**An operation that reads and then mutates a room is a synchronous method of `Room` that holds the room's own `Lock`
across the whole state change and publishes its event inside it.** `ScopedRoom` only checks permissions and calls it. A
closed room answers `RoomClosed`. Do not introduce a second synchronization mechanism for the same state, and never `await`
under the room lock.

```text
Handler A                    Handler B
    |                            |
ScopedRoom (permission)     ScopedRoom (permission)
    |                            |
Room.Operation ── Lock ──┐       |
    mutate, publish      │       |
    ── unlock ───────────┘  Room.Operation waits for the lock
```

The lock gives serialization per room without serializing unrelated rooms. The race is covered by
`tests/Application/Multiplayer/Objects/Rooms/RoomConcurrencyTests.cs`. The complete model is in
[`multiplayer.md`](multiplayer.md); see it before changing room permissions.

---

## Room state and SSE

The same room events that feed the Bancho packets also feed the HTTP live streams: both consume the events of the one room
object rather than keeping copies of its state. The ordering that matters is that a state change publishes its event while
the room lock is held, so a subscriber never sees an older state after a newer one. The SSE side is documented in
[`sse.md`](sse.md).

---

## Adding a packet

1. Find the packet type in `ClientPacketType` and the payload layout in `Basil.Protocol.Bancho` (add the reader or model there
   when the layout is new, with a protocol test: packet layouts are wire contracts).
2. Add a case to `PacketHandlers` (in the partial file of its feature) that reads the payload and calls the operation on the
   scope of the object acted on.
3. If the operation does not exist yet, add it to that object in Application, with its permission check and its outcome,
   and a test there.
4. If clients must hear about the change, publish an Application event and add or extend a `Delivery` handler.
5. Add a host test in `tests/Hosts/Bancho` that sends the packet from a simulated client and checks what the client
   receives.

Do not introduce transport-specific persistence or business logic into the handler.

---

## Failure handling

A packet that cannot be dispatched because it is unknown or not allowed for the connection does not prevent the rest of
the request from being processed; a malformed body is ignored; a handler that throws is logged and the batch goes on.
The dispatcher is not a catch-all for expected failures: those are outcomes, not exceptions.

A request whose token no longer names an open osu! or tournament connection (after a restart, or when the server closed
the connection) is answered with the `RestartServer` packet, which makes the client log in again.

---

## Related code

| Component                  | Location |
|----------------------------|----------|
| Routes, request flow       | [`BanchoRoutes.cs`](../../src/Hosts/Bancho/BanchoRoutes.cs) |
| Login                      | [`BanchoLogin.cs`](../../src/Hosts/Bancho/Login/BanchoLogin.cs) |
| Packet dispatcher          | [`PacketDispatcher.cs`](../../src/Hosts/Bancho/Packets/PacketDispatcher.cs) |
| Packet handlers            | [`PacketHandlers.cs`](../../src/Hosts/Bancho/Packets/PacketHandlers.cs), [`PacketHandlers.Multiplayer.cs`](../../src/Hosts/Bancho/Packets/PacketHandlers.Multiplayer.cs) |
| Outgoing queue             | [`Outbox.cs`](../../src/Hosts/Bancho/Support/Outbox.cs) |
| Event delivery             | [`UserDelivery.cs`](../../src/Hosts/Bancho/Delivery/UserDelivery.cs), [`ChatDelivery.cs`](../../src/Hosts/Bancho/Delivery/ChatDelivery.cs), [`RoomDelivery.cs`](../../src/Hosts/Bancho/Delivery/RoomDelivery.cs) |
| Wire format                | [`src/Protocol/Bancho`](../../src/Protocol/Bancho) |
| Room runtime state         | [`Room.cs`](../../src/Application/Multiplayer/Objects/Rooms/Room.cs) |
| Room race tests            | [`RoomConcurrencyTests.cs`](../../tests/Application/Multiplayer/Objects/Rooms/RoomConcurrencyTests.cs) |
| Host tests                 | [`tests/Hosts/Bancho`](../../tests/Hosts/Bancho) |

---
