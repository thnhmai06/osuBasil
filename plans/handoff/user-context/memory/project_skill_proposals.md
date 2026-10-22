---
name: project-skill-proposals
description: Principles stated during osuBasil work that should later be merged into the code-principles skill (user consolidates skills later)
metadata:
  node_type: memory
  type: project
  originSessionId: d775f7bb-415e-490a-ad9e-bd79b6c6f436
  modified: 2026-10-13T12:35:03.042Z
---

Pending additions to `~/.claude/skills/code-principles` (user will consolidate later; do not edit the skill silently):

- 2026-10-13 — `references/design.md`, section "Contracts, interfaces and abstractions" or a new "Child services":
  a service whose object has parts gets one child service per part, exposed as a property named after the part
  (`rooms.Slots`); the object stays a parameter (`rooms.Slots.MoveAsync(room, by, …)`), not an object-bound handle;
  children `I{Parent}{Part}Service` share the parent's event stream; no parts ⇒ flat; size alone is no reason.
  Already in osuBasil AGENTS.md. See [[feedback-service-children-by-parts]].
- 2026-10-13 — `references/design.md` (permissions): a larger permission may contain a smaller one of another group
  (`TournamentObserveRooms` contains `PlayerSpectate`); group masks then take only their own group's bits.

**How to apply:** append new cross-project principles here when the user states them; offer the list when the user
asks to update skills.

- 2026-10-21 - superseded: the 2026-10-13 "child services by parts" item no longer applies; services are now object-free and coordination is a method of the acted-on object. The consolidated, current list of proposals is the section "Notes for the code-principles skill" in osuBasil AGENTS.md (layering of services, plain data in the lower layer, staged reference flip with a bridge interface, deferred port against construction cycles, content-based file detection, architecture-test positive control, agent working rules, read the IDE).
