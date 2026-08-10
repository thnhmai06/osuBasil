# Bancho dispatch

## Overview

Basil's Bancho transport receives batches of binary packets through the osu! stable HTTP long-poll endpoint.

The client-facing packet format is documented in [`protocol.md`](../for-client/bancho/protocol.md). This document describes how Basil dispatches those packets to Application-layer handlers.

The dispatch pipeline is:

```text
HTTP request
    ↓
Bancho protocol reader
    ↓
PacketDispatcher
    ↓
packet handler
    ↓
Application service
    ↓
session / repository / broadcast
```

A packet handler is responsible for interpreting one packet type and delegating application behaviour to the appropriate service.

---

## PacketDispatcher

[`PacketDispatcher`](../../src/Basil.Application/Packets/PacketDispatcher.cs) processes the request body sequentially.

For each packet it:

1. reads the packet identifier;
2. reads its payload;
3. resolves the registered handler;
4. checks whether the handler is allowed for the caller's privilege state;
5. invokes the handler when permitted;
6. otherwise skips the packet.

The dispatcher does not terminate the entire batch because one packet is unknown or unavailable to the current player.

Conceptually:

```text
for each packet:
    handler = registry[packet.Id]

    if handler does not exist:
        skip payload
        continue

    if caller is restricted and handler is not AllowedWhenRestricted:
        skip payload
        continue

    handler.HandleAsync(...)
```

This makes packet processing independent: one unsupported or disallowed packet cannot prevent later packets in the same request from being processed.

---

## Packet handlers

Basil uses one handler class per Bancho packet.

Handlers are grouped by feature under:

```text
Basil.Application/Packets/
```

The main groups are:

```text
Packets/
├── Users/
├── Channels/
├── Spectating/
└── Multiplayer/
```

The handler should contain packet-specific orchestration rather than becoming a second Application service.

A typical flow is:

```text
PacketHandler
    ↓
Application service
    ↓
runtime state / persistence
    ↓
outgoing packet(s)
```

For example:

```text
CreateMatchHandler
    ↓
MatchMembershipService.CreateAsync
    ↓
IMatchRegistry
    ↓
MatchSession
```

When the same operation is needed by another transport, prefer sharing the Application service instead of duplicating the behaviour in the transport handler.

---

## Restricted accounts

Every packet handler declares whether it can run for a restricted account through `AllowedWhenRestricted`.

A restricted account can still authenticate and poll the Bancho endpoint, but only handlers explicitly marked as allowed can execute.

For a restricted caller:

```text
packet
  ↓
handler exists?
  ↓
AllowedWhenRestricted?
  ├── yes → execute
  └── no  → skip payload
```

The dispatcher performs this check before invoking the handler.

See [`privileges.md`](privileges.md) for the meaning of the restricted privilege state.

---

## Outgoing packets

Handlers do not normally construct the HTTP response directly.

Instead, they enqueue outgoing packets onto the relevant session(s).

For example, an operation may produce packets for:

* the requesting player;
* another player;
* every member of a channel;
* every player in a multiplayer match.

Those packets become available to the affected session's next Bancho poll.

Conceptually:

```text
Incoming packet
      ↓
Handler
      ↓
Application state mutation
      ↓
enqueue outgoing packets
      ↓
next poll from recipient
      ↓
HTTP response
```

This is important because Bancho is a long-poll protocol rather than a traditional synchronous request/response API.

A handler should therefore not assume that a packet it queues for another player is delivered during the current HTTP request.

---

## Handler conventions

When adding or modifying a handler:

* keep packet parsing and packet-specific orchestration in the handler;
* delegate reusable business logic to an Application service;
* use Application abstractions for persistence and external capabilities;
* enqueue responses through the session infrastructure;
* respect `AllowedWhenRestricted`;
* do not directly depend on Infrastructure;
* do not assume that outgoing packets are delivered immediately;
* preserve the existing packet ordering requirements.

If the operation changes shared multiplayer state, follow the match locking rules described below.

---

## Multiplayer dispatch

Multiplayer is the largest Bancho packet group and is the most important example of the handler/service/session separation.

A typical match-creation flow is:

```text
CREATE_MATCH packet
        ↓
CreateMatchHandler
        ↓
MatchMembershipService.CreateAsync
        ↓
IMatchRegistry
        ↓
MatchSession
        ↓
broadcast updated match state
```

[`IMatchRegistry`](../../src/Basil.Application/Sessions/Multiplayer/IMatchRegistry.cs) manages the available match slots, while [`MatchSession`](../../src/Basil.Application/Sessions/Multiplayer/MatchSession.cs) contains the runtime state for an individual match.

The handler should not implement the entire match lifecycle itself. Match operations belong in the appropriate Application service.

---

## MatchSession locking

**Any operation that reads and then mutates shared match state must hold `MatchSession.Lock`.**

The lock must cover the complete read-modify-broadcast sequence.

For example:

```text
acquire MatchSession.Lock
        ↓
read current slots
        ↓
validate operation
        ↓
mutate slots
        ↓
build updated state
        ↓
broadcast updated state
        ↓
release MatchSession.Lock
```

Do not release the lock before the broadcast when the broadcast represents the mutation being protected.

This prevents two concurrent packet handlers from observing and modifying the same match state simultaneously.

### Why the lock is required

Basil runs on the ASP.NET Core thread pool. Two packets for the same match can therefore execute concurrently.

Without the per-match lock, operations such as slot changes can race:

```text
Handler A                    Handler B
    |                            |
read slots                      |
    |                         read slots
modify slot A                    |
    |                         modify slot B
write state                      |
    |                         write state
```

One write can overwrite the other.

`MatchSession.Lock` provides serialization at the match level without globally serializing unrelated matches.

The race is covered by:

```text
tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs
```

When adding a new multiplayer mutation, treat this lock as a correctness requirement, not an optional optimization.

---

## Match state and SSE

Match state changes can also feed Basil's HTTP SSE layer.

The important ordering is:

```text
MatchSession.Lock
      ↓
mutate match state
      ↓
publish updated state
      ↓
SSE subscribers receive update
      ↓
release lock
```

The SSE implementation is documented separately in [`sse.md`](sse.md).

The Bancho packet and SSE transports should consume the same underlying match state rather than maintaining independent copies.

---

## Adding a packet

When implementing a new Bancho packet:

1. identify the packet ID and feature;
2. create the handler under the appropriate `Packets/` directory;
3. declare the correct `AllowedWhenRestricted` value;
4. inject the Application services or abstractions required by the operation;
5. register the handler through the Application dependency-injection setup;
6. add handler tests covering the packet's behaviour;
7. add or update protocol tests when the wire contract itself changes.

Do not introduce transport-specific persistence or business logic into the handler.

---

## Failure handling

A packet that cannot be dispatched because it is unknown or disallowed should not prevent the remainder of the request from being processed.

This is especially important because a single HTTP request can contain multiple packets.

The dispatcher therefore follows the principle:

```text
one bad packet ≠ failed batch
```

Known handlers should still report or handle genuine application failures according to the existing Application error-handling conventions. Do not turn the dispatcher into a catch-all that silently hides unexpected programming errors.

---

## Related code

| Component                | Location                                                           |
| ------------------------ | ------------------------------------------------------------------ |
| Packet dispatcher        | [`Basil.Application/Packets/PacketDispatcher.cs`](../../src/Basil.Application/Packets/PacketDispatcher.cs)                    |
| Match creation handler   | [`Basil.Application/Packets/Multiplayer/CreateMatchHandler.cs`](../../src/Basil.Application/Packets/Multiplayer/CreateMatchHandler.cs)      |
| Match membership service | [`Basil.Application/Services/Multiplayer/MatchMembershipService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchMembershipService.cs) |
| Match runtime state      | [`Basil.Application/Sessions/Multiplayer/MatchSession.cs`](../../src/Basil.Application/Sessions/Multiplayer/MatchSession.cs)           |
| Match race tests         | [`tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs`](../../tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs)  |

---

## See also

* [`../for-client/bancho/protocol.md`](../for-client/bancho/protocol.md): Bancho wire contract
* [`../for-client/bancho/authentication.md`](../for-client/bancho/authentication.md): session creation and login
* [`multiplayer.md`](multiplayer.md): multiplayer state and lifecycle
* [`sse.md`](sse.md): HTTP live updates generated from match state
* [`chat.md`](chat.md): shared chat and command handling
* [`privileges.md`](privileges.md): privilege and restriction rules
