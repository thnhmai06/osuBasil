---
name: private-room-unlisted
description: "Private" room/match = unlisted (hidden from lobby and API match lists), never an access control
metadata:
  type: project
---

A private room (`MatchRecord.IsPrivate`, `!mp private`, `!mp makeprivate`) only leaves the room out of the lobby and out of the public match lists (API, for users without `RoomViewPrivate`). Anyone who knows the room id can still find (`Lobby.Find`), join and read it, like an unlisted YouTube video. Invitations grant no access.

**Why:** owner decision 2026-10-22, after a test showed the bot promised "open only to invited players" while the code never enforced it.

**How to apply:** never add invite-only or "private blocks join" logic; listings (MatchQuery.LimitTo, lobby list in the hosts) are the only place `IsPrivate` matters. Documented in docs/for-developers/multiplayer.md. Related: [[week-plan-20261021]].
