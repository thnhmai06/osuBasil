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

## Unit 4 — the remaining Auth contracts and services (done)

`Users` and `Content` had nothing left after Units 1-2: every top-level file in `Features/Users/` is
now a route, an adapter, a DI registration, or the API-facing `UserView`; `Features/Content/`'s
remaining services are all the filesystem-I/O ones Unit 2 already confirmed blocked. Auth still had
four clean candidates: `IPasswordHasher`, `ITokenGenerator` (contracts, zero dependencies — their
implementations `BCryptPasswordHasher`/`GuidTokenGenerator` stay, same pattern as every other
contract-versus-adapter split so far), `LoginForm` (parses the raw login POST body into a
domain-shaped record, only `Basil.Domain.Login` in its dependency list), and `AdminKeyService`
(reads/writes the admin key hash through `ISettingsRepository` and the now-moved `IPasswordHasher`
— no session or filesystem touch). `AuthenticationService` stays: confirmed `GameSession`-typed,
the same category as `ScoreSubmissionService` below.

One new `DomainAdjacency` edge, `Auth -> Content` (`AdminKeyService` through `ISettingsRepository`).
`OsuWebRoutes` dropped out of the `Shared -> Features` pinned list as a second side effect (its
`Features.Auth` references all moved) — second pinned-list movement, row deleted.

**Verification:** build green; `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 197 (+14, two
relocated test files), `Basil.Server.Tests` 979 (−14, matching), `Basil.Protocol.Tests` 158;
`Basil.IntegrationTests` 363, all passed, 7 min 32 s.

Six files moved (4 source, 2 test), one more `Shared -> Features` pinned row deleted (two total
since C1b started).

## Unit 5 — Chat's channel registry and Spectating's player-event contracts (done)

Both slices surveyed with the four-blocker checklist; each had a clean sub-set and a genuinely
blocked remainder.

**Chat:** `ChannelSession` (pure `ConcurrentDictionary<int,int>`-backed membership tracker, keyed
entirely by user id, only a `Basil.Domain.Users.UserPrivileges` dependency), `IChannelRegistry` and
`InMemoryChannelRegistry` (the registry over it, keyed by channel name, no session types anywhere)
all moved to `Basil.Domain.Channels`. `ChannelMembershipService`, `ChatDispatchService`,
`IChannelNotifier`/`ChannelNotifier` and the rest of `Features/Chat/` stay — they are the
`GameSession`/`IrcSession`-branching notification layer, the same shape C1a's chat seam already
carved out.

**Spectating:** `IPlayerInputEvents`/`PlayerInputEvents` and `IPlayerStatusEvents`/
`PlayerStatusEvents` moved together — contract and implementation both, unlike the usual
contract-only split — because the implementations are trivial `event Action<int,byte[]>` dispatchers
with zero framework or session dependency, not adapters wrapping an external system. Both moved to
`Basil.Domain.Spectating`.

Two Spectating files were checked and correctly left behind, for two different reasons:

* `SpectateEvents.cs` (`SpectateEvent`/`SpectateState`/`SpectateStateEvent`) is blocked by **peer
  coupling**: it carries a `UserBrief`, which lives in `Basil.Server.Features.Multiplayer`
  (`MatchLiveSnapshotBuilder.cs`) and has not moved, since Multiplayer is deliberately last. Left
  entirely untouched — a real blocker, not a judgment call.
* `PlayerStatusView.cs` is an **API-view type**, the same category as `ScoreDetailView` and
  `BeatmapViews.cs`: its own doc comment calls it "the wire shape of a userSession's live status,
  published on the `GET /users/{userId}/live` stream's `status` event." Its `Build(GameSession?)`
  factory is `GameSession`-typed regardless, but even split from the factory the record itself is a
  presentation shape for the HTTP host, not a business model — stays in `Basil.Server` by category,
  not merely by the `GameSession` blocker.

No new `DomainAdjacency` edges and no `Shared -> Features` pinned-list movement this unit — both
moved sub-sets were already self-contained.

**Verification:** build green; `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 219 (+22, three
relocated test files — `ChannelSessionTests`, `InMemoryChannelRegistryTests`,
`PlayerInputEventsTests`), `Basil.Server.Tests` 957 (−22, matching), `Basil.Protocol.Tests` 158;
`Basil.IntegrationTests` 363, all passed, 6 min 58 s.

Ten files moved (7 source, 3 test).

## Unit 6 — Bot's reply sink, and the finding that the rest of Bot is not actually next (done)

Surveyed Bot's 6 files before Multiplayer, per the previous "Next exact step." Found something the
checklist alone would not have caught: `architecture-target-20260908.md` §6 ("F4, `Bot`") already
states that `MpCommandService`/`MpReplies` — multiplayer behaviour parked in `Bot` by history — move
to `Basil.Domain/Multiplayer` first, and only *then* does what remains of `Bot` (the dispatcher and
reply sink) become Domain-eligible. Checked directly: `ICommandDispatcher.DispatchAsync` takes
`UserSession sender`, `CommandDispatcher` calls `IMpCommandService` (itself `GameSession`-typed) and
`BotBootstrapService` boots a live `GameSession` — every one of those is blocked the same way
`AuthenticationService`/`ScoreSubmissionService` are, and `BotReplies` reads through
`LocaleCatalog`, a `Basil.Server.Shared` type Domain cannot reference at all. Only
`ICommandReplySink` — a two-method `string`-only interface, zero dependency in any direction — was
actually clean. Moved to `Basil.Domain.Bot`, the namespace's first file.

**Conclusion: Bot is not a slice that can run ahead of Multiplayer.** Five of its six files are
gated on the same `MatchSession`/`MpCommandService` split as Multiplayer itself; there is no
independent "survey Bot" unit left to do. The per-feature table's remaining work collapses to two
items, not three.

**Verification:** build green, no stale `using` lines (the move's own edit already caught them); no
test file to relocate (`ICommandReplySink` has no standalone test, only exercised through
`CommandDispatcherTests`); `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 219 (unchanged),
`Basil.Server.Tests` 957 (unchanged), `Basil.Protocol.Tests` 158; `Basil.IntegrationTests` 363, all
passed, 6 min 51 s.

One file moved.

## Unit 7 — `AuthenticationService`'s password verification, split from session lookup (done)

Measured before designing anything, per the lesson two units taught: check the actual
`GameSession`/`UserSession` *member accesses*, not just the parameter type, before assuming a
service needs a chat-seam-scale design. `AuthenticationService.AuthenticateOnlinePlayerAsync` turned
out to touch exactly one `GameSession` member for its real business decision — `session.Id`, to look
up the stored password hash — plus returning the session itself, which every caller (`OsuWebRoutes`,
`ScoreSubmissionService`) genuinely needs live afterward (`player.Status.Mode = ...`,
`ClientIntegrityService.HandleLastFmFlagsAsync(player, ...)`). That is a 1-2 commit seam, not a
design doc: the verification logic (fetch hash, compare) is already expressed entirely through
`IUserRepository`/`IPasswordHasher`, both Domain contracts.

Extracted `Basil.Domain.Auth.CredentialVerifier` (`VerifyPasswordAsync(int userId, string
passwordMd5, CancellationToken)`), taking exactly those two contracts. `AuthenticationService` stays
in `Basil.Server` — session-registry lookup is inherently session-shaped — now delegates to it
instead of holding `IUserRepository`/`IPasswordHasher` directly. One new `DomainAdjacency` edge,
`("Auth", "Users")` (`CredentialVerifier` through `IUserRepository`). Existing
`AuthenticationServiceTests`/`ScoreSubmissionServiceTests` needed only their `MakeService`/`MakeUseCase`
wiring updated (`new CredentialVerifier(_users, _passwordHasher)` in place of the two direct
dependencies) — same mocks, same assertions, unchanged behavior. A new `CredentialVerifierTests.cs`
in `Basil.Domain.Tests` covers the extracted logic directly.

**Verification:** build green; `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 222 (+3, new
tests), `Basil.Server.Tests` 957 (unchanged, no relocation — both test files stay, they exercise
session-registry orchestration), `Basil.Protocol.Tests` 158; `Basil.IntegrationTests` 363, all
passed, 6 min 32 s.

One new file, no relocations.

## Unit 8 — the `MatchSession` split, phase 1: `MatchRoomState` extracted, zero call-site changes (done)

Before designing anything, checked what the advisor review at this point in the session flagged:
the two route files the target doc's §4 named as gating Multiplayer (`MatchRoutes.cs`,
`MatchSubResourceRoutes.cs`, "1,424" and "627" lines, measured 2026-09-08) are **already
decomposed** — both are now 27- and 31-line registration facades delegating to thirteen per-resource
files under `Features/Multiplayer/Endpoints/` (33-375 lines each). That gate is resolved and was not
a prerequisite here. The real blocker, read directly from `MatchSession.cs` (584 lines) and
`MatchMutationScope.cs`: **the lock and the live-projection publish are one mechanism, not two.**
`MatchMutationScope.CompleteAsync` allocates a state version, releases `MatchSession.Lock`, *then*
hands off to `IMatchMutationPublisher` — under invariant 1 that publisher side can never live in
`Basil.Domain`. A member-by-member inventory of `MatchSession` written before deciding how to handle
that entanglement would have been redone once the decision landed, so the design question came
first: when a Domain-owned match object's locked mutation ends, who allocates the version and
triggers the publish?

**Decision: move the state, not the mechanism — the lock, the mutation scope, and every SSE/packet
primitive stay in `Basil.Server` exactly as they are today, unchanged.** Three shapes were possible
(Domain owns the lock and raises an event a host subscribes to; the host owns the scope over a plain
Domain state bag; the scope stays in `Basil.Server` and only the state moves). The third was chosen:
it is the lowest-risk cut available at C1b's stage, because splitting hosts out of `Basil.Server` is
explicitly Stage D's job, not C1b's — the target doc's own migration order (§8, step 8) warns that
moving business logic into host projects before the logic itself is settled means moving it back out
again later. Extracting only the plain state now, and leaving where the projection lives as an open
question for Stage D, avoids answering that question twice.

**What moved:** a new `Basil.Domain.Multiplayer.MatchRoomState` (named to avoid colliding with the
existing wire type `Basil.Protocol.Multiplayer.MatchState`) holds every plain business field and
method verbatim from `MatchSession` — room settings (`Name`, `Password`, `HostId`, `MapId` and
friends), lifecycle flags (`InProgress`, `IsLocked`, `IsPrivate`, the timer fields), referee/ban/
invite/tourney-client sets, and the pure slot methods (`GetSlot`, `GetFreeSlotId`, `IsReferee`,
`UnreadyPlayers`, etc.). `MatchSlot` moved alongside it — already zero-dependency, same as Unit 1's
repository interfaces. `MatchSession` itself stays in `Basil.Server`, keeps its full public API
surface (every property and method, unchanged signatures), and now holds a `MatchRoomState State`
instance internally, forwarding every business-state member to it (`public string Name { get =>
State.Name; set => State.Name = value; }`) while keeping `Lock`, `PacketBroadcastGate`,
`SseSubscribers`, every `StateStream<T>`, `MutationPublisher`, `BeginMutationAsync` and
`MatchMutationScope` exactly as they were.

**This is why it needed zero changes to any of the other 76 Multiplayer files.** `MatchSession`'s
constructor signature is unchanged (only 1 production call site exists,
`InMemoryMatchRegistry.cs`, plus 13 test files — none needed to change), and every `match.Name`,
`match.GetSlot(...)`, `match.Slots[i]` call site anywhere in the codebase keeps compiling and behaving
identically, because it was already going through `MatchSession`'s public surface, which now just
delegates one level deeper. The lock-discipline sites `chat-seam-decision.md` §5 named as risks for
this kind of extraction (`AbortHandler.cs:58`, `MatchLifecycle.cs:368`/`:377`,
`MatchChangeSettingsHandler.cs:118`) were not touched at all — `Lock`/`BeginMutationAsync`/
`MatchMutationScope` are bit-for-bit the same code as before this unit, so the thin race-test net
`MatchSessionRaceTests.cs` provides (3 tests, slot-join concurrency only) was not stretched past what
it already covered; nothing here changes the answer to "is the lock held correctly," only where the
data it protects physically lives.

**A `DomainAdjacency` gap surfaced, not from the framework/session checklist but from ADR-008's own
documented blind spot.** `MatchRoomState.NoHostId` reads `SystemUserIds.BasilBot`
(`Basil.Domain.Users`), and `Basil.ArchitectureTests` passed with *no* `("Multiplayer", "Users")` edge
declared. `SystemUserIds.BasilBot` is a `const int` — C# inlines it at the call site, so no IL
reference to `SystemUserIds` exists for `DomainBoundaryTests` to see, exactly the "nine live edges
invisible to constant inlining" gap `architecture-target-20260908.md` §5 and ADR-008 already name.
Declared the edge anyway, with a comment stating why the test cannot currently catch its absence —
consistent with the project's own principle that a real edge is declared because it is real, not
because a test demands it.

**Verification:** build green (after one rename — `MatchState` collided with the existing
`Basil.Protocol.Multiplayer.MatchState` wire type, renamed to `MatchRoomState` via Rider's rename
refactoring before the first build); `Basil.ArchitectureTests` 8, `Basil.Domain.Tests` 235 (+13, one
relocated test file — `MatchSessionTests.cs` → `MatchRoomStateTests.cs`, every test unchanged except
constructing `MatchRoomState` directly instead of through `MatchSession`), `Basil.Server.Tests` 944
(−13, matching), `Basil.Protocol.Tests` 158; `Basil.IntegrationTests` 363 (including
`MatchManagementEndpointTests` and `MatchSessionRaceTests`), all passed, 4 min 52 s.

Two files moved (`MatchRoomState` new, `MatchSlot` relocated), one file rewritten
(`MatchSession.cs`, same public surface), one test file relocated, one `DomainAdjacency` edge added.

## A correction, made before any further file moved: `GameSession.Match` retyping has no beneficiary

The "Next exact step" originally written here (still visible in git history at `928dd1ac`) named
scoping `GameSession`'s full split — retyping `Match`, `Spectating`, `ModeStats`, etc. per target doc
§6's table — as C1b's next unit, on the theory that it would unblock `ScoreSubmissionService`. Before
touching any file, checked every call site of `GameSession.Match`/`UserSession.Match` across the
codebase (9 outside `Features/Multiplayer`, ~36 across 24 files inside it) and found the theory
wrong on two counts:

1. **`ScoreSubmissionService` stays in `Basil.Server` regardless of `Match`'s type.** Unit 7 already
   established this — it writes `player.Status.Mods`/`.Mode` and `player.ModeStats` directly, live
   session mutation that has nothing to do with `Match`. Retyping `Match` would not move
   `ScoreSubmissionService` an inch closer to `Basil.Domain`; nothing currently needs it retyped.
2. **The dominant pattern inside Multiplayer needs the full `MatchSession`, not just its state.**
   `MatchChangeSlotHandler.HandleAsync` — representative of the ~24 packet handlers — reads
   `gameSession.Match`, then immediately calls `match.BeginMutationAsync(...)`: it needs the lock and
   the mutation scope, not a read-only state view. Retyping `Match` to `MatchRoomState` would force
   every one of those 24 files to re-resolve the full session through
   `matchRegistry.GetByDbId(...)` (the pattern `ChatDispatchService.ResolveScope` already uses when
   it needs the same thing) — real, spread-out work with no current beneficiary on the other end.

**The 24 packet handlers were never C1b candidates in the first place.** The target doc's own §3.2
places them at `Basil.Hosts.Bancho/Multiplayer/` — Stage D's project, not `Basil.Domain`, regardless
of how `GameSession`/`MatchSession` end up shaped. `GameSession.Match`'s retyping belongs with target
doc §8 step 5 ("Split `GameSession` per §6"), which is Stage D/E work gated on the host projects
existing, not a C1b prerequisite. Recorded here so a successor does not re-derive this from scratch:
**`GameSession`'s split is out of scope for C1b.**

## Next exact step

**What's actually left in C1b's per-feature table, now that the packet-handler layer is correctly
out of scope, is `MpCommandService`/`MpReplies` (target doc §6, "F4, `Bot`") — the same measurement
Unit 7 used for `AuthenticationService`, not yet applied here.** Before sizing a design: grep
`MpCommandService`'s actual `GameSession`/`UserSession` member accesses, not its parameter types. If
they resolve to reads of `.Id`/`.Name` plus replies through `ICommandReplySink` (already in
`Basil.Domain.Bot` since Unit 6), the seam is small, `CredentialVerifier`-sized. If it mutates
session state or calls `BeginMutationAsync` itself — plausible, since `!mp` commands mutate match
state — it stays in `Basil.Server` for the same reason `ScoreSubmissionService` does, and Bot's
remaining 5 files stay with it. Either way, C1b's per-feature table is close to fully resolved once
this one measurement is made — Multiplayer's Domain-eligible surface turned out to be `MatchRoomState`
+ `MatchSlot` (Unit 8) plus whatever this measurement adds, not the full "16 files" the original
table implied, because most of that count was always the packet-handler layer.

**C5's remeasurement, whenever it runs, will not land at the plan's predicted features-only ≈17.**
That prediction assumed C1's original, undivided ~96-file move including the 24 Multiplayer handlers
and the rest of the transport layer. C1b never intended to move those — this section is the record
of why, the same way `architecture-progress.md`'s "C5, run after C1a" section explains the frozen
post-C1a numbers. Not a miss; an expected result of C1 having correctly split into C1a/C1b in the
first place.
