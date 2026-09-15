# Architecture v3 migration plan — `Basil.Domain` / `Basil.Application` / `Basil.Infrastructure`

**Status: PLAN ONLY. Nothing below has been executed.** Written 2026-09-16 against commit
`52e29999` on `feat/vsa-migration`, from the user's `osuBasil-architecture-v3.md` (the
specification), the current tree, and the migration record in `plans/execution/`. The user
reviews this before any batch runs.

The specification's one-line summary, which every decision here is checked against:

> Project boundary changes, feature boundary and feature vocabulary do not.

## 0. Why this plan is mostly mechanical

Stages A–C of the previous plan did the hard part already, and it carries over unchanged:

* Business types are in `Basil.Domain` (71 files) with its own adjacency instrument.
* Every place a business service used to encode a bancho packet or IRC line inline now goes
  through a contract: `IChatNotifier`, `IChannelNotifier`, `IMatchNotifier`,
  `ISpectatorNotifier`, `LoginResponseEncoder`. `TransportSeamTests` pins the three remaining
  exceptions by name.
* `MatchSession` is split: plain state in `Basil.Domain.Multiplayer.MatchRoomState`, the lock and
  stream machinery in `Basil.Server`.
* Logout is an ordered `IPlayerLogoutHandler` list, not five slice imports.
* `Basil.Protocol` is already two projects (`.Bancho`, `.Irc`), each a pure wire-format library
  with no framework reference.

Measured on the current tree with a first-cut classifier (path + technology grep, then every
candidate checked for references into technology-bound types): of the 315 `.cs` files in
`src/Basil.Server`, **205 are Infrastructure by technology** (ASP.NET, Dapper/SQLite, filesystem,
ImageSharp/FFmpeg/osu!, `BackgroundService`, packet handlers, routes, DI registration, the host) and
**110 are Application candidates**. Of those 110, **only 11 types block a move** by referencing
something that must stay in Infrastructure, and 9 of the 11 are resolved by splitting a file or
moving a small pure helper along. That is why the batches below are "move, fix namespaces, build,
test" rather than "redesign".

The classifier is a technology grep, not an ownership judgement — §3 corrects it where the two
disagree (e.g. `BCryptPasswordHasher` has no framework `using` but is a concrete provider, so it
is Infrastructure).

## 1. Current state → target state

### 1.1 Projects

| Now | Role now | Target | Role in v3 |
|---|---|---|---|
| `Basil.Domain` | domain model, ports, a few services | `Basil.Domain` | unchanged role; five application-shaped services move *out* (§3.1) |
| — | — | **`Basil.Application`** (new, class library) | use cases, application services, contracts the use cases need, in-process session/eventing state |
| `Basil.Server` (`Sdk.Web`, the executable) | everything else: routes, packet handlers, IRC TCP, persistence, storage, media, background services, host | **`Basil.Infrastructure`** (`Sdk.Web`, **still the executable**) | technical implementations and integrations, including hosting — v3 §Infrastructure lists "Hosting/runtime integration" explicitly |
| `Basil.Protocol.Bancho`, `Basil.Protocol.Irc` | wire formats | unchanged | out of scope; referenced as-is |
| test projects, `Basil.LoadTests` | — | unchanged names | only `ProjectReference` / `InternalsVisibleTo` / executable-name edits (§7) |

**Decision D1 — `Basil.Infrastructure` is the executable.** v3 reorganises exactly two projects
into three and names no host project. Keeping `Basil.Server.csproj`'s shell (Web SDK, icon,
manifest, publish-cleanup target, `Dockerfile`/`release.yml` publish path) and renaming it is the
smallest change that satisfies the spec. The alternative — a fourth slim host project — is the
obsolete Stage D design (§9) and is *not* proposed. Cost of D1: `Dockerfile`, `release.yml`,
`tests/Basil.LoadTests/Hosting/DotnetServerHost.cs` and `CategoryEnricher`'s rule strings change
the executable/namespace name once.

### 1.2 Project references — target

```text
Basil.Domain           -> (none)                                  packages: Logging.Abstractions, Options
Basil.Application      -> Basil.Domain, Basil.Protocol.Irc, Basil.Protocol.Bancho
Basil.Infrastructure   -> Basil.Application (Domain + Protocol transitively)

Basil.Domain.Tests     -> Basil.Domain                                          (unchanged)
Basil.Server.Tests     -> Basil.Infrastructure, Basil.Protocol.Bancho, Basil.Protocol.Irc
Basil.IntegrationTests -> Basil.Infrastructure
Basil.ArchitectureTests-> Basil.Domain, Basil.Application, Basil.Infrastructure, Basil.Protocol.*
Basil.LoadTests        -> Basil.Protocol.Bancho                                (unchanged)
```

`Basil.Application` packages: `Microsoft.Extensions.Logging.Abstractions`,
`Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions` (for
`IServiceScopeFactory` where a service already takes it), `System.Diagnostics.Metrics` is BCL.
Nothing else. Verify by reading the csproj after Batch 1, as C1 did for Domain.

**Decision D2 — `Basil.Application` references both `Basil.Protocol.*` projects.** v3 forbids
*Domain* from bancho/IRC; it says nothing against Application knowing the wire format, and the
two Protocol projects are framework-free value/encoder libraries. Two things force it:

* The IRC feature's application layer *is* message building: `IrcAuthenticationService`,
  `IrcQueryService`, `IrcNamesReply`, `IrcLoginOutcome` return `IrcMessage` values, and
  `IIrcConnection.Send(IrcMessage)` is the contract `UserSession` holds. Redesigning that to a
  string/byte seam is exactly the "move → redesign → new abstraction" loop v3 tells us not to
  repeat.
* `LoginService` (413 lines, Auth) builds its bancho login response through
  `LoginResponseEncoder` + `PacketBuilders`. Same reasoning.

The existing `TransportSeamTests` pinned list becomes the instrument for this: it is re-scoped to
`Basil.Application.*` types and lists, by name, every Application type that references
`Basil.Protocol.*`. Adding a name is a deliberate act. Expected initial list: the three today
(`AnnounceRoutes` drops out — it stays Infrastructure; `MatchPacketDataMapper` and
`SpectateFramesEvent` are re-homed, see §3) plus `LoginService`, `LoginResponseEncoder`,
`PacketBuilders`, and the IRC application types.

### 1.3 Folder and namespace shape — target

Feature is the folder axis in all three projects, same names as today's slices. `Shared/` survives
in Application and Infrastructure **only** for code that genuinely belongs to no feature (sessions,
eventing, configuration, localization, JSON options, persistence plumbing, HTTP plumbing) — it
already exists under that name and moving it into a feature would be a rename for its own sake.
No `Services/`, `Repositories/`, `Handlers/`, `Managers/` folders anywhere.

```text
Basil.Domain/<Feature>/                 as today; loses five services (§3.1)
Basil.Application/<Feature>/            services, contracts, in-memory registries, replies
Basil.Application/Shared/Sessions       (see open question Q1)
Basil.Application/Shared/Eventing
Basil.Application/Shared/Configuration
Basil.Application/Shared/Localization
Basil.Application/Shared/Json           BasilJsonOptions, JsonMergePatch (moved out of Shared/Http)
Basil.Infrastructure/<Feature>/         routes, endpoints, views, packet handlers, Sqlite*, Caching*, notifier impls, DI extensions, background services
Basil.Infrastructure/<Feature>/Packets  keeps today's sub-folder
Basil.Infrastructure/Shared/{Http,Persistence,Storage,Media,Logging}
Basil.Infrastructure/Host/              Bootstrap and the *Setup files, unchanged
```

Namespaces follow the folder: `Basil.Application.Multiplayer`, `Basil.Infrastructure.Multiplayer`,
`Basil.Infrastructure.Multiplayer.Packets`, `Basil.Application.Shared.Sessions`, and so on. The
`Features.` segment disappears — v3 wants `Basil.Application.Beatmaps`, not
`Basil.Application.Features.Beatmaps`. Rider's *Adjust Namespaces* does this per folder once the
files are in place.

Feature names stay exactly as today: `Auth, Beatmaps, Bot, Chat, Content, Diagnostics, Irc,
Multiplayer, Scores, Spectating, Users` in Application/Infrastructure, and Domain keeps its own set
(`Auth, Beatmaps, Bot, Channels, Content, Login, Multiplayer, Scores, Social, Spectating, Users`).
The pre-existing `Channels`/`Chat` and `Login`/`Auth` vocabulary difference between Domain and
Server is **not** fixed by this migration (§10, scope).

## 2. Decisions the classification rests on

| # | Decision | Why |
|---|---|---|
| D1 | `Basil.Infrastructure` is the executable | §1.1 |
| D2 | Application references `Basil.Protocol.*`; `TransportSeamTests` re-scoped as the pinned list | §1.2 |
| D3 | Repository/storage **ports stay in `Basil.Domain`** (`IUserRepository`, `IScoreRepository`, `IReplayStorage`, `IPasswordHasher`, … 24 interfaces in `Basil.Domain`, of which about 18 are persistence/technology ports) | v3 says, verbatim, that "all interfaces must be in Application" is the mechanical rule it does *not* want, and that dependency direction is what matters. They are already in the right direction (Domain declares, Infrastructure implements, Application consumes). Moving them plus their `Basil.Domain.Tests` usages buys nothing. Alternative recorded as Q2. |
| D4 | The five Domain files that are application services move to Application | v3 §Application: "Application services / Use cases". `AdminKeyService`, `CredentialVerifier`, `MotdService`, `MirrorService`, `ReplayService` orchestrate repositories/storage; none expresses a business rule. `InMemoryChannelRegistry` stays in Domain — v3 uses it as its own example. |
| D5 | Sessions, eventing core, configuration POCOs, localization, JSON options are Application | All framework-free (verified: their `using` lists are BCL + Domain + `Basil.Protocol.Irc`). Application services already take `IOptions<IrcOptions>` etc.; if these stayed in Infrastructure, Application could not compile. `LocaleCatalog` reads JSON fragments next to the assembly once at startup — a resource loader, not persistence; recorded as a conscious call. |
| D6 | Concrete providers with an external algorithm/library are Infrastructure even when framework-free | `BCryptPasswordHasher` (BCrypt.Net), `GuidTokenGenerator`, `RijndaelScoreDecryptor`, `HttpMirrorSearchClient`, `PpyOsuCalculator`. v3 §Infrastructure: "Concrete providers". |
| D7 | Content's four filesystem services stay Infrastructure, unsplit | `FaqService`, `MenuBannerService`, `MenuIconService`, `MenuSeasonalService` are `File.*`/`Directory.*` throughout; their result enums are part of them. Splitting rules from I/O is the redesign v3 forbids. Their routes and DI stay next to them. |
| D8 | Diagnostics is Infrastructure end to end | Runtime/host instrumentation (`RuntimeMeterListener` is ASP.NET-bound, the samplers read the process). No Application part. |
| D9 | API views/DTOs stay with their routes in Infrastructure, except `PlayerStatusView` | `UserView`, `BeatmapViews`, `ScoreDetailView`, `MatchChatMessage` are read only by routes/endpoints. `PlayerStatusView` is *produced* by `LoginService`, `StatusPublishLogoutHandler` and `ChangeActionHandler` and published through `IPlayerStatusEvents`, so it moves with its producers. |
| D10 | Static meter classes split from their `IHostedService` publishers | `MultiplayerMetrics.cs`, `ChatMetrics.cs`, `IrcMetrics.cs` each hold a static meter (used by Application code, e.g. `InstrumentedMatchLock`) *and* a publisher hosted service. One file becomes two; the meter goes to Application, the publisher stays. `BasilMeter` (root meter) goes to Application. |
| D11 | `MatchRoundEndOutbox.cs` split the same way | `IMatchRoundEndOutbox`, `RoundEndWrite`, `MatchRoundEndOutboxFullException` → Application/Multiplayer (used by `MatchLifecycle`, `AbortHandler`); the `BackgroundService` implementation stays Infrastructure/Multiplayer. This is the one background service that already has the "thin worker over an application contract" shape v3 asks for. |
| D12 | Test projects keep their names | Out of v3 scope. `Basil.Server.Tests` keeps testing Application *and* Infrastructure types via its reference to `Basil.Infrastructure` (transitive). Renaming it is an optional follow-up, not part of this migration. |

## 3. Ownership mapping

Legend: **D** = `Basil.Domain`, **A** = `Basil.Application`, **I** = `Basil.Infrastructure`.
"move" means physical move + namespace change only. Anything marked **split** or **edit** is
called out in §4.

### 3.1 `Basil.Domain` today (71 files) — what leaves

| File | → | Note |
|---|---|---|
| `Auth/AdminKeyService.cs` | A/Auth | orchestrates `ISettingsRepository` + `IPasswordHasher` |
| `Auth/CredentialVerifier.cs` | A/Auth | C1b Unit 7; use case |
| `Content/MotdService.cs` | A/Content | get/set one setting |
| `Beatmaps/MirrorService.cs` (+ `MirrorEndpoints`) | A/Beatmaps | takes `IOptions<MirrorOptions>`, `ILogger`; orchestration |
| `Scores/ReplayService.cs` (+ `ReplayFetchResult`/`Code`) | A/Scores | fetch through `IReplayStorage` + `IScoreRepository` |
| `Spectating/PlayerInputEvents.cs`, `PlayerStatusEvents.cs` and their interfaces | A/Spectating | in-process event fan-out keyed by session id; the interface doc even cross-references `ILiveEventHub`. Not a business concept. |
| everything else (entities, records, enums, parsers, `ChannelSession`, `IChannelRegistry` + `InMemoryChannelRegistry`, `MatchRoomState`, `MatchSlot`, `ICommandReplySink`, all `I*Repository`/`I*Storage`/`IPasswordHasher`/`ITokenGenerator`/`IScoreDecryptor`/`IOsuCalculator`/`IMirrorSearchClient`, `MirrorOptions`, `Geolocation`, `SystemUserIds`) | D | stays; `DomainAdjacency` (14 rows) keeps covering it |

`Basil.Domain.Tests` follows: `AdminKeyServiceTests`, `CredentialVerifierTests`,
`MirrorServiceTests`, `ReplayServiceTests`, `PlayerInputEventsTests` (5 of its 25 files) move to `Basil.Server.Tests` (which
references Application) or a new `Basil.Application.Tests` — see Q3. Everything else in
`Basil.Domain.Tests` stays.

### 3.2 `Basil.Server` per feature

#### Auth (9 files)

| File | → |
|---|---|
| `AuthenticationService.cs`, `ClientIntegrityService.cs`, `LoginService.cs` | A |
| `BCryptPasswordHasher.cs`, `GuidTokenGenerator.cs` (D6), `SqliteIngameLoginRepository.cs`, `AdminKeyAuthenticationHandler.cs`, `AdminKeyRoutes.cs`, `AuthServiceCollectionExtensions.cs` | I |

`LoginService` needs `LoginResponseEncoder` and `PacketBuilders` (today `Shared/Http/Bancho`, pure
`Basil.Protocol` encoders) — both move to **A/Auth** under D2 and join the pinned list. It also
needs `BasilJsonOptions` (→ A/Shared/Json, D5).

#### Beatmaps (17 files)

| File | → |
|---|---|
| `DirectSearchService.cs` | A |
| `BeatmapViews.cs` | I (D9) |
| `BeatmapIngestionService.cs` (fs + osu! rulesets), `BeatmapWatcherService.cs`, `BeatmapsetGarbageCollectorService.cs`, `BeatmapsetMigrationService.cs` (three `BackgroundService`s), `BeatmapsetAssetCache.cs`, `BeatmapAssetRoutes.cs`, `BeatmapsetAssetRoutes.cs`, `BeatmapsetRoutes.cs`, `CachingBeatmapRepository.cs`, `CachingBeatmapsetRepository.cs`, `HttpMirrorSearchClient.cs`, `PpyOsuCalculator.cs`, `SqliteBeatmapRepository.cs`, `SqliteBeatmapsetRepository.cs`, `BeatmapsServiceCollectionExtensions.cs` | I |

Background-service check (v3 §Background Services): `BeatmapWatcherService` and
`BeatmapsetMigrationService` call into `BeatmapIngestionService`, which is itself filesystem +
ruleset-calculator bound — the "use case" underneath is I/O, so there is no Application service to
extract without redesign. Left as-is, recorded in §8 as the one place v3's ideal shape is not met.

#### Bot (5 files)

| File | → |
|---|---|
| `BotBootstrapService.cs`, `BotReplies.cs`, `CommandDispatcher.cs`, `ICommandDispatcher.cs` | A |
| `BotServiceCollectionExtensions.cs` | I |

#### Chat (18 files)

| File | → |
|---|---|
| `ChannelMembershipService.cs`, `ChatDispatchService.cs`, `ChatLine.cs`, `IChannelNotifier.cs`, `IChatNotifier.cs`, `ChannelPartLogoutHandler.cs` | A |
| `ChatMetrics.cs` | **split** (D10): `ChatMetrics` static → A, `ChatMetricsPublisher` → I |
| `Packets/*` (9: the seven handlers, `ChannelNotifier`, `ChatNotifier`), `SqliteChannelRepository.cs`, `ChatServiceCollectionExtensions.cs` | I |

#### Content (16 files)

| File | → |
|---|---|
| `FaqService.cs`, `MenuBannerService.cs`, `MenuIconService.cs`, `MenuSeasonalService.cs` (D7), all seven `*Routes.cs`, `CachingSettingsRepository.cs`, `SqliteMenuBannerRepository.cs`, `SqliteSettingsRepository.cs`, `ContentServiceCollectionExtensions.cs` | I |

Content has no Application file. v3: "only create the feature folder where there is code". Its
application-shaped piece (`MotdService`) comes from Domain (§3.1) and becomes `A/Content`'s only
file — so `Basil.Application/Content/` does exist, with one file.

#### Diagnostics (13 files) — all I (D8)

#### Irc (14 files)

| File | → |
|---|---|
| `IIrcConnection.cs`, `IrcSession.cs`, `IrcSessionRegistry.cs`, `IrcSessionRemovalLogoutHandler.cs`, `IrcAuthenticationService.cs`, `IrcQueryService.cs`, `IrcNamesReply.cs`, `IrcLoginOutcome.cs`, `IrcReplies.cs` | A (the `IrcMessage`-returning ones join the pinned list, D2) |
| `IrcMetrics.cs` | **split** (D10) |
| `TcpIrcConnection.cs` (sockets), `TcpIrcListener.cs` (`BackgroundService`), `BanchoIrcBridgeConnection.cs` (`ServerPacketWriter` adapter), `IrcServiceCollectionExtensions.cs` | I |

#### Multiplayer (76 files)

| File | → |
|---|---|
| `MatchControlService.cs`, `MatchLifecycle.cs`, `MatchMembership.cs`, `MatchBroadcast.cs`, `MatchRecoveryService.cs`, `MatchReportService.cs`, `MatchLiveSnapshotBuilder.cs`, `MpCommandService.cs`, `IMpCommandService.cs`, `MpReplies.cs`, `IMatchNotifier.cs`, `IMatchRegistry.cs`, `InMemoryMatchRegistry.cs`, `MatchSession.cs`, `MatchMutationScope.cs` (+ `IMatchMutationPublisher`), `InstrumentedMatchLock.cs`, `MatchStreams.cs`, `MatchCreationData.cs`, `MatchLeaveLogoutHandler.cs`, `UserBriefResolver.cs`, `Handlers/**` (8 files: Countdown, Lifecycle, Slots) | A |
| `MatchRoundEndOutbox.cs` | **split** (D11) |
| `MultiplayerMetrics.cs` | **split** (D10) |
| `MatchChatMessage.cs` | I (D9; read by `MatchChatEndpoints` and `ChannelMembershipService` — see §4 blocker) |
| `Endpoints/*` (13), `Packets/*` (26 incl. `BanchoMatchNotifier`, `MatchCreationDataMapper`), `MatchPacketDataMapper.cs`, `MatchLiveRoutes.cs`, `MatchRoutes.cs`, `MatchSubResourceRoutes.cs`, `SqliteMatchRepository.cs`, `MultiplayerServiceCollectionExtensions.cs` | I |

`MatchSession` carries `InstrumentedMatchLock`, `StateStream`, `SseSubscriberRegistry` — all
Application under D5, so it moves whole. No further `MatchSession` split.

#### Scores (9 files)

| File | → |
|---|---|
| `ScoreSubmissionService.cs`, `ScoreSubmissionResponseBuilder.cs`, `ScoreSubmissionChartsFormatter.cs` | A (the two formatters build the osu-web response text the use case returns; pure strings, no HTTP) |
| `ScoreDetailView.cs` | I (D9) |
| `RijndaelScoreDecryptor.cs` (D6), `ScoreRoutes.cs`, `SqliteLeaderboardStore.cs`, `SqliteScoreRepository.cs`, `ScoresServiceCollectionExtensions.cs` | I |

#### Spectating (13 files)

| File | → |
|---|---|
| `SpectatorService.cs`, `ISpectatorNotifier.cs`, `PlayerStatusView.cs` (D9), `SpectatorTeardownLogoutHandler.cs`, `StatusPublishLogoutHandler.cs`, `SpectateEvents.cs` | A (`SpectateEvents` carries `ReplayFrame`/`SpectateFrameBundle` from `Basil.Protocol.Bancho` — pinned, D2) |
| `Packets/*` (5 incl. `BanchoSpectatorNotifier`), `PlayerLiveRoutes.cs`, `SpectatingServiceCollectionExtensions.cs` | I |

#### Users (21 files)

| File | → |
|---|---|
| — | A: nothing. Users' application logic already lives in Domain (`User`, parsers) and in Auth. |
| `UserView.cs` (D9), `UserRoutes.cs`, `AvatarRoutes.cs`, `Packets/*` (11), `CachingUserRepository.cs`, five `Sqlite*Repository.cs`, `UsersServiceCollectionExtensions.cs` | I |

### 3.3 `Basil.Server/Shared` and `Host`

| Folder | → | Note |
|---|---|---|
| `Shared/Sessions/` (9) | A, except `LogoutBroadcastHandler.cs` (uses `ServerPacketWriter`) and `GhostDisconnectService.cs` (`BackgroundService`) → I | folder name: Q1 |
| `Shared/Eventing/` (10) | A, except `SseEndpoints.cs` → I/Shared/Http | `StateStream` needs `BasilJsonOptions` + `JsonMergePatch` (both → A/Shared/Json) |
| `Shared/Configuration/` (6) | A | POCOs bound by `IOptions<>`; `DatabaseOptions` has a path helper, still a POCO |
| `Shared/Localization/` (3) | A | D5 |
| `Shared/BasilMeter.cs` | A | D10 |
| `Shared/Http/BasilJsonOptions.cs`, `Shared/Http/JsonMergePatch.cs` | A/Shared/Json | pure `System.Text.Json`; used by Application types |
| `Shared/Http/Bancho/LoginResponseEncoder.cs`, `PacketBuilders.cs` | A/Auth | D2; `PacketBuilders` is also used by five Users packet handlers and `OsuWebRoutes` — fine, Infrastructure references Application |
| `Shared/Http/Bancho/IPacketHandler.cs`, `PacketDispatcher.cs`, rest of `Shared/Http/**` (routes, middleware, OpenAPI, converters, `Pagination`) | I/Shared/Http | |
| `Shared/Media/**`, `Shared/Persistence/**`, `Shared/Storage/**`, `Shared/Logging/**` | I | `IResponseCache` (Shared/Storage) is only used by Infrastructure — stays |
| `Host/**` (20) | I/Host | unchanged content; `Bootstrap`, `SliceRegistration` keep composing everything |

## 4. Dependencies that break on move, and the fix for each

Measured: every Application candidate grepped for every type defined in an Infrastructure-only
file. Eleven hits.

| Blocker type (defined in) | Used by | Fix |
|---|---|---|
| `BasilJsonOptions` (Shared/Http) | `LoginService`, `ChannelMembershipService`, `StatusPublishLogoutHandler`, `StateStream` | move to A/Shared/Json (also used by 14 Infrastructure files — those just re-point their `using`) |
| `JsonMergePatch` (Shared/Http) | `StateStream` | same |
| `LoginResponseEncoder`, `PacketBuilders` (Shared/Http/Bancho) | `LoginService` | move to A/Auth (D2, pinned) |
| `IMatchRoundEndOutbox`, `RoundEndWrite`, `MatchRoundEndOutboxFullException` (`MatchRoundEndOutbox.cs`) | `MatchLifecycle`, `AbortHandler` | split file (D11) |
| `MultiplayerMetrics` (static, same file as publisher) | `InstrumentedMatchLock` | split file (D10) |
| `RuntimeMeterListener`, `DurationAggregateSnapshot` | Diagnostics snapshots | not a blocker — whole slice is I (D8) |
| `InviteResult` | `MpCommandService` | false positive: it is `MatchControlService.InviteResult` (A), name-collides with a private one in `MatchSlotEndpoints` |
| `MatchChatMessage` (D9 → I) | `ChannelMembershipService` (A) | **reverse blocker**: an Application type would reference an Infrastructure DTO. Fix: `MatchChatMessage` moves to A/Multiplayer instead (it is a `(UserBrief, string, DateTimeOffset)` record with no HTTP concern). Amend D9 for this one. |
| `IIrcConnection` (Features/Irc → A) | `UserSession` (A) | not a blocker once both are in A; it is *why* A references `Basil.Protocol.Irc` |
| `ISessionRegistry<GameSession>` etc. | ~60 Application files | not a blocker once Sessions is A |

The `Shared -> Features` pinned list (10 entries, `SliceBoundaryTests`) contains
`GameSession`/`UserSession` (Shared referencing `Features.Irc.IIrcConnection` and
`Features.Multiplayer.MatchSession`) — after the move these are `Basil.Application.Shared.Sessions`
referencing `Basil.Application.Irc` / `Basil.Application.Multiplayer`, still Shared → feature.
The rule is re-scoped per project (§6) and those rows stay pinned; the Media/Http rows move to the
Infrastructure copy of the list.

**What does not need a new abstraction:** nothing in this table. Every fix is a move or a file
split. No interface is introduced.

## 5. Batches

Each batch: move (`git mv`) → Rider *Adjust Namespaces* on the touched folders → fix `using`
lines the IDE reports → `dotnet build` → the four fast test projects → commit. Full
`Basil.IntegrationTests` at the end of Batches 0, 1, 5 and 8 (the ones that touch composition
or sessions), and a `Release` build at Batch 0 and Batch 8. Order is chosen so the solution
compiles at every commit; the only planned red build is inside a batch, never between them.

| # | Batch | Files (≈) | Mechanical? | Cost |
|---|---|---|---|---|
| 0 | **Rename `Basil.Server` → `Basil.Infrastructure`.** `git mv` the directory and csproj; root namespace `Basil.Server` → `Basil.Infrastructure` (Rider rename on the namespace, or *Adjust Namespaces* on the project root); `Features.` segment removed at the same time (`Basil.Infrastructure.Multiplayer.Packets`); `InternalsVisibleTo`; four test csproj references; `Basil.slnx`; `Dockerfile`, `.github/workflows/release.yml`, `DotnetServerHost.cs` executable name; `CategoryEnricher` rule strings; `SliceAdjacency`/`SliceBoundaryTests`/`TransportSeamTests` prefix constants; `docs/` references to `Basil.Server` (grep). No file changes project. | all 315 + tests (namespace only) | yes — Rider does the namespace, the rest is a grep list | **M** (wide but flat; one commit) |
| 1 | **Create `Basil.Application`** (csproj, references, `AssemblyMarker`), add `Basil.Infrastructure → Basil.Application` reference, `Basil.ArchitectureTests → Basil.Application`. Move the cross-cutting layer: `Shared/Configuration`, `Shared/Localization`, `Shared/Json` (2 files out of Shared/Http), `BasilMeter`, `Shared/Eventing` (9 of 10), `Shared/Sessions` (7 of 9), `Irc/IIrcConnection.cs` + `IrcSession.cs` + `IrcSessionRegistry.cs`. Split the three metrics files and `MatchRoundEndOutbox.cs`. **This batch will not compile until `MatchSession` (Batch 5) moves**, because `GameSession.Match` is a `MatchSession` — so Batch 1 and Batch 5's `MatchSession`/`MatchMutationScope`/`InstrumentedMatchLock`/`MatchStreams`/`IMatchRegistry`/`InMemoryMatchRegistry` move together in one commit. | ~45 | mostly; the four file splits are hand edits | **M** |
| 2 | **Domain → Application** (§3.1): five services + two event pairs, plus their tests (Q3). Add `Basil.Domain.Tests` / `Basil.Server.Tests` reference edits. | 9 + tests | yes | **S** |
| 3 | **Auth**: `AuthenticationService`, `ClientIntegrityService`, `LoginService`, `LoginResponseEncoder`, `PacketBuilders`, `PlayerStatusView` (from Spectating, D9). Re-scope `TransportSeamTests` to `Basil.Application` and pin the new names. **The one reasoning batch** (`LoginService` is the largest Application type and the only one that encodes a full bancho response). | 6 | move yes; pinned-list edit is a judgement | **M** |
| 4 | **Chat + Bot** (they are mutually dependent — `Chat -> Bot`, `Bot -> Chat` in `SliceAdjacency`): the six Chat A files, the four Bot A files, `IrcReplies`/`IrcAuthenticationService`/`IrcQueryService`/`IrcNamesReply`/`IrcLoginOutcome`/`IrcSessionRemovalLogoutHandler` (Chat → Irc edge). | 16 | yes | **S** |
| 5 | **Multiplayer** (the rest of §3.2's A list, minus what Batch 1 took): services, handlers, replies, `MatchChatMessage`, `MatchCreationData`, `MatchLeaveLogoutHandler`, `UserBriefResolver`, `IMatchNotifier`, `IMpCommandService`. `Multiplayer -> Bot/Chat/Irc/Scores/Spectating` edges mean Batches 4, 6, 7 must precede or join it: run **4 → 7 → 6 → 5** in practice, or accept 5 as the last feature batch. | ~30 | yes | **M** (largest, but pure moves) |
| 6 | **Scores**: three A files. | 3 | yes | **S** |
| 7 | **Spectating**: `SpectatorService`, `ISpectatorNotifier`, two logout handlers, `SpectateEvents` (pinned). | 5 | yes | **S** |
| 8 | **Beatmaps**: `DirectSearchService`. Then the architecture-test rewrite (§6), `plans/`+`docs/` updates, full suite, Release build. | 1 + tests + docs | tests: hand-written | **M** |

Users, Content, Diagnostics have no Application batch (Content's single A file arrives in Batch 2).

Practical order given the slice cycle (`Chat ↔ Multiplayer ↔ Bot`, `Multiplayer ↔ Scores`,
`Multiplayer ↔ Spectating`): **0, 1(+5 core), 2, 3, 4, 7, 6, 5(rest), 8**. If any of 4/6/7 fails to
compile alone because a Multiplayer service it references has not moved yet, fold it into 5
rather than introducing a temporary shim.

Total: nine commits. Relative cost: Batch 0 and 1 are the wide ones; 3 is the only one where a
line of code changes meaning (the pinned list); everything else is `git mv` + namespaces.

## 6. Verification per batch, and the instruments after

Per batch: `dotnet build` (0 errors), `Basil.ArchitectureTests`, `Basil.Domain.Tests`,
`Basil.Protocol.Tests`, `Basil.Server.Tests` — counts must equal today's 9 / 235 / 158 / 944
(minus tests moved between projects in Batch 2, plus whatever §6's rewrite adds). Full
`Basil.IntegrationTests` (363) after Batches 0, 1, 5, 8. The route table via
`get_endpoint_map` (151 endpoints) after Batch 0 and 8 — a project rename must not move a route.

Architecture tests after the migration (`Basil.ArchitectureTests`, rewritten in Batch 8):

| Rule | Instrument |
|---|---|
| Domain references no Application/Infrastructure/framework/Protocol | `DependencyDirectionTests` (exists; add `Basil.Application`, `Basil.Infrastructure` to the forbidden list) |
| Application references no Infrastructure | compiler (no `ProjectReference`) + one NetArchTest line for the record |
| Application types that touch `Basil.Protocol.*` are exactly the pinned list | `TransportSeamTests`, re-scoped |
| Slice adjacency inside Application and inside Infrastructure | `SliceAdjacency` split into two allowlists with the same 44 rows as the starting point, then pruned: a row belongs to Application if both endpoints have an Application folder, to Infrastructure otherwise. Rows that no longer resolve after the move are deleted — deletions are the measurable win, same as C5. |
| `Shared` must not reference a feature, per project | `SliceBoundaryTests` pinned list split the same way (Sessions rows → Application list; Media/Http rows → Infrastructure list) |
| Domain internal adjacency | `DomainAdjacency` unchanged (14 rows) |

`measure-slice-graph.py` needs its `Features/` path assumption updated to walk
`src/Basil.Application/<Feature>` and `src/Basil.Infrastructure/<Feature>` — one-line change; it
stays a search tool, not a gate.

## 7. Everything outside the three projects that has to change (and only this)

| Where | Change | Why |
|---|---|---|
| `Basil.slnx` | project paths; add `Basil.Application` under `/Sources/` | Batch 0/1 |
| `tests/Basil.Server.Tests.csproj`, `Basil.IntegrationTests.csproj`, `Basil.ArchitectureTests.csproj` | `ProjectReference` paths; ArchitectureTests adds Application | references only |
| `src/Basil.Infrastructure.csproj` | `InternalsVisibleTo Basil.Server.Tests` stays; `Basil.Application.csproj` gets the same line | tests use internals |
| `Dockerfile`, `.github/workflows/release.yml` | `src/Basil.Server` → `src/Basil.Infrastructure`, `ENTRYPOINT ["./Basil.Infrastructure"]` | executable name (D1) |
| `tests/Basil.LoadTests/Hosting/DotnetServerHost.cs` | two `"Basil.Server"`/`"Basil.Server.exe"` literals | same |
| `tests/**` | `using Basil.Server.*` → new namespaces | Rider fixes on build; no logic change |
| `CLAUDE.md` Architecture section, `docs/for-developers/architecture.md` (already banner-ed as out of date), `plans/execution/HANDOVER.md` | describe the three projects | Batch 8 |

Not touched: `Basil.Protocol.*`, `Basil.LoadTests` beyond the two literals, `Basil.Domain.Tests`
beyond Batch 2's test moves, any package version.

## 8. Risks and edge cases

1. **`Basil.Server.Tests` becomes a misnomer** — it will test both Application and
   Infrastructure. Accepted (D12); rename is a follow-up.
2. **`LocaleCatalog` in Application** reads files. If the user prefers it in Infrastructure, the
   reply constants (`MpReplies`, `BotReplies`, `IrcReplies`) it feeds must go with it, and then
   `MpCommandService`/`CommandDispatcher`/`IrcQueryService` cannot be Application — the cost is
   the whole Bot/Chat/Irc application layer. That is why D5 keeps it in Application.
3. **The Beatmaps background services** do not get the thin-worker shape (§3.2 Beatmaps). Recorded,
   not fixed.
4. **`WebApplicationFactory<Bootstrap>`** (40 integration tests) keeps working because `Bootstrap`
   stays in the executable project; only its namespace changes (`Basil.Infrastructure.Host`).
5. **Content-root and locale copies**: `Basil.Server.csproj`'s `Content Update` items for
   `Features/**/Locale/*.json` reference the `Features/` path — Batch 0 removes that segment, so
   the glob becomes `**/Locale/*.json`. Check the locale files still land in
   `Data/Localization/` of every referencing project's output after Batch 0 (`LocaleCatalogTests`
   covers it).
6. **Rider *Adjust Namespaces* on 315 files** is the single biggest mechanical step. Do it per
   folder, build between folders; commit only when the whole batch is green.
7. **Slice cycle** (`Chat ↔ Bot ↔ Multiplayer`) can force Batches 4/5 to merge — planned for in §5.
8. **`OpenApiDocumentEndpointTests`** snapshot: namespaces appear nowhere in the generated
   document (schema ids are type names), so it should not change; if it does, that is a finding,
   not a fix.

## 9. What the old plan loses

`plans/basil-plan-20260909.md`:

* **Stage D2, D3, D4 — obsolete.** Three host projects, `Basil.Host`, the `AnnounceRoutes` event,
  and "no host references another host" have no counterpart in v3. `Basil.Server` is still renamed
  to `Basil.Infrastructure`, but as Batch 0 of this plan, for v3's reason. The D2 investigation in
  `plans/execution/stage-d-progress.md` (the missing `Basil.Host`, the csproj split) is moot: under
  D1 the executable does not move.
* **Stage D1 — done and kept.** `Basil.Protocol.Bancho`/`.Irc` exist; v3 leaves other projects
  alone.
* **Stage E1** (declare the three surviving relationships) — still valid, now expressed as rows in
  the split `SliceAdjacency` lists (§6). **Stage E2** — replaced by §6's table.
* **Stage G — done.** Unchanged.
* **Stage H** (ADRs, localization rules doc, `architecture.md` rewrite, full verification) —
  still valid; runs after Batch 8, against the three-project layout.
* `architecture-target-20260908.md` §3 (five/six-project target) is superseded by v3 for the
  project layout; its invariants about Domain purity and the transport seam survive.

## 10. What is deliberately **not** done

* No `Basil.Host` extraction, no `Basil.Hosts.Bancho/.Irc/.Api`.
* No `GameSession`/`UserSession` split. They move whole to Application.
* No moving of repository ports out of Domain (D3), unless Q2 is answered the other way.
* No new interfaces. Every seam this migration needs already exists.
* No renaming of features, including the Domain `Channels`/`Login` vs Server `Chat`/`Auth`
  vocabulary gap. Recorded; a separate decision.
* No renaming of test projects (D12).
* No changes to `Basil.Protocol.*`, `Basil.LoadTests` (beyond §7), package versions,
  `Directory.Build.props`, `Directory.Packages.props`.
* No splitting of the Content filesystem services or the Beatmaps ingestion pipeline into
  Application + Infrastructure halves.
* No `Services/`, `Repositories/`, `Handlers/`, `Managers/`, `Helpers/` folders. `Shared/` is the
  only non-feature folder, and only for what is already there.

## 11. Open questions for the user

**Q1 — Folder/namespace for sessions.** `Shared/Sessions/` has no feature name, and v3 forbids
global technical folders that lose feature ownership. Three options: (a) keep
`Basil.Application/Shared/Sessions` (cross-cutting, like Eventing — the plan's default);
(b) promote it to a feature: `Basil.Application/Sessions/`, `Basil.Infrastructure/Sessions/`
(for `GhostDisconnectService`, `LogoutBroadcastHandler`); (c) fold into `Auth`. (b) reads best
against v3's "follow one feature across projects" goal; (a) is zero-decision. ~9 files either way.

**Q2 — Repository ports.** D3 keeps the ≈18 `I*Repository`/storage/provider interfaces in
`Basil.Domain`. v3's Application section lists "contracts the application needs" and its Domain
section warns against interfaces that only hide a database. If the user wants them in
`Basil.Application/<Feature>/`, it is one more mechanical batch (≈18 files, plus `Basil.Domain.Tests`
usages of `IUserRepository` etc.), best run as Batch 2b. Default: leave them.

**Q3 — Where the moved Domain services' tests go.** `Basil.Server.Tests` (references
Infrastructure, can see Application) or a new `Basil.Application.Tests`. Default: a new
`Basil.Application.Tests` mirroring `Basil.Domain.Tests`' csproj — one project, ~5 test files,
keeps the 1:1 test-project convention.

**Q4 — `Basil.Server.Tests` rename** to `Basil.Infrastructure.Tests` in Batch 0, or leave (D12).
Default: leave.

Nothing else needs a decision before Batch 0 can start.
