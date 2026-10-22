---
name: feedback-runtime-references
description: "osuBasil runtime models (Room, sessions) derive everything derivable from the domain object they wrap (=> forwarding, setter emits the event), hold references not ids, identify by their basis; events live in Application under a nested category tree used by the dispatcher's <T>."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 1fa71e24-ceb4-4be8-ad3a-d8a67d3c5089
  modified: 2026-09-29T07:42:16.747Z
---

Rule: **"Cái gì suy biến được thì thực hiện suy biến"** — anything derivable is derived, not stored.

- A property co-owned by a runtime object and its domain object (source of truth = domain) is a forwarding property on the runtime object: `Room.Name { get => Match.Name; set { Match.Name = value; <emit RoomNameChanged> } }`. Same for `IsVisible`, `Creator`, the `MatchSettings` fields, `ChannelSession.Name => Channel.Name`. The setter is where the event is emitted — no exposed event writer, no caller that can forget to emit. Application manages interaction around the model (runtime, events); Domain manages the model and its own validity.
- Basis = what the object represents. `GameSession` may store the `Login` record (supporting info) but derives `User => Login.User`, and — SUPERSEDED 2026-09-30 — its identity is now the `Login` (one session = one login); duplicate-user checks compare `Login.User`. See [[application-redesign-decisions]]. Same idea for `Room` (basis `Match`), `ChannelSession` (basis `IChannel`). Choose object vs identifier deliberately (`Room.Beatmap` object; `PlayerStatus` beatmap = `Md5?` since the server may lack the map). Never `string` where `Md5` exists.
- Forwarding + hiding the inner object applies only to co-owned properties (e.g. `Match` is the persisted snapshot of `Room`, so `Match` is truth and `Room` co-owns `Name`); members only the inner object owns (`Match.Id`, `CreatedAt`) are read via `room.Match` directly.
- Identify by the basis object (`User`), not its id — ids are only for repository lookups (`UserId` → repository → `User` → registry). Registries of coexisting session kinds are separate (one for `GameSession`, one for `IrcSession`), keyed by `User`. Reference-carrying names drop the type suffix: `From`, not `FromUser`/`FromUserId`.
- Relations are references in `ConcurrentSet<T>` (Basil.Domain.Utilities); any `ConcurrentDictionary<T, byte>` must become `ConcurrentSet<T>`.
- `User` vs `GameSession` in a `Room`: "user goes offline and comes back — is the info gone?" Gone → `GameSession` (slot occupant, host). Survives → `User` (creator, referees, bans, invites).
- SUPERSEDED 2026-09-30 by [[passive-side-owns-interaction]]: the acted-on object stores the relation and emits the event; the session only calls.
- Instance identity: `IEquatable` handles equality; the cache keeps one live instance, released only after 5 min idle AND unreferenced; DB updates are set directly onto that live instance.
- Events are runtime rules, not business rules: `Event`/`IEventSource` belong in Application, not Domain. One operation = one event carrying its whole consequence. Events are grouped in a nested category tree (e.g. `Event` ← `RoomEvent` ← `RoomSettingsEvent` ← `RoomNameChanged`) — for management/knowing what an event belongs to, with the OOP payoff at the dispatcher (`IEventHandler<TCategory>` receives every sub-event).

**Why:** User decisions across 2026-09-29 reviews of the Domain/Application refactor (rejected copied ids/fields, generic `SettingsChanged`, string MD5s, `Login` as basis, and an exposed event writer; chose derived forwarding setters that emit).

**How to apply:** Before adding a member to a runtime model: can it be derived from the basis/domain object (then `=>` and emit in its setter)? Is it a relation (reference + ConcurrentSet)? Is it the represented actor's action (session method) or done to it (passive)? See [[feedback-room-api-shape]] and [[feedback-eventlog-redundant]].
