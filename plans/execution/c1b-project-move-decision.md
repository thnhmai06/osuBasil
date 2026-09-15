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

## Unit 2 — the Content and Scores business logic that survived the filesystem check (done)

A third blocker surfaced here, not caught by anything in Unit 1: **direct filesystem I/O.**
`FaqService`, `MenuBannerService`, `MenuIconService` and `MenuSeasonalService` all call
`File.Create`/`File.Delete`/`Directory.CreateDirectory` etc. directly — none of it shows up as a
`using` for a forbidden namespace (`System.IO` is never forbidden), but it is exactly the kind of
"external service implementation" CLAUDE.md's business-layer rule already excludes, the same
category the original C1 table already put `IMemoryCache`/`HttpClient` users in. All four stay in
`Basil.Server`. Only `MotdService` — reads and writes one setting through `ISettingsRepository`,
already Domain-owned, no file access — moved.

**Two more response-encoder types were found and correctly left behind.**
`ScoreSubmissionChartsFormatter` and `ScoreSubmissionResponseBuilder` build the plain-text body the
osu! client receives after a score submission — the charts formatter's own remarks say it keeps
"the protocol's fixed key/value shape intact." Same category as `LoginResponseEncoder` (step 4 of
C1a): the response body *is* the wire payload, not a notification, so encoding it is an adapter's
job even though neither file imports anything currently forbidden. Left in `Basil.Server`.

From Scores, four files did qualify and moved: `IReplayStorage`, `IScoreDecryptor` (the two
contracts Unit 1 should have caught but missed — `RijndaelScoreDecryptor`, the BouncyCastle
implementation, correctly stays), and `ReplayService` with its `ReplayFetchResult`/
`ReplayFetchResultCode` types (fetches a stored replay through two already-Domain contracts, no
file access itself — the storage adapter, `FileSystemReplayStorage`, does that and stays behind).
`Basil.Domain.csproj` gained its first package reference, `Microsoft.Extensions.Logging.Abstractions`
(already the allowed abstraction package from the original C1 table; `ReplayService` takes
`ILogger<ReplayService>`).

`FileSystemReplayStorage` (in `Shared/Storage/`, not a C1b candidate itself) referenced
`Features.Scores.IReplayStorage` and dropped out of the `Shared_Should_Not_Reference_Features`
pinned list as a side effect — the first pinned-list movement since C3. Row deleted.

**Verification:** build green; `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 176 (+3, one
relocated test file, `NSubstitute` added to that project's references), `Basil.Server.Tests` 1000
(−3, matching), `Basil.Protocol.Tests` 158; `Basil.IntegrationTests` 363, all passed, 6 min 9 s.

Five files moved (`MotdService`, `IReplayStorage`, `IScoreDecryptor`, `ReplayService`, its test
file), one `Shared -> Features` pinned row deleted.

## Unit 3 — Beatmaps' mirror contracts and service (done)

**A fourth blocker surfaced: an `IOptions<T>` type argument that is itself a Server type.**
`Microsoft.Extensions.Options`/`ILogger<T>` are allowed abstraction packages, but the config POCO
they wrap around is not automatically one — `MirrorOptions` lived in
`Basil.Server.Shared.Configuration`, and `MirrorService` took `IOptions<MirrorOptions>`. The
namespace-only move compiled fine (the file was still physically in `Basil.Server`, so the type
reference resolved within the same assembly); only the physical `git mv` — which actually changes
which project compiles the file — surfaced the real `CS0246`. **The namespace edit alone never
proves a move is safe; only the physical relocation plus a rebuild does.** `MirrorOptions` is a
plain POCO with no framework attributes beyond what `Options` binds by reflection, so it moved to
`Basil.Domain.Beatmaps` alongside `MirrorService`; `Basil.Domain.csproj` gained
`Microsoft.Extensions.Options` as its second package reference.

`DirectSearchService` and `BeatmapViews.cs` were checked and correctly left behind: the former
builds the pipe-delimited osu!direct wire response (`Format`/`FormatMirror`, the same
response-encoder category as `ScoreSubmissionChartsFormatter` and `LoginResponseEncoder`); the
latter is explicitly, by its own doc comments, the "API-facing view types" mapped from the Domain
model for the beatmap/beatmapset HTTP endpoints — the same category as `ScoreDetailView`.
`PpyOsuCalculator` (osu!-framework-backed `IOsuCalculator` implementation, with direct
`File.OpenRead`) and `BeatmapsetAssetCache` (filesystem/zip extraction) stayed for reasons already
established.

Four files moved: `IMirrorSearchClient` (+ its `MirrorSearchSet`/`MirrorSearchBeatmap` result DTOs,
which had to move with it — the interface's own return type made them a hard requirement, not a
judgment call), `IOsuCalculator` (+ its `BeatmapAnalysis` result type), `MirrorService` (+
`MirrorEndpoints`), `MirrorOptions`. Two new `DomainAdjacency` edges: `Beatmaps -> Scores`
(`IOsuCalculator.Analyze` takes `Mods`), `Beatmaps -> Content` (`MirrorService` reads/writes through
`ISettingsRepository`). `FakeOsuCalculator` (a test double implementing `IOsuCalculator`) stayed in
`Basil.Server.Tests` — it is consumed only by tests of services that themselves stay in
`Basil.Server` (`BeatmapIngestionServiceTests` and its two siblings), so moving the interface it
implements did not require moving it.

**Verification:** build green; `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 183 (+7, one
relocated test file), `Basil.Server.Tests` 993 (−7, matching), `Basil.Protocol.Tests` 158;
`Basil.IntegrationTests` 363, all passed, 8 min 34 s.

## Next exact step

**`ScoreSubmissionService` is the last Scores candidate, and it needs a design pass, not a quick
move.** It takes `Basil.Server.Shared.Sessions` types directly (resolving the submitting player by
name through the live session registries) alongside its real business decisions (duplicate check,
grade computation, hardware-ban-adjacent validation). This is the same shape as C1a's match and
chat seams: the service decides and, in the same method, reaches for session-held state a plain id
or a small Domain-shaped lookup result could carry instead. Size it the way
`chat-seam-decision.md` sized the chat seam before touching any file, rather than attempting it as
one more unit like the contract moves above.

**Otherwise, Users and Content's remaining service-shaped files are the next safe territory** —
apply the four-blocker checklist (framework imports; `GameSession`/`UserSession`/`IrcSession`
coupling; direct filesystem I/O; an `IOptions<T>`/other wrapped type argument that is itself a
Server type) to each remaining candidate with `move_type_to_namespace preview: true`, and confirm
every move with a physical relocation and rebuild before trusting a clean preview — Unit 3's
`MirrorOptions` finding shows preview and the namespace-only edit are not enough on their own.

**`Multiplayer` and `MatchSession`'s split are last, not first.** Every Multiplayer handler file
takes `MatchSession` (still entirely `Basil.Server.Features.Multiplayer`) as a parameter, and
`MatchSession` itself needs the split the original C1 task text describes — business state
(slots, host, settings, progress) to Domain, the SSE projection machinery
(`StateStream<T>`, `SseSubscriberRegistry`, `SequenceGate`) staying behind — before any Multiplayer
handler can move. That split is its own unit of work, sized similarly to C1a's chat seam, and should
be scoped and measured on its own before starting, the way `chat-seam-decision.md` was written before
its five commits landed.
