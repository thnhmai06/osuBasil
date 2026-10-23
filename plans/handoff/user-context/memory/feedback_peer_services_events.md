---
name: peer-services-events
description: Peer services never call each other — they emit events and Infrastructure's dispatcher delivers; only parent/child or ownership relations call directly
metadata:
  type: feedback
---

Services that are peers (neither owns the other, neither's rule depends on the other — scores/rooms,
anticheat/rooms) never call each other. The acting service only emits its event; Infrastructure's per-stream
dispatcher delivers it by calling the peer's contract. Direct calls only for a clear parent/child or ownership
relation (service → storage, room → its channel, session → its PM/spectator channel), or a rule that must hold in the
same operation (room → lobby for empty-room closing, auth → session opening, user deletion → closing their connections, a security rule). The dispatcher stays in Infrastructure
(user chose this over an Application-level dispatcher, 2026-10-04).

**Why:** user wants peer services decoupled ("giao tiếp qua hệ thống service"), e.g. the anticheat service only
announces flags and the dispatcher delivers the warning to the room.

**How to apply:** when a service would call another service's contract, ask whether one owns the other or the rule
must hold in the same operation; if not, emit an event and record the Infrastructure handler in the plan instead.
Written into AGENTS.md "Peers talk through events; parents call their children directly". See [[passive-side-owns-interaction]], [[storage-vs-services]].
