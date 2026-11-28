# W6: core rules (roadmap C1 and C2)

Source of truth: `plans/review/release-1.0.md`, roadmap groups **C1** and **C2**, and the finding rows they name:
F-006, F-099, F-026, F-011, F-057, F-060, F-019 (D17), F-041, F-027, F-032, F-030, F-017 (D21), F-108 (core half).
Read every one of those rows and their decisions before writing code. Read `AGENTS.md` in full and the files it says to
read first: `docs/for-agents/application-design.md`, `docs/for-agents/identity-privileges.md`,
`docs/for-developers/multiplayer.md`. bancho.py (osuAkatsuki/bancho.py, `app/objects/match.py`,
`app/api/domains/cho.py`) is the reference for osu! behaviour, under Basil's rules.

## Order and what each item must end as

**C1, security first**

1. F-006 with F-099: one rule for "may this actor view this room", owned by the room (Domain rule, exposed through the
   Application `Room`/`ScopedRoom`), used by `!mp in`, `!mp settings`, IRC room visibility, the API and `GET /v1/matches`
   (F-030 uses the same rule). osu!direct's `Supporter` rule leaves `WebAuthentication` (a host) for the object that
   owns it. No host keeps a privilege rule.
2. F-026: every operation authorizes before it looks up its target or the target's state, so a caller without the
   privilege learns nothing (`Denied` before `NotFound`/state refusals). The HTTP body half (422 before 403) is W9.
3. F-011: an API session is not a chat presence (it never appears in `NAMES`, channel member lists or presence);
   the reason recorded when a session leaves is the true one; `!mp host` agrees with `NAMES`.

**C2, multiplayer and session rules**

4. F-057: a room never stays without a host while it has a player (every path that removes the host hands it on,
   offline users included).
5. F-060: a round whose players cannot load or have all left ends by itself (Domain transition, reported in the
   operation's outcome; no timer in Domain; any deadline is the lazy rule of the lock-holding Application object).
6. F-019 (D17): the match id is the one number of a room on every surface (BasilBot, IRC `#mp_<n>`, API, console);
   the runtime room id stays only for the osu! wire, mapped at the Stable host.
7. F-041: spectating yourself is refused; spectating a player who is not playing is allowed; a spectate ends only when
   the player goes offline.
8. F-027 with F-032 (core half): refuse what cannot happen at the moment it is asked (a countdown with no beatmap, an
   empty update); a room command answers the room as it now is; a room rule is never reported as a missing privilege
   (kicking a referee is a room-rule refusal, not `Denied`).
9. F-030: `GET /v1/matches` lists only matches the caller may view (the rule from item 1; the route stays in Host.Rest).
10. F-017 (D21): idle limits per session kind in `SessionRules` (osu! 60 s, IRC 300 s, API 8 h); expiry stays lazy (no
    timer decides it). Last-seen flushing is C5.6, not here.
11. F-108 (core half): the osu! session stores its presence filter (none, all, friends) from packet 79
    (`ReceiveUpdates`), as bancho.py does; the Stable host sends the message; delivery filtering is W7.

## Rules that bind every item

- Every business rule lives in Domain (state, invariant, transition and its consequences inside the same object);
  Application holds the lock, the map to live sessions, publishing. Read-then-mutate is one synchronous method under
  the room's `Lock`; never await under it; publish inside it.
- Refusals are outcome enums (`Denied = 0`, `Ok = 1`, others from 2); a reason that needs another object goes in the
  Application enum of that category, never a flattened catch-all.
- Hosts translate only: no privilege, permission or state rule in Stable, IRC, REST or Cli. Lookups are allowed;
  changes go through Mediator (F-100 is W8, but do not add new direct changes).
- BasilBot commands run with the typist's privileges; a bot message goes through no privilege check.
- Reply texts: only where an item changes what a user is told; put new texts in the surface's reply class, short and
  plain (the reviewer runs them through the humanizer). Do not reword other replies.
- Check the "Final verification" items 7 and 8 of AGENTS.md against your own diff before you report.

## Rules for you

Rider MCP (`search_symbol`, `find_usages`, `rename_refactoring`, `change_api_signature`, `safe_delete`,
`get_file_problems`, `build_solution_*`), OpenCode Go at `http://localhost:4096` with cheap flash models for mechanical
edits only. Build only (`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no
commit, no `git add`, do not touch `Basil/Data/appsettings.json`. An item needing an unwritten decision: skip it,
finish the rest, report.

## Report

Per item: what the rule now is, where it lives (type and file), the files changed; skipped items and why; decisions you
took; build result.
