---
name: feedback-service-children-by-parts
description: "Services whose object has parts get one child service per part (rooms.Slots…), object stays a parameter; flat otherwise"
metadata:
  node_type: memory
  type: feedback
  originSessionId: d775f7bb-415e-490a-ad9e-bd79b6c6f436
  modified: 2026-10-13T12:22:42.862Z
---

"Cái gì có dạng có con bên trong, thì chia." A service whose object has parts gets one child service per part,
exposed as a property on the parent contract (`rooms.Slots`, `channels.Members`, `sessions.Spectators`,
`users.Restrictions`). The object stays a parameter (`rooms.Slots.MoveAsync(room, by, …)`), not an object-bound handle
(`rooms.For(room)…` was offered and declined). Services with no parts stay flat (auth, beatmapset, score, anticheat, match).

**Why:** user wants an object-oriented facade that groups the API by the parts of the object, following `IRoomService`.

**How to apply:** when a service grows operations on distinct parts of its object, split it into
`I{Parent}{Part}Service` children sharing the parent's event stream; don't split a service just for size.
Plan: plans/services-facade-beatmapset-plan-20261013.md (2026-10-13). Related: [[feedback-passive-side-owns-interaction]].
