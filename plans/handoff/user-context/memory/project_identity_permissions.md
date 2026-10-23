---
name: identity-permissions
description: "Basil is now an osu!(stable) multiplayer backend platform: no bot in the server, users act by identity + fine-grained Permissions (ClientPrivileges derived), restrictions are timed records; plan plans/identity-permissions-plan-20261005.md"
metadata:
  node_type: memory
  type: project
  originSessionId: fdae2673-0224-44ed-93fc-5a0d73896330
  modified: 2026-10-05T01:44:16.835Z
---

Decided with the user on 2026-10-05; the authoritative plan is
`plans/identity-permissions-plan-20261005.md`. Positioning changed to "A lightweight,
high-performance osu!(stable) backend platform specialized for multiplayer."

* The server has no bot concept (no bot user id 0, no Bot connection kind, no virtual in-game user).
  BasilBot is an ordinary account using the HTTP API with granted permissions; `!mp` and every chat
  command are the bot's job; the server only emits events (no server-side chat announcements).
* Permissions are the source of truth: one `[Flags] enum Permissions : ulong` grouped by
  `ClientPrivileges` category (`PlayerCreateRoom`, `ModeratorSilence`, … with category masks), Discord-
  style fine-grained; only high-level management permissions may be broad. A `ClientPrivileges` bit is
  on when any effective permission of its category is on.
* Restrictions (silence included) are timed records in their own repository, each a mask of
  suspended permissions; effective = granted minus active masks. Staff actions follow a Discord-like
  hierarchy (actor must hold a strict superset of the target's granted permissions).
* Delegation: `TournamentActForUsers` lets an account act for an online user, room/lobby operations
  only, through a `DelegatedConnection` whose scope Application enforces.
* Work order: Domain/Protocol/Application first; other layers get notes only (plan section 3).

**Status (2026-10-05):** Domain/Protocol/Application done and pushed up to `91c37ffa`; baseline ALL PASS. Added after review (plan section 7): no self-restriction (approved), restrictions never touch `Permissions.Management`, scope-based delegation (`DelegatedConnection`, scope `RoomDelegation`, enforced in Application), `ServerSettings.LockedCreation` (accounts/rooms/beatmapsets; uploads locked by default; `PlayerUploadBeatmapsets`), no admin key, no implicit first-user permissions. Security fixes `2fad042e`. Handoff for the next session is plan section 8 (`1027cd10`): status, verification, ordered next steps, open questions.

**Why:** user's redesign brief and answers in the 2026-10-05 session.

**How to apply:** read the plan before touching users, permissions, sessions, rooms or chat; do not
reintroduce bot special cases or authority by connection kind. Supersedes the BasilBot parts of
[[storage-services-split]].
