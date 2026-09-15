# C1b — moving files into `Basil.Domain`, one verified unit at a time

> Status: **Started 2026-09-15**, after the user chose to run C1b rather than stop at C1a's
> namespace-level separation (see `architecture-progress.md`'s "C5, run after C1a"). This document
> is the working checkpoint for the move itself — what is safe to move, what looks safe but is not,
> and what is done.

## The lesson that shaped how this proceeds

C1's original sizing table (`plans/basil-plan-20260909.md`) classified 143 candidates by checking
five framework packages. C1a already showed that check insufficient once (`Basil.Protocol`, never
checked at all). Re-deriving the candidate list after C1a, against `architecture-target-20260908.md`
§4's per-feature Domain counts — the actual authoritative sizing, not a text classifier — surfaced
two more blind spots a package-level check cannot see:

1. **A type with no import can still be blocked.** A file with zero `using` lines for anything
   forbidden can still take `GameSession`/`UserSession`/`IrcSession` as a parameter, or a type from
   `Basil.Server.Shared.*`, purely through inferred usage. `Basil.Domain` has zero project
   references — it cannot depend on `Basil.Server` at all, the same invariant that blocked C1's
   original services, just via the *shared-state* dependency this time instead of the *protocol*
   one.
2. **Peer coupling inside the "candidate" set matters as much as external imports.** A file can pass
   every check in isolation and still be blocked by a sibling file it composes that isn't itself
   eligible — `DiagnosticOverviewSampler` (no direct blocker) takes `ApplicationSampler`
   (blocked: needs live `GameSession` counts) as a constructor parameter. Moving the first without
   the second breaks the project-reference direction; the classifier that checks one file at a time
   never sees it.
3. **A route file can be invisible to an import-based check.** `MatchRoutes.cs`,
   `MatchSubResourceRoutes.cs` and everything under `Endpoints/` use `RouteGroupBuilder`,
   `MapGet`/`MapPost` and friends through implicit global usings the web SDK adds — no `using
   Microsoft.AspNetCore.*` line exists to catch. These were never Domain candidates; they were
   invisible to the same kind of classifier that already burned this migration three times
   (documented in `HANDOVER.md` §4).

**Diagnostics was reverted, in full, before anything committed.** A first pass moved four of its
samplers (clean of every check above) before checking `architecture-target-20260908.md`'s own
per-feature table — which has no Diagnostics row at all. The Diagnostic API (Stage F) is process
and runtime telemetry, added after the original ten-slice model and never scoped as a Domain
concern by the architecture that document describes. Reverted with `git checkout` before any commit;
nothing was lost, and Diagnostics is **not** a C1b slice.

## What this means for how C1b proceeds

**The target's own per-feature table is the sizing authority, not a re-derived classifier.**
(`architecture-target-20260908.md` §4: Multiplayer 16, Users 9, Beatmaps 15, Content 8, Chat 6,
Auth 10, Scores 10, Spectating 7, Bot 6 files to Domain.) A classifier is still useful to *propose*
candidates inside a slice the table already commits to, but every proposal needs verification
against both blockers above — checked with `mcp__rider__move_type_to_namespace preview: true`,
which reports the true reference graph, not text search — before it is trusted.

**Move in verified units, not whole slices at once.** A unit is a small group of files whose
dependency closure is entirely resolved — every type it needs is either a BCL primitive, already in
`Basil.Domain`, or moving in the same unit. Build and the four fast test projects after every unit;
the full suite (including the ~8-minute integration run) at least once per slice, not necessarily
every unit within it.

## Unit 1 — the repository and store contracts, across six slices (done)

The safest possible category: every repository/store interface in the codebase (`ILoginRepository`,
`IBeatmapRepository`, `IBeatmapsetRepository`, `IChannelRepository`, `IMenuBannerRepository`,
`ISettingsRepository`, `IMatchRepository`, `ILeaderboardStore`, `IScoreRepository`,
`IClientHashRepository`, `IRelationshipRepository`, `IUserLogRepository`, `IUserRepository`,
`IUserStatRepository`) already imported only `Basil.Domain.*` namespaces or nothing at all — these
are exactly the "ports" the business layer should own, sitting in the wrong project by history, not
by design. Their SQL implementations (`Sqlite*Repository`) and DI registrations stay in
`Basil.Server`, now depending on the Domain-owned interface, the same shape every earlier
`IMatchNotifier`/`IChatNotifier`-style contract from C1a already used.

Moving them surfaced their own dependency closures, handled in the same unit:

* `IBeatmapRepository`/`IUserRepository`'s search methods took `BeatmapsetSearchFilters`/
  `UserSearchFilters` — query-filter DTOs with their own parser (`BeatmapsetSearchQueryParser`,
  `UserSearchQueryParser`), themselves clean (regex parsing into a filter record, no session or
  framework dependency). Both pairs moved in the same unit; their query-parser tests moved to
  `Basil.Domain.Tests` (flat file layout, `Basil.Domain.Tests` namespace, matching that project's
  existing convention) since parsing logic is what they actually verify.
* `IScoreRepository`/`IUserStatRepository` returned DTOs (`ScoreOwner`, `ScoreReport`,
  `ScoreInsertRow`, `ScoreRow`, `Stats`) that had been split into a second, still-`Basil.Server`
  namespace block in the same file — one of them carried `// TODO: Đưa Score record lên Domain`
  ("move the Score record up to Domain"), already flagging this as known, pending work. Moved into
  the same Domain namespace as their owning interface.

Three new `DomainAdjacency` edges, each for a reference these moves made visible:
`("Users", "Beatmaps")` (`Stats.Mode`, `IUserStatRepository.IncrementAsync`'s `GameMode` parameter),
`("Scores", "Multiplayer")` (`ScoreReport`/`ScoreInsertRow`/`ScoreRow`'s `MatchTeam?` field), and
`("Auth", "Login")` (`ILoginRepository.CreateAsync` returns a `Login` row). A new `Basil.Domain.Auth`
namespace was created (`ILoginRepository` is the first file in it); every other interface landed in
an existing Domain namespace.

**Verification:** build green; `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 173 (+59, the two
relocated parser test files), `Basil.Protocol.Tests` 158, `Basil.Server.Tests` 1003 (−59, matching);
`Basil.IntegrationTests` 363, all passed, 8 min 43 s. A follow-up namespace-brace style pass (two
files still block-scoped after the move, `IScoreRepository.cs` and `IUserStatRepository.cs`,
converted to file-scoped to match the codebase's convention) re-verified the four fast projects
green; the full integration run was not repeated for that pass, since it changes no behavior — noted
here rather than silently assumed.

18 files moved total: 14 interfaces, 2 filter/parser pairs (4 files), plus 2 test files relocated.

## Next exact step

**Unit 2 — pick one slice from the target table and verify its remaining candidates individually.**
Smallest first is still the safer order: **Content** (8 files target) and **Scores** (10 files
target, several DTOs already moved in Unit 1 — check what is left) are good next candidates. For
each remaining file in a slice: check with `move_type_to_namespace preview: true` first — trust its
`conflicts` field over any text search — and if it reports affected files touching
`Basil.Server.Shared.*` or a `GameSession`/`UserSession`/`IrcSession` parameter type transitively,
stop and record why that file is not yet movable rather than forcing it.

**`Multiplayer` and `MatchSession`'s split are last, not first.** Every Multiplayer handler file
takes `MatchSession` (still entirely `Basil.Server.Features.Multiplayer`) as a parameter, and
`MatchSession` itself needs the split the original C1 task text describes — business state
(slots, host, settings, progress) to Domain, the SSE projection machinery
(`StateStream<T>`, `SseSubscriberRegistry`, `SequenceGate`) staying behind — before any Multiplayer
handler can move. That split is its own unit of work, sized similarly to C1a's chat seam, and should
be scoped and measured on its own before starting, the way `chat-seam-decision.md` was written before
its five commits landed.
