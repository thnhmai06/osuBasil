# Architecture v3 migration plan — Domain / Application / Infrastructure + Host projects

**Status: PLAN ONLY. Nothing below has been executed.** Written 2026-09-16 against `52e29999` on
`feat/vsa-migration`, revised the same day after the user's answers (§0.2). Sources: the user's
`osuBasil-architecture-v3.md`, the current tree, `plans/execution/`. The user reviews this before
Batch 0 runs.

The specification's one-line summary, which every decision here is checked against:

> Project boundary changes, feature boundary and feature vocabulary do not.

## 0. Frame

### 0.1 Target

```text
Basil.Domain            business model and rules.                      -> (nothing)
Basil.Protocol.Bancho   bancho wire format.                            -> (nothing)   } peers of Domain
Basil.Protocol.Irc      IRC wire format.                               -> (nothing)   }

Basil.Application       use cases, application services, ports,       -> Domain, Protocol.Bancho, Protocol.Irc
                        in-process session/eventing state

Basil.Infrastructure    persistence, storage, media, external          -> Application
                        services, background services, concrete providers

Basil.Host.Bancho       osu! client transport: c.* packet exchange,     -> Application, Infrastructure, Protocol.Bancho
                        osu. (/web/*.php, /d/), b. beatmap assets
Basil.Host.Irc          IRC TCP transport                              -> Application, Infrastructure, Protocol.Irc
Basil.Host.Api          api., assets., a.: envelope, OpenAPI, SSE,     -> Application, Infrastructure
                        routes, views, image providers

Basil.Host              entry point and composition (the executable)  -> everything
```

Rules the compiler enforces by absent references: Domain and Protocol are leaves; Application
never sees Infrastructure or a host; no host references another host; only `Basil.Host` sees all
three hosts. Rules a test enforces: `Basil.Host.Api` never depends on `Basil.Protocol.Bancho` even
though it *could* through Application (§7).

Feature is the folder axis in every project. Same names as today's slices:
`Auth, Beatmaps, Bot, Chat, Content, Diagnostics, Irc, Multiplayer, Scores, Spectating, Users`,
plus **`Sessions`** promoted from `Shared/Sessions` to a feature (user's call, §0.2). `Shared/`
survives only for `Eventing`, `Configuration`, `Localization`, `Json` in Application and for
`Persistence`, `Storage`, `Media`, `Http` in Infrastructure/hosts — cross-cutting code that
already lives under that name. No `Services/`, `Repositories/`, `Handlers/`, `Managers/`.

Namespaces follow folders with no `Features.` segment: `Basil.Application.Multiplayer`,
`Basil.Infrastructure.Beatmaps`, `Basil.Host.Bancho.Multiplayer.Packets`,
`Basil.Application.Sessions`, `Basil.Application.Shared.Eventing`.

### 0.2 Decisions the user made on 2026-09-16 (not re-opened here)

| # | Decision |
|---|---|
| U1 | Host projects **are** wanted: `Basil.Host` (entry point) + `Basil.Host.Bancho`, `Basil.Host.Irc`, `Basil.Host.Api`. Protocol projects sit beside Domain as leaves. |
| U2 | Host assignment follows `architecture-target-20260908.md` §3.3: Bancho = `c.`/`ce.`/`c4.`/`c5.`/`c6.` + `osu.` + `b.`; Irc = the TCP listener; Api = `api.` + `assets.` + `a.`. |
| U3 | `Basil.Application` may reference `Basil.Protocol.*`; `TransportSeamTests` becomes the pinned list of Application types that do. |
| U4 | Order: Application/Infrastructure split first, host carve-out after. |
| U5 | Sessions stay whole (`UserSession`/`GameSession`/`IrcSession` keep `Enqueue`/`Dequeue`/`IrcConnection`). The old Task C2 split stays off the path. |
| U6 | `Sessions` is a feature folder, not `Shared/`. |
| U7 | Repository/storage/provider ports move from Domain to `Basil.Application/<Feature>/`. |
| U8 | Tests split 1:1 with source: `Basil.Application.Tests`, `Basil.Infrastructure.Tests`, `Basil.Host.Tests`, `Basil.Host.Bancho.Tests`, `Basil.Host.Irc.Tests`, `Basil.Host.Api.Tests`; `Basil.Domain.Tests`, `Basil.Protocol.Tests`, `Basil.IntegrationTests`, `Basil.LoadTests` keep their names. `Basil.Server.Tests` dissolves. |
| U9 | After this plan is updated: stop, wait for review. |

### 0.3 Why the middle is mechanical

Stages A–C of the previous plan did the hard part and it carries over: business types in Domain
with `DomainAdjacency`; every business service reaches bancho/IRC through a contract
(`IChatNotifier`, `IChannelNotifier`, `IMatchNotifier`, `ISpectatorNotifier`,
`LoginResponseEncoder`) with `TransportSeamTests` pinning the three exceptions; `MatchSession`'s
plain state already in Domain; logout as an ordered handler list; Protocol already two leaf
projects.

Measured on the current tree: of the 315 `.cs` files in `src/Basil.Server`, **205 are
Infrastructure-or-host by technology** (ASP.NET, Dapper, filesystem, ImageSharp/FFmpeg/osu!
rulesets, `BackgroundService`, packet handlers, routes, DI, the host) and **110 are Application
candidates**. Only **11 types block a move**; 9 of the 11 are a file split or a small pure helper
moving along. The host carve-out adds **one** new abstraction (§4, `AnnounceRoutes`), because
`Basil.Host.Api` may not encode a bancho packet — everything else already has its seam.

## 1. Where every file goes

Legend: **D** Domain, **A** Application, **I** Infrastructure, **HB** Host.Bancho, **HI** Host.Irc,
**HA** Host.Api, **H** Host (entry). "move" = `git mv` + namespace only. **split**/**edit** are
listed in §4.

### 1.1 `Basil.Domain` (71 files) — what leaves

| File(s) | → |
|---|---|
| `Auth/AdminKeyService.cs`, `Auth/CredentialVerifier.cs`, `Content/MotdService.cs`, `Beatmaps/MirrorService.cs` (+`MirrorEndpoints`, `MirrorOptions`), `Scores/ReplayService.cs` (+`ReplayFetchResult`) | A/<same feature> — application services (they orchestrate ports; no business rule) |
| `Spectating/IPlayerInputEvents.cs`, `PlayerInputEvents.cs`, `IPlayerStatusEvents.cs`, `PlayerStatusEvents.cs` | A/Spectating — in-process event fan-out |
| Ports (U7): `Auth/ILoginRepository`, `IPasswordHasher`, `ITokenGenerator`; `Beatmaps/IBeatmapRepository`, `IBeatmapsetRepository`, `IMirrorSearchClient`, `IOsuCalculator`; `Bot/ICommandReplySink`; `Channels/IChannelRepository`; `Content/IMenuBannerRepository`, `ISettingsRepository`; `Multiplayer/IMatchRepository`; `Scores/ILeaderboardStore`, `IReplayStorage`, `IScoreDecryptor`, `IScoreRepository`; `Social/IRelationshipRepository`; `Users/IClientHashRepository`, `IUserLogRepository`, `IUserRepository`, `IUserStatRepository` (20 files) | A/<same feature>. Measured: no remaining Domain type references any of them once the five services above leave. `IChannelRegistry` + `InMemoryChannelRegistry` **stay** — v3 uses them as its own example of a Domain concept. |
| everything else (entities, records, enums, parsers, `ChannelSession`, `MatchRoomState`, `MatchSlot`, `Geolocation`, `SystemUserIds`, …) | D |

`Basil.Domain.Tests` → `AdminKeyServiceTests`, `CredentialVerifierTests`, `MirrorServiceTests`,
`ReplayServiceTests`, `PlayerInputEventsTests` (5 of 25) move to `Basil.Application.Tests`.

### 1.2 `Basil.Server` — per feature

**Auth** (9): `AuthenticationService`, `ClientIntegrityService`, `LoginService` → A.
`LoginResponseEncoder`, `PacketBuilders` (from `Shared/Http/Bancho`) → A/Auth (U3, pinned).
`BCryptPasswordHasher`, `GuidTokenGenerator`, `SqliteIngameLoginRepository` → I (concrete
providers). `AdminKeyAuthenticationHandler`, `AdminKeyRoutes` → HA. `AuthServiceCollectionExtensions`
→ split per owning project (§4).

**Beatmaps** (17): `DirectSearchService` → A. `BeatmapIngestionService`, `BeatmapWatcherService`,
`BeatmapsetGarbageCollectorService`, `BeatmapsetMigrationService`, `BeatmapsetAssetCache`,
`CachingBeatmapRepository`, `CachingBeatmapsetRepository`, `HttpMirrorSearchClient`,
`PpyOsuCalculator`, `SqliteBeatmapRepository`, `SqliteBeatmapsetRepository` → I.
`BeatmapAssetRoutes` (`b.`) → HB. `BeatmapsetRoutes`, `BeatmapsetAssetRoutes` (`assets.`),
`BeatmapViews` → HA. DI ext → split.

**Bot** (5): `BotBootstrapService`, `BotReplies`, `CommandDispatcher`, `ICommandDispatcher` → A. DI
ext → A (it registers only Application types).

**Chat** (18): `ChannelMembershipService`, `ChatDispatchService`, `ChatLine`, `IChannelNotifier`,
`IChatNotifier`, `ChannelPartLogoutHandler` → A. `ChatMetrics.cs` **split**: static → A, publisher →
I. `Packets/*` (7 handlers + `ChannelNotifier` + `ChatNotifier`) → HB. `SqliteChannelRepository` → I.
DI ext → split.

**Content** (16): `FaqService`, `MenuBannerService`, `MenuIconService`, `MenuSeasonalService`
(filesystem throughout — unsplit), `CachingSettingsRepository`, `SqliteMenuBannerRepository`,
`SqliteSettingsRepository` → I. Seven `*Routes.cs` → HA, with `AnnounceRoutes` calling a new
`IAnnouncementNotifier` (§4). `MenuAssetRoutes` (`assets.`) → HA. A/Content holds `MotdService`
(from Domain), `ISettingsRepository`, `IMenuBannerRepository`, and the new notifier contract.

**Diagnostics** (13): samplers, snapshots, `RuntimeMeterListener` (its "ASP.NET" mention is a
meter-name string, no framework reference), `DiagnosticBroadcastService`, `DiagnosticStreams` → I.
`DiagnosticRoutes` → HA. No Application part.

**Irc** (14): `IIrcConnection`, `IrcSession`, `IrcSessionRegistry`,
`IrcSessionRemovalLogoutHandler`, `IrcAuthenticationService`, `IrcQueryService`, `IrcNamesReply`,
`IrcLoginOutcome`, `IrcReplies` → A (the `IrcMessage`-returning ones pinned, U3). `IrcMetrics.cs`
**split**. `TcpIrcConnection`, `TcpIrcListener` → HI. `BanchoIrcBridgeConnection` (adapts a
`GameSession` to `IIrcConnection` with `ServerPacketWriter`) **stays in A** (Batch 11: `GameSession`'s
own constructor self-constructs it, so moving it to HB would force an A→host dependency; reverted,
documented in code). DI ext → split.

**Multiplayer** (76): `MatchControlService`, `MatchLifecycle`, `MatchMembership`, `MatchBroadcast`,
`MatchRecoveryService`, `MatchReportService`, `MatchLiveSnapshotBuilder`, `MpCommandService`,
`IMpCommandService`, `MpReplies`, `IMatchNotifier`, `IMatchRegistry`, `InMemoryMatchRegistry`,
`MatchSession`, `MatchMutationScope`, `InstrumentedMatchLock`, `MatchStreams`, `MatchCreationData`,
`MatchChatMessage`, `MatchLeaveLogoutHandler`, `UserBriefResolver`, `Handlers/**` (8) → A.
`MatchRoundEndOutbox.cs` **split** (contract → A, worker → I). `MultiplayerMetrics.cs` **split**.
`SqliteMatchRepository` → I. `Packets/*` (26, incl. `BanchoMatchNotifier`, `MatchCreationDataMapper`),
`MatchPacketDataMapper` → HB. `Endpoints/*` (13), `MatchLiveRoutes`, `MatchRoutes`,
`MatchSubResourceRoutes` → HA. DI ext → split.

**Scores** (9): `ScoreSubmissionService`, `ScoreSubmissionResponseBuilder`,
`ScoreSubmissionChartsFormatter` → A. `RijndaelScoreDecryptor`, `SqliteLeaderboardStore`,
`SqliteScoreRepository` → I. `ScoreRoutes`, `ScoreDetailView` → HA. DI ext → split.

**Spectating** (13): `SpectatorService`, `ISpectatorNotifier`, `PlayerStatusView` (produced by
Application code, published through `IPlayerStatusEvents`), `SpectatorTeardownLogoutHandler`,
`StatusPublishLogoutHandler`, `SpectateEvents` (pinned — carries `ReplayFrame`) → A. `Packets/*`
(5, incl. `BanchoSpectatorNotifier`) → HB. `PlayerLiveRoutes` → HA. DI ext → split.

**Users** (21): nothing → A. `CachingUserRepository`, five `Sqlite*` → I. `Packets/*` (11) → HB.
`UserRoutes`, `AvatarRoutes` (`a.`), `UserView` → HA. DI ext → split.

### 1.3 `Shared/` and `Host/`

| Today | → |
|---|---|
| `Shared/Sessions/` — `UserSession`, `GameSession`, `GameSessionRegistry`, `ISessionRegistry`, `IPlayerLogoutHandler`, `PlayerLogoutService`, `GameSessionRegistryRemovalLogoutHandler` | A/**Sessions** (U6) |
| `Shared/Sessions/GhostDisconnectService` (`BackgroundService`) | I/Sessions |
| `Shared/Sessions/LogoutBroadcastHandler` (`ServerPacketWriter`) | HB/Sessions |
| `Shared/Eventing/` (9 of 10) | A/Shared/Eventing |
| `Shared/Eventing/SseEndpoints` | HA/Shared/Http |
| `Shared/Configuration/` (6 option POCOs) | A/Shared/Configuration |
| `Shared/Localization/` (3) + every `Features/*/Locale/*.json` | A/Shared/Localization and A/<Feature>/Locale; the csproj `Content Update` glob moves to `Basil.Application.csproj` so the fragments still land in `Data/Localization/` of every referencing project |
| `Shared/BasilMeter.cs` | A/Shared |
| `Shared/Http/BasilJsonOptions`, `JsonMergePatch`, `CountryJsonConverter`, `TimeSpanSecondsJsonConverter` | A/Shared/Json (pure `System.Text.Json`; `BasilJsonOptions` is used by `StateStream`, `LoginService`, `ChannelMembershipService`) |
| `Shared/Http/Bancho/IPacketHandler`, `PacketDispatcher`; `Shared/Http/BanchoProtocolRoutes`, `OsuWebRoutes` | HB/Shared/Http |
| `Shared/Http/BanchoHostGroups` | **split** by host: each host maps its own `Map*Group` on the groups it owns; `Basil.Host` creates the `WebApplication` and hands each host its groups (§4) |
| rest of `Shared/Http/**` — `ApiHostRoutes`, `AssetsHostRoutes`, `AbbreviationRedirectRoutes`, `Middleware/*` (5), `OpenApi/*` (9), `ContentTypes`, `NumericIdRouteConstraint`, `Pagination`, `RouteDocs`, `HttpMetrics`, `DateTimeExtensions` | HA/Shared/Http |
| `Shared/Media/Assets/*` (ImageSharp.Web providers, 8) | HA/Shared/Media (they serve `a.`/`assets.`) |
| `Shared/Media/FfmpegAudioExtractor`, `IAudioExtractor` | I/Shared/Media |
| `Shared/Persistence/**`, `Shared/Storage/**` | I |
| `Shared/Logging/**` (Serilog enricher, hard-link hooks) | H/Logging (only `SerilogSetup` uses them) |
| `Host/**` (20) | H — `Bootstrap`, `SliceRegistration`, eight `*Setup`, `CommandLine`, `StartupData`, `StartupBanner`, `BuildVersion`, `UpdateProbe`/`VelopackUpdateProbe`, `StartupUpdateCheck`, `DomainAdvertiser`, `LocaleTouch`, `SharedInfrastructureServiceCollectionExtensions` |

## 2. Project files

| Project | SDK | Packages (from today's `Basil.Server.csproj`) | Refs |
|---|---|---|---|
| `Basil.Application` | `Microsoft.NET.Sdk` | `Microsoft.Extensions.Logging.Abstractions`, `Options`, `DependencyInjection.Abstractions`; `Hosting.Abstractions` only if an Application type takes `IHostApplicationLifetime` (the compiler decides in Batch 2) | Domain, Protocol.Bancho, Protocol.Irc |
| `Basil.Infrastructure` | `Microsoft.NET.Sdk` (**not** Web, from Batch 12) | `Dapper`, `dbup-sqlite`, `Microsoft.Data.Sqlite`, `BCrypt.Net-Next`, `BouncyCastle.Cryptography`, `FFMpegCore`, `SixLabors.ImageSharp` (not `.Web`), `ppy.osu.Game.Rulesets.*` (4), `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Http`, `Hosting.Abstractions`; embedded `Migrations/*.sql`, the two avatar PNGs | Application |
| `Basil.Host.Bancho` | `Microsoft.NET.Sdk` + `<FrameworkReference Include="Microsoft.AspNetCore.App"/>` | — | Application, Infrastructure, Protocol.Bancho |
| `Basil.Host.Irc` | `Microsoft.NET.Sdk` | `Hosting.Abstractions` | Application, Infrastructure, Protocol.Irc |
| `Basil.Host.Api` | `Microsoft.NET.Sdk` + `FrameworkReference` | `Microsoft.AspNetCore.OpenApi`, `Microsoft.OpenApi`, `Microsoft.Extensions.ApiDescription.Server`, `Scalar.AspNetCore`, `SixLabors.ImageSharp.Web` | Application, Infrastructure |
| `Basil.Host` | `Microsoft.NET.Sdk.Web`, `SelfContained`, icon, manifest, `RemoveUnusedOsuRulesetRuntimeFiles` target, `Data/appsettings.json`, `docs-site/**`, dev cert, default banner, `Velopack`, `Makaretu.Dns.Multicast.New`, `Serilog.*` (4) | everything |

`InternalsVisibleTo`: each source project → its own `*.Tests` project only.

Test projects (U8), each `xunit.v3` like today's: `Basil.Application.Tests` ← Application;
`Basil.Infrastructure.Tests` ← Infrastructure; `Basil.Host.Bancho.Tests` ← Host.Bancho;
`Basil.Host.Irc.Tests` ← Host.Irc; `Basil.Host.Api.Tests` ← Host.Api; `Basil.Host.Tests` ← Host
(`CommandLineTests`, `CompositionRootTests`, `StartupUpdateCheckTests`, `ConfigurationSourceTests`,
`LocaleCatalogTests`). `Basil.IntegrationTests` ← `Basil.Host` (`WebApplicationFactory<Bootstrap>`
keeps working; `Bootstrap` stays public). `Basil.ArchitectureTests` ← all source projects.

Today's `Basil.Server.Tests` (150 files) maps by folder: `Features/*/Packets/*` (46) → Host.Bancho;
`Features/Irc/TcpIrcConnectionTests` → Host.Irc; `Features/Diagnostics/*` (10), `Sqlite*`/`Caching*`
tests, `Shared/Persistence`, `Shared/Storage`, `Shared/Media`, Content fs-service tests →
Infrastructure; `Host/*` (5) → Host; `Shared/Http/*` (envelope/OpenAPI/JSON) → Host.Api or
Application.Tests for the JSON ones; everything else (services, sessions, eventing, localization,
chat, multiplayer non-packet, bot, auth services) → Application.

## 3. Decisions (mine, with reasons)

| # | Decision | Why |
|---|---|---|
| D1 | `Basil.Host` is extracted **first** (Batch 0), before the Application split | Sole executable rename (`Basil.Server` → `Basil.Host`) happens once; `Dockerfile`, `release.yml`, `DotnetServerHost.cs` change once. Every later batch then moves library code only. This is the one place the plan bends U4: it pulls the *entry-point shell* ahead, not the transport hosts. |
| D2 | `Basil.Server` is renamed to `Basil.Infrastructure` and shrinks | Rider handles the namespace rename; files leave it batch by batch. It keeps `Sdk.Web` until Batch 12 empties it of routes, then drops to plain `Sdk`. |
| D3 | The three metrics files and `MatchRoundEndOutbox.cs` split into contract + worker | Application code (`InstrumentedMatchLock`, `MatchLifecycle`, `AbortHandler`) uses the static meter / outbox contract; the `IHostedService` half is Infrastructure. Already the "thin worker over an application contract" shape v3 asks for. |
| D4 | Content's four filesystem services and the Beatmaps ingestion pipeline are Infrastructure, unsplit | I/O throughout; splitting rules from I/O is the redesign v3 forbids. The Beatmaps background services therefore do not get the thin-worker shape — recorded in §8. |
| D5 | Views stay with their routes in Host.Api except `PlayerStatusView` and `MatchChatMessage` | The two are produced/consumed by Application services. |
| D6 | DI registration: each project owns `Add<Feature>` for the types it declares; `Basil.Host.SliceRegistration` calls them in the same order as today | Mechanical split of today's 11 `*ServiceCollectionExtensions`; no registration changes meaning. |
| D7 | `BanchoHostGroups` splits by host | `Basil.Host` builds the app, resolves `ServerOptions.Domain`, creates the seven host groups, and calls `Basil.Host.Bancho.MapBancho(groups)`, `Basil.Host.Api.MapApi(groups)`, `Basil.Host.Irc` registers its listener. Same route templates, same order; verified by the endpoint map (151). |
| D8 | One new contract, `IAnnouncementNotifier` (A/Content), implemented in HB/Content | `AnnounceRoutes` (Api) encodes `ServerPacketWriter.Notification` and enqueues to every `GameSession`; under "no host references another host" and the Api-never-sees-bancho test that cannot stay. The route resolves the sessions as today and calls `Announce(session, text)`; the HB implementation is the two lines that moved. This is the only new abstraction in the plan. |
| D9 | `LoginService` stays whole in Application (U3) | 413 lines of orchestration over one encoder call; pinned, not split. |
| D10 | `LocaleCatalog` is Application | It loads JSON fragments once at startup; the reply constants it feeds (`MpReplies`, `BotReplies`, `IrcReplies`) are used by Application services, so anywhere else forces the whole Bot/Chat/Irc application layer out of Application. |
| D11 | `Diagnostics -> Auth` stays the only Diagnostics edge; Diagnostics has no Application folder | `diagnostics-boundary-decision.md` still holds. |

## 4. Every non-move edit, listed

| Edit | Where | Size |
|---|---|---|
| `BanchoProtocolRoutes` takes `ILogger<Bootstrap>` for its log category | replace with `loggerFactory.CreateLogger("Host")`; add `("Host", false, "Host")` to `CategoryEnricher.Rules` | 3 lines |
| `MatchRoundEndOutbox.cs`, `MultiplayerMetrics.cs`, `ChatMetrics.cs`, `IrcMetrics.cs` | cut each into two files; no code inside a type changes | 4 files |
| 11 `*ServiceCollectionExtensions.cs` | cut each by owning project; `SliceRegistration` calls the pieces | ≤ 11 × 3 files |
| `BanchoHostGroups` | split: group creation in `Basil.Host`, `Map*Group` per host (D7) | 1 file → 3 |
| `AnnounceRoutes` + new `IAnnouncementNotifier` + `BanchoAnnouncementNotifier` | D8 | ~30 lines |
| `Basil.Server.csproj` | becomes two csproj (Host exe shell, Infrastructure library) — the split the D2 investigation in `stage-d-progress.md` measured | 1 |
| `Dockerfile`, `.github/workflows/release.yml`, `tests/Basil.LoadTests/Hosting/DotnetServerHost.cs` (2 literals) | `Basil.Server` → `Basil.Host` | 4 lines |
| `CategoryEnricher.Rules` | namespace strings (`Basil.Server.Features.X.` → the new prefixes; add both Application and host prefixes where a category spans them) | ~12 lines |
| `Basil.slnx` | new projects under `/Sources/`, `/Sources/Host/`, `/Tests/` (flat folder names) | 1 |
| `measure-slice-graph.py` | walk `src/Basil.Application/<Feature>`, `src/Basil.Infrastructure/<Feature>`, `src/Basil.Host.*/<Feature>` | 1 |
| Architecture tests | §7 | rewrite |

Not on the list: no interface other than D8, no session split, no service rewrite.

## 5. Batches

Each batch: `git mv` → Rider *Adjust Namespaces* on the touched folders → `dotnet build` (0
errors) → the fast test projects → commit (local; not pushed until the user says). Full
`Basil.IntegrationTests` after Batches 0, 2, 9, 11, 13; `Release` build after 0, 9, 13; endpoint
map (151) after 0, 12, 13.

| # | Batch | Files (≈) | Mechanical? | Cost |
|---|---|---|---|---|
| **0** | **Extract `Basil.Host`.** New exe project takes `Host/**` (20), `Shared/Logging` (2), the exe csproj settings and Host-shaped content items; `Basil.Server` becomes a library referenced by `Basil.Host`; `BanchoProtocolRoutes` logger straggler fixed; `Basil.Host.Tests` created with the 5 Host tests (+`LocaleCatalogTests`); `Basil.IntegrationTests` → `Basil.Host`; `Dockerfile`/`release.yml`/`DotnetServerHost.cs`; `dotnet run --project src/Basil.Host` smoke check. Namespace stays `Basil.Server.Host` for this batch (renamed in Batch 1 with everything else). | 27 + csproj | csproj split is by hand; rest is moves | **M** |
| **1** | **Rename `Basil.Server` → `Basil.Infrastructure`** (library). Rider rename of the root namespace, `Features.` segment dropped (`Basil.Infrastructure.Multiplayer.Packets`), `Basil.Server.Host` → `Basil.Host`; `Basil.Server.Tests` → `Basil.Infrastructure.Tests` (whole, for now); `CategoryEnricher` strings; architecture-test prefix constants; `Basil.slnx`; `CLAUDE.md` project names. | all remaining ~295 + tests (namespace only) | yes | **M** (wide, flat) |
| **2** | **Create `Basil.Application`** + the cross-cutting layer: `Sessions/` (7), `Shared/Eventing` (9), `Shared/Configuration` (6), `Shared/Localization` (3) + all `Locale/*.json` with the content glob, `Shared/Json` (4), `BasilMeter`, the four file splits (D3), `Irc/{IIrcConnection,IrcSession,IrcSessionRegistry}`, and the Multiplayer core `MatchSession`, `MatchMutationScope`, `InstrumentedMatchLock`, `MatchStreams`, `IMatchRegistry`, `InMemoryMatchRegistry` (because `GameSession.Match` is a `MatchSession`). `Basil.Application.Tests` created; matching tests move. | ~50 | yes, plus 4 splits | **M** |
| **3** | **Domain → Application** (§1.1): 5 services, 2 event pairs, 20 ports, `MirrorOptions`; 5 Domain tests move. `DependencyDirectionTests` gains `Basil.Application` in Domain's forbidden list. | 32 + 5 tests | yes | **S** |
| **4** | **Auth** → A: 3 services + `LoginResponseEncoder`/`PacketBuilders` + `PlayerStatusView`. `TransportSeamTests` re-scoped to `Basil.Application` and pinned (§7). **The one reasoning batch.** | 6 | move yes; pinned list is a judgement | **M** |
| **5** | **Chat + Bot + Irc** application types (the `Chat ↔ Bot`, `Chat -> Irc` edges): 6 + 4 + 6 files, their DI pieces. | 16 | yes | **S** |
| **6** | **Spectating** → A (5). | 5 | yes | **S** |
| **7** | **Scores** → A (3). | 3 | yes | **S** |
| **8** | **Multiplayer** rest → A (~24) — last because `Multiplayer -> Bot/Chat/Irc/Scores/Spectating`. If 5–7 do not compile alone, fold them into 8; no shims. | 24 | yes | **M** |
| **9** | **Beatmaps** `DirectSearchService` → A; DI splits finished; architecture tests pass 1 (Application/Infrastructure rules, §7); `plans/`+`docs/` pass 1. Full suite, Release. **Milestone: v3 without hosts.** | 1 + tests + docs | tests by hand | **M** |
| **10** | **`Basil.Host.Irc`**: `TcpIrcListener`, `TcpIrcConnection`, `IrcMetricsPublisher`, `AddIrcHost`; `Basil.Host.Irc.Tests` (`TcpIrcConnectionTests`). `Basil.Host` references it. | 4 + tests | yes | **S** |
| **11** | **`Basil.Host.Bancho`**: 46 packet handlers + `MatchNotifier`/`SpectatorNotifier` (renamed from `BanchoMatchNotifier`/`BanchoSpectatorNotifier`) + `LogoutBroadcastHandler` + `MatchPacketDataMapper` + `MatchCreationDataMapper` + `IPacketHandler`/`PacketDispatcher` + `BanchoProtocolRoutes` + `OsuWebRoutes` + `BeatmapAssetRoutes` + the bancho/osu-web/b. groups from `BanchoHostGroups` (D7 — done as a **three-way split**, not a whole move: composition-root half → `Basil.Host` as `HostGroups`, pure-I/O half → new Infrastructure `BeatmapsetAssetBuilder`) + `AnnouncementNotifier` (D8, with the `IAnnouncementNotifier` Application contract and the `AnnounceRoutes` edit). `BanchoIrcBridgeConnection` **stays in A** (see Irc row above — plan deviation, not a move). `Basil.Host.Bancho.Tests` (46 packet tests + dispatcher + `MatchCreationDataMapperTests`). Full suite. Two commits: handlers, then routes. | ~62 + 47 tests | yes, except D7/D8 | **L** (count, not difficulty) |
| **12** | **`Basil.Host.Api`**: every remaining route/endpoint/view (Auth 2, Beatmaps 3, Content 8, Diagnostics 1, Multiplayer 16, Scores 2, Spectating 1, Users 3), `Shared/Http/**` remainder (24), `SseEndpoints`, `Shared/Media/Assets/*` (8), `AdminKeyAuthenticationHandler`; api./assets./a. groups (D7); the Api-side `*Setup` files stay in `Basil.Host` and now reference Host.Api. `Basil.Infrastructure.csproj` drops `Sdk.Web` and the ASP.NET packages. `Basil.Host.Api.Tests`. `HostBoundaryTests` lands here. Endpoint map must still read 151. | ~70 + tests | yes, except D7 | **L** |
| **13** | **Close**: architecture tests pass 2 (§7 complete); `CLAUDE.md` Architecture section, `docs/for-developers/architecture.md` rewrite (old Task H3), `HANDOVER.md`; full suite, Release, endpoint map, `Basil.LoadTests` publish smoke (`dotnet publish src/Basil.Host`). | docs + tests | by hand | **M** |

Fifteen commits. Batches 0, 11, 12 are the wide ones; 4 (pinned list), 11 (D7, D8) and 12 (D7)
are where a line changes meaning; everything else is `git mv` + namespaces.

## 6. Dependencies that break on move, and the fix

Measured on the current tree (every Application candidate grepped for every type defined in a
technology-bound file):

| Blocker | Used by | Fix |
|---|---|---|
| `BasilJsonOptions`, `JsonMergePatch` (Shared/Http) | `LoginService`, `ChannelMembershipService`, `StatusPublishLogoutHandler`, `StateStream` | → A/Shared/Json (Batch 2) |
| `LoginResponseEncoder`, `PacketBuilders` | `LoginService` | → A/Auth (Batch 4, U3) |
| `IMatchRoundEndOutbox`, `RoundEndWrite`, `MatchRoundEndOutboxFullException` | `MatchLifecycle`, `AbortHandler` | file split (Batch 2) |
| `MultiplayerMetrics` static | `InstrumentedMatchLock` | file split (Batch 2) |
| `MatchChatMessage` | `ChannelMembershipService` | goes to A, not HA (D5) |
| `RuntimeMeterListener`, `DurationAggregateSnapshot` | Diagnostics snapshots | whole slice is I — not a blocker |
| `InviteResult` | `MpCommandService` | false positive (`MatchControlService.InviteResult`) |
| `ServerPacketWriter` in `AnnounceRoutes` | resolved: `IAnnouncementNotifier` (A) / `AnnouncementNotifier` (HB) | D8 done, Batch 11 |
| `ILogger<Bootstrap>` in `BanchoProtocolRoutes` | Infrastructure → Host cycle | Batch 0 edit |

`SliceAdjacency`'s 44 rows and the `Shared -> Features` pinned list (10) are re-homed per
project in §7; rows that stop resolving after a move are deleted, and those deletions are the
measurable win (same rule as C5).

## 7. Verification and the instruments after

Per batch: build 0 errors; `Basil.ArchitectureTests`, `Basil.Domain.Tests`, `Basil.Protocol.Tests`
and whichever `*.Tests` exist at that point — total test count must equal today's 1709 plus tests
added by §7, never fewer. Full `Basil.IntegrationTests` (363) at the milestones in §5. Route table
via `get_endpoint_map` (151 endpoints, full patterns) — not the grep baseline, which cannot see a
group prefix move.

`Basil.ArchitectureTests` after Batch 13:

| Rule | Instrument |
|---|---|
| Domain and Protocol reference nothing in the solution; Domain references no framework | `DependencyDirectionTests` (exists; extend the forbidden list) |
| Application references no Infrastructure and no host | compiler; one NetArchTest line for the record |
| Application types touching `Basil.Protocol.*` are exactly the pinned list | `TransportSeamTests`, re-scoped in Batch 4 |
| **No host references another host; `Basil.Host.Api` has no dependency on `Basil.Protocol.Bancho`** | new `HostBoundaryTests`: three `NotHaveDependencyOnAny` checks from each host assembly + the Api/Protocol check. Prove it fails by adding a reference, then remove it (C6's rule). |
| Slice adjacency inside Application, Infrastructure, and each host | `SliceAdjacency` split into per-project allowlists seeded from today's 44 rows, pruned to rows that resolve |
| `Shared` never references a feature, per project | `SliceBoundaryTests` pinned list split the same way |
| Domain internal adjacency | `DomainAdjacency` 12 rows (down from 14: `9be67c32` folded `Login` into `Auth`/`Users`/`Client`, dropping 2 edges) |
| Transport adapter size (old E2's "under ~60 lines or listed") | dropped — v3 does not ask for it |

## 8. Risks

1. **Batch 0 is the csproj split** the D2 investigation measured: `Sdk.Web`, `SelfContained`,
   icon/manifest, publish-cleanup target, and content items decide what is Host and what is
   library. Locale fragments and the two embedded avatars go with their slices; `appsettings.json`,
   `docs-site`, dev cert, default banner, icon go to `Basil.Host`. `LocaleCatalogTests` and the 40
   `WebApplicationFactory<Bootstrap>` tests catch a wrong content root.
2. **Rider *Adjust Namespaces* over ~300 files** (Batch 1). Per folder, build between folders,
   one commit.
3. **Slice cycle** `Chat ↔ Bot ↔ Multiplayer` can force Batches 5–8 to merge — planned.
4. **Batches 11–12 count**: ~130 files + ~50 test files; large but each file is a plain move.
5. **`Basil.Host.Api` seeing `Basil.Protocol.Bancho` transitively** through Application: the
   `HostBoundaryTests` check is the only thing stopping a future `using Basil.Protocol.Packets;` in a
   route. It lands in Batch 12, not 13.
6. **Beatmaps background services** keep I/O-shaped use cases (D4). Recorded, not fixed.
7. **`OpenApiDocumentEndpointTests`**: schema ids are type names, not namespaces; a diff there is a
   finding, not a fix.
8. **Test-count accounting** across six new test projects: keep a running table in
   `HANDOVER.md` from Batch 2 on.

## 9. What the old plan loses, and what carries over

* `basil-plan-20260909.md` **Stage D2–D4**: superseded by Batches 10–12 (same idea, now on top of
  the Application split, names `Basil.Host.*` not `Basil.Hosts.*`). **D1** done and kept. **E1**
  (the three surviving relationships) → rows in the split `SliceAdjacency` lists. **E2** → §7.
  **G** done. **H** (ADRs, localization doc, `architecture.md`, full verification) → Batch 13 and
  after.
* `architecture-target-20260908.md` §3: project list replaced by §0.1 here (adds Application,
  Protocol split already real); its invariants survive.
* `plans/execution/stage-d-progress.md` §D2: the composition-root finding is now D1/Batch 0 of this
  plan.
* Task C2 (`GameSession` split): still off the path (U5).

## 10. Deliberately not done

* No `GameSession`/`UserSession` split (U5). No new abstraction except D8.
* No feature renames, including Domain's `Channels`/`Login` vs `Chat`/`Auth`.
* No changes to `Basil.Protocol.*`, `Basil.LoadTests` beyond the two literals, package versions,
  `Directory.Build.props`, `Directory.Packages.props`.
* No splitting of the Content filesystem services or the Beatmaps ingestion pipeline.
* No `Services/`, `Repositories/`, `Handlers/`, `Managers/`, `Helpers/` folders.
* No pushes during execution; local commits only, until the user says.

## 11. Open questions

None blocking. Everything the user needed to decide is in §0.2; the plan's defaults cover the
rest and are marked where the compiler or a test decides (Application's package list in Batch 2,
the pruned adjacency rows in Batches 9 and 13).
