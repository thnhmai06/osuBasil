# W9b: the relationship registry and the presence filter (D45)

Source of truth: decision **D45** in `plans/review/release-1.0.md` (read it word for word), F-108, and `AGENTS.md`
("Application feature folders": `Registry/` holds what keeps live data in RAM and owns the operations on it;
"Domain and Application"; "Avoid shared mutable state before you lock it"). W7 added `Basil.Host.Stable/Support/PresenceAudience.cs`,
which today sends one `GetFriends` query per friends-filtered client on every broadcast: that is what this wave removes.

## Steps

1. **`RelationshipRegistry`** in `Basil.Application.Users/Registry/`, a singleton built in `AddUsersApplication()`:
   per user id a `FrozenDictionary<RelationshipType, ConcurrentSet<int>>` (`Basil.Shared` already has `ConcurrentSet`),
   the ids that user (the actor) has each kind of relationship with. Loaded once per user on first need through
   `IRelationshipRepository.ListAsync` (cache-aside; two first readers of the same user must not load twice into
   different sets: use the pattern `CacheAside` or `ConcurrentDictionary.GetOrAdd` with a lazy load). Operations:
   add/set, remove, and synchronous `Has(actorId, type, targetId)` and `Of(actorId, type)` (the latter may need the
   user loaded: give an async ensure-loaded entry for callers that may hit an unloaded user, and keep the sync ones for
   callers that know the user is online and loaded). Writes go to the repository and the set (write-through, set changed
   only after the write succeeds).
2. **`UserRelationships` / `ScopedUserRelationships`:** the rules (self, deleted target, BasilBot) stay where they are
   or move to Domain if they are pure; every read and write of relationships goes through the registry; no list is
   read from the database to answer a question.
3. **Loading on sign-in:** when a user's first session opens, the registry loads that user (the osu! client asks for its
   friends at login anyway; make that answer come from the registry).
4. **Presence filter:**
   - `PresenceFilter` default is `All` everywhere a session starts (Domain default value or the session's initial
     value; keep the wire numbering).
   - Domain: the meaning of a filter value, e.g. `PresenceFilter.Includes(bool isSelf, bool isFriend)`.
   - The osu! session answers whether it wants a user's presence (it asks the registry for friendship, synchronously).
   - `PresenceAudience` only walks the open clients and asks each session; no Mediator call, no database.
5. **Blocks:** message delivery and anything else that calls `IRelationshipRepository.ListBlockerIdsAsync` asks the
   registry instead (for each recipient: `Has(recipient, Block, sender)`, loading an unloaded recipient once). Remove
   `ListBlockerIdsAsync` from the port and its EF implementation.
6. **Docs:** the line about relationships in `docs/for-agents/persistence.md` (Registry / Repository model) and
   `docs/for-developers/osu-stable.md` (packet 79: filter applied, default All).

## Rules for you

Rider MCP (`search_symbol`, `find_usages`, `safe_delete`, `get_file_problems`, `build_solution_*`), OpenCode Go at
`http://localhost:4096` with cheap flash models for mechanical edits only. Build only
(`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no commit, no `git add`, do
not touch `Basil/Data/appsettings.json`. AGENTS.md "Final verification" 7 and 8 apply: no `.ConfigureAwait(false)`, no
wait between a check and the change it guards, no Infrastructure reference from a host. A step needing an unwritten
decision: skip it, finish the rest, report.

## Report

Per step the files changed and the design chosen (how double loading is prevented, when a user is unloaded, if ever);
skipped steps and why; build result.
