---
name: passive-side-owns-interaction
description: "osuBasil Application: the object being acted on stores the relation and emits the event; the actor (user/session) only calls. Application only emits events — handlers, dispatchers, notification routing belong to Infrastructure."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 9424f3a0-187c-4744-9c0c-1e47cd3acb93
  modified: 2026-09-30T09:03:12.503Z
---

Rule that runs through all of Basil.Application: **an active object (user, session, any actor) only
invokes an action; the object that is acted on stores the resulting relation and emits the event.**

- Joining a channel: the channel manages its members. The session must NOT hold a `Channels` set,
  and must NOT have `Join(ChannelSession)` — otherwise every new joinable kind (channel, room,
  spectator stream, …) forces a new `Join*` method on the user, which breaks OOP. The call is
  `channel.Join(session)`; "which channels is X in" is a query over channels.
- A user/session does not emit events by itself. The event comes from the object interacted with
  (channel, room, slot, spectated stream, presence board, …).
- Application's job is to model the environment and emit events so other layers can hook in. It
  must not implement event handlers, dispatchers, or event → notification routing; those belong to
  Infrastructure. Application should read as a simulation of the real environment, not a linear
  "do this then that" pipeline.
- `ICreatable<TData,TValue>` is redundant with `IRepository`. Safe-name normalization
  (`UserSafeName`) is the repository's own lookup concern, not Application's.

**Why:** 2026-09-30 the user reviewed my explanation of Application and called `UserSession.Channels`
+ `UserSession.Join/Part` + session-emitted events + Application-side `*EventHandlers` a basic
principle violation that I, as orchestrator, let through. This supersedes the older line in
[[feedback-runtime-references]] that "what the user can do = methods on the session".

**How to apply:** For every relation, ask "who is acted on?" and put the set + the event there.
Before accepting delegated code, check each new member against this rule. Keep Application free of
handler/dispatcher implementations.
