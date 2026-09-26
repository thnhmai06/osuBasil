# Handover — osuBasil architecture migration

**Written 2026-09-11, re-verified and updated 2026-09-15 (C1b's completion).** For a successor agent
with no prior context. Read this first, then `plans/README.md` for what every other document is,
then `plans/basil-plan-20260909.md`. Everything here is verifiable from the repository; where it is
not, it says so.

> **2026-09-16 — direction change, execution underway.** The target is *Architecture v3*:
> `Basil.Domain` + `Basil.Server` reorganise into `Basil.Domain`, `Basil.Application`,
> `Basil.Infrastructure`, then the transports split into `Basil.Host.Bancho`/`.Irc`/`.Api` with
> `Basil.Host` as the entry point — feature folders kept throughout. The plan is
> `plans/architecture-v3-migration-plan-20260916.md`, approved by the user; execution started
> 2026-09-16. **Batch 0 done** (`9244a6e3`, local, not pushed): `Basil.Host` extracted as the exe,
> `Basil.Server` is now a library. Next: **Batch 1** — rename `Basil.Server` → `Basil.Infrastructure`.
> Stage D2–D4 and E2 below are superseded by the v3 plan's Batches 10–13; D1 and G are done and
> stand; §3–§9 of this handover about instruments, rules and pitfalls still applies except where a
> batch has since moved the file a rule names. Every batch commit is local only — not pushed until
> the user says so. **Batch 1 done** (`Basil.Server` renamed to `Basil.Infrastructure`, `Features.`
> segment dropped, `Basil.Server.Host` → `Basil.Host`, `Basil.Server.Tests` → `Basil.Infrastructure.Tests`).
> **Batch 2 done**: `Basil.Application` created (Sessions, Eventing, Configuration, Localization,
> Json, Irc's `IIrcConnection`/`IrcSession`/`IrcSessionRegistry`, Multiplayer core, the D10
> reply-constant classes and their locale fragments). The compiler forced additions the plan's file
> list didn't name, each a framework-free leaf with a real caller in an already-moved type:
> `MatchCreationData.cs`, `MatchLiveSnapshotBuilder.cs` (+`UserBriefResolver.cs`), `BeatmapViews.cs`
> (pulled forward from Batch 9 — **Batch 9's row in §5 no longer reads "1 file + tests"**, it's
> DirectSearchService plus DI splits only), `DateTimeExtensions.cs`, `Envelope.cs`/`PageMeta`/
> `FieldError`, `BanchoIrcBridgeConnection.cs`. `ChatMetrics.cs`/`IrcMetrics.cs` turned out to be
> plain renames to `*Publisher.cs`, not D3 splits (only `MatchRoundEndOutbox.cs` and
> `MultiplayerMetrics.cs` had a real contract half to extract). `BotBootstrapService.BotId` calls in
> `MatchLiveSnapshotBuilder.BuildSettings` became `MatchSession.NoHostId` (same value, already used
> elsewhere in the file, removes an Infrastructure dependency). Test suite: 9 + 235 + 158 +
> 65 (`Basil.Application.Tests`, new) + 851 (`Basil.Infrastructure.Tests`, down from 916 — the 65
> moved out) + 28 (`Basil.Host.Tests`) + 363 (`Basil.IntegrationTests`) = **1709**, unchanged from
> Batch 1. `SliceBoundaryTests`'s pinned `Shared_Should_Not_Reference_Features` list shrank by 4
> (`GameSession`, `UserSession`, `PacketDispatcher`, `GhostDisconnectService` — each now depends on
> `Basil.Application.*` instead of a Features slice) — the list working as designed, not edited
> freely. `dotnet run` smoke check could not run in this session: `ServerOptions.Port` is 443, which
> needs admin/a URL ACL reservation this background session doesn't have; `Basil.IntegrationTests`
> boots the same `Bootstrap`/`StartupData`/DI graph through `WebApplicationFactory` (DbUp migrations,
> `LocaleTouch.AllReplyHolders()` included) and is the stronger signal here. Two follow-up requests
> from the user during this batch were **not** part of it and got their own commits, done before
> Batch 3 per the user's explicit choice: **`f4be2c25`** made `MatchSession.HostId` nullable instead
> of `MatchRoomState.NoHostId`/`SystemUserIds.BasilBot` as a sentinel (wire behavior unchanged:
> `MatchPacketDataMapper` writes `HostId ?? SystemUserIds.BasilBot`); **`71c70431`** did the same for
> `MatchSession.MapMd5` (was `""` for "no map", now `null`; wire mapping is `MapMd5 ?? ""` in the
> same mapper, mirroring `MapId`'s existing `?? -1`). Round/score beatmap md5s and a player's own
> `UserStatus.MapMd5` are distinct, always-populated fields, untouched. Test suite unchanged at
> 1709 (one known-flaky SSE test, see §1, unrelated).
> **Batch 3 done**: the 5 services (`AdminKeyService`, `CredentialVerifier`, `MirrorService`
> +`MirrorOptions`, `MotdService`, `ReplayService`), the 2 event pairs (`I`/`PlayerInputEvents`,
> `I`/`PlayerStatusEvents`), and the 21 ports named in §1.1 moved `Basil.Domain` → `Basil.Application`
> (one more than the plan's "20" — the measured count). `IChannelRegistry`/`InMemoryChannelRegistry`
> stayed in Domain as planned. `Basil.Domain.Bot` and `Basil.Domain.Spectating` are now empty
> (every file they held moved out); every `using Basil.Domain.Bot;`/`Basil.Domain.Spectating;` became
> `Basil.Application.*` outright rather than an addition, since nothing else in those namespaces
> remained to keep. Every other touched namespace (Auth, Beatmaps, Channels, Content, Multiplayer,
> Scores, Social, Users) kept its Domain entities in place and gained an added `Basil.Application.*`
> using alongside — Domain still owns `Beatmap`, `Channel`, `MenuBanner`, `Match`/`Round`,
> `Relationship`, `User`, `Mods`, etc. `DependencyDirectionTests.Domain_Should_Not_HaveDependencyOn_Server`
> now also asserts `Basil.Application` — passes, confirming no Domain type reaches back into it. The 5
> Domain tests named in §1.1 moved to `Basil.Application.Tests/{Auth,Beatmaps,Scores,Spectating}/`.
> Test suite: 9 + 207 (`Basil.Domain.Tests`, down from 235) + 158 + 93 (`Basil.Application.Tests`, up
> from 65) + 851 + 28 + 363 = **1709**, unchanged. Full `Basil.IntegrationTests` 362/363 (the same
> known-flaky SSE test, passes alone). Release build succeeds.
> **Batch 4 done**: moved `AuthenticationService.cs` (Infrastructure.Auth → Application.Auth),
> `LoginResponseEncoder.cs` and `PacketBuilders.cs` (Infrastructure.Shared.Http.Bancho → Application.Auth),
> and `PlayerStatusView.cs` (Infrastructure.Spectating → Application.Spectating). `LoginService.cs`
> and `ClientIntegrityService.cs` were scoped in the plan but deliberately deferred: both depend on
> Infrastructure types not yet moved (BotBootstrapService, SpectatorService, MatchBroadcast, IChatNotifier
> — batches 5-8), and `LoginService` also needs a not-yet-created Application contract for `MenuIconService`
> (filesystem-permanent in Infrastructure). `TransportSeamTests` unchanged for Infrastructure (still pins
> 3 offenders), re-scoped with a new Application test pinning `LoginResponseEncoder`/`PacketBuilders`
> plus the IRC seam types (`BanchoIrcBridgeConnection`, `IIrcConnection`) — total test count
> 1709 → **1710** (+1). Per-project: Basil.ArchitectureTests 10 (was 9), Domain 207, Protocol 158,
> Application 93, Infrastructure 851, Host 28, Integration 363.
> **Batch 5 done**: moved 14 files to `Basil.Application` — Chat (6: `ChannelMembershipService`,
> `ChatDispatchService`, `ChatLine`, `IChannelNotifier`, `IChatNotifier`, `ChannelPartLogoutHandler`),
> Bot (2: `BotBootstrapService`, `ICommandDispatcher`), Irc (5: `IrcAuthenticationService`,
> `IrcQueryService`, `IrcNamesReply`, `IrcLoginOutcome`, `IrcSessionRemovalLogoutHandler`), plus
> `MatchChatMessage.cs` (Multiplayer, pulled forward from Batch 8 — a pure DTO record with no other
> Infrastructure dependency, needed by `ChannelMembershipService.PublishMatchChat`). `CommandDispatcher.cs`
> (the concrete `Basil.Infrastructure.Bot` implementation of `ICommandDispatcher`, moved separately from
> its interface) was scoped in the plan but deliberately deferred to Batch 8: it depends on
> `IMpCommandService` (Multiplayer) and `FaqService` (Content, filesystem-permanent in Infrastructure like
> `MenuIconService`). `ClientIntegrityService.cs` (deferred already in Batch 4) still depends on
> `MatchBroadcast`/Multiplayer — unchanged, still Batch 8. DI split for Chat/Bot/Irc: new
> `AddChatApplication()`/`AddBotApplication()`/`AddIrcApplication()` extension methods in
> `Basil.Application`, called from the still-Infrastructure `AddChat()`/`AddBot()`/`AddIrc()`, which now
> register only concrete Infrastructure-only types (`ChatNotifier`/`ChannelNotifier`, packet handlers,
> `SqliteChannelRepository`, `ChatMetricsPublisher`, `CommandDispatcher`, `TcpIrcListener`,
> `IrcMetricsPublisher`). `TransportSeamTests.Application_Types_Should_Not_Reference_Protocol` (added in
> Batch 4) gained 4 more pinned offenders once real code (not just moved-file scaffolding) reached the
> namespace: `IrcAuthenticationService`, `IrcLoginOutcome`, `IrcNamesReply`, `IrcQueryService` — each
> builds or carries `Basil.Protocol.Irc.IrcMessage`, an IRC-side seam already covered by the existing
> `BanchoIrcBridgeConnection`/`IIrcConnection` pins (U3). Test count unchanged at **1710** (pure code
> movement, no new/removed test cases). Per-project: ArchitectureTests 10, Domain 207, Protocol 158,
> Application 93, Infrastructure 851, Host 28, Integration 363 (not run this batch — not a milestone;
> `Basil.Infrastructure.Tests` already covers `LoginServiceTests`/`ClientIntegrityServiceTests` etc.).
> **Batch 6 done**: moved 5 files to `Basil.Application/Spectating` — `ISpectatorNotifier`,
> `SpectateEvents.cs` (`SpectateEvent`/`SpectateFramesEvent`/`SpectateState`/`SpectateStateEvent`),
> `SpectatorService`, `SpectatorTeardownLogoutHandler`, `StatusPublishLogoutHandler`. `PlayerLiveRoutes.cs`
> and `Packets/*` (incl. `BanchoSpectatorNotifier`) stay in Infrastructure (destined `HB`/`HA` at
> Batches 11/12). DI split: new `AddSpectatingApplication()` in `Basil.Application.Spectating`
> (registers `SpectatorService`, `IPlayerInputEvents`/`IPlayerStatusEvents` — already Application since
> Batch 3 — and the two logout handlers), called from the still-Infrastructure `AddSpectating()`, which
> now registers only `ISpectatorNotifier`/`BanchoSpectatorNotifier` and the 4 packet handlers.
> `SpectateFramesEvent` carries the wire-level `ReplayFrame`/`ScoreFrame` types directly, so it moved
> from `TransportSeamTests`' Infrastructure-side pinned list (`Business_And_Api_Types_Should_Not_Reference_Protocol`,
> now down to 2 offenders) to the Application-side one (`Application_Types_Should_Not_Reference_Protocol`,
> now 9). Unrelated but exposed by the same move: `SliceBoundaryTests.Shared_Should_Not_Reference_Features`'s
> pinned list shrank by 1 — `OpenApiExampleExtensions` (`Shared/Http/OpenApi`) built its `/spec/{id}` SSE
> OpenAPI examples from `SpectateFramesEvent`, which is no longer a Features-slice reference now that the
> type lives in Application; its now-dead `using Basil.Infrastructure.Spectating;` was removed. Test
> count unchanged at **1710** (pure code movement). Per-project: ArchitectureTests 10, Domain 207,
> Protocol 158, Application 93, Infrastructure 851, Host 28, Integration 363 (not run this batch — not a
> milestone).
> **Batch 7 done**: moved 3 files to `Basil.Application/Scores` — `ScoreSubmissionService`,
> `ScoreSubmissionResponseBuilder`, `ScoreSubmissionChartsFormatter`. All were already clean (their
> `Basil.Infrastructure.Auth`/`.Multiplayer` usings were stale leftovers from earlier batches — the real
> symbols they needed, `AuthenticationService` and `MatchSession`, had already moved to Application in
> Batches 2/4). `RijndaelScoreDecryptor`, `SqliteLeaderboardStore`, `SqliteScoreRepository`, `ScoreRoutes`,
> `ScoreDetailView`, `ReplayService`'s DI registration, `FileSystemReplayStorage` stay in Infrastructure.
> DI split: new `AddScoresApplication()` in `Basil.Application.Scores` (registers only
> `ScoreSubmissionService`), called from the still-Infrastructure `AddScores()`. Test count unchanged at
> **1710**. Per-project: ArchitectureTests 10, Domain 207, Protocol 158, Application 93, Infrastructure
> 851, Host 28, Integration 363 (not run this batch).
> **Batch 8 done**: moved 20 files to `Basil.Application` — the Multiplayer "rest": `MatchControlService`,
> `MatchLifecycle`, `MatchMembership`, `MatchBroadcast`, `MatchRecoveryService`, `MatchReportService`,
> `MpCommandService`/`IMpCommandService`, `IMatchNotifier`, `MatchLeaveLogoutHandler`,
> `Handlers/{Countdown,Lifecycle,Slots}/*` (8 files); plus the two Batch-4/5-deferred files, now
> unblocked: `ClientIntegrityService.cs` (Auth — its last blocker, `MatchBroadcast`, moved this batch)
> and `CommandDispatcher.cs` (Bot — its `IMpCommandService` blocker moved this batch; its `FaqService`
> blocker was permanent-Infrastructure, resolved below). `BanchoMatchNotifier`, `SqliteMatchRepository`,
> all 26 `Packets/*` handlers, `MatchRoundEndOutbox` (worker half), `MultiplayerMetricsPublisher`,
> `Endpoints/*`, `MatchLiveRoutes`/`MatchRoutes`/`MatchSubResourceRoutes` stay in Infrastructure.
> `CommandDispatcherTests.cs` was **not** moved to `Basil.Application.Tests` despite `CommandDispatcher`
> itself moving — it depends on `MultiplayerTestSupport` (`Basil.Infrastructure.Tests`, `internal`), a
> fixture builder shared by ~30 other Infrastructure-side packet-handler tests that legitimately stay
> put; moving the test would have meant either duplicating that fixture builder or an illegal reverse
> project reference. This matches the precedent already set by `AuthenticationServiceTests.cs` (Batch 4)
> and `ScoreSubmissionServiceTests.cs` (Batch 7): a moved production type's test stays in
> `Basil.Infrastructure.Tests` by default — Infrastructure.Tests may freely reference Application types,
> so nothing here is an architecture violation. Only Batch 3's original 5 test moves were the deliberate
> exception, made when the test's own dependencies were entirely clean and self-contained.
>
> **The `FaqService` gap, resolved**: `CommandDispatcher` directly `new`'d a concrete
> `Basil.Infrastructure.Content.FaqService` (filesystem-permanent per plan §1.2, same shape as
> `LoginService`'s still-open `MenuIconService` gap from Batch 4) — worse than injecting it, since a
> direct `new` cannot be swapped for a contract at the DI layer alone. Fixed the same way §4's D8 extends
> to this case: added `IFaqStore` (`ListEntries`/`ReadEntryAsync`) to `Basil.Application.Content`,
> `FaqService : IFaqStore` in Infrastructure, registered `services.AddSingleton<IFaqStore>(sp =>
> sp.GetRequiredService<FaqService>())` in `ContentServiceCollectionExtensions.AddContent`, and changed
> `CommandDispatcher`'s constructor to take `IFaqStore faq` instead of `IOptions<StorageOptions>`
> (dropping the direct `new`). **`LoginService.cs`'s `MenuIconService` gap is now the only thing like
> this left unresolved** — it needs the identical `IMenuIconStore` treatment before it can move; not done
> here since `LoginService` is Auth, not Multiplayer, and out of this batch's scope.
>
> DI split: new `AddMultiplayerApplication()` in `Basil.Application.Multiplayer` (registers every
> Application-only type above, plus `IMatchRegistry`/`InMemoryMatchRegistry` which had stayed registered
> in Infrastructure since Batch 2 despite both halves being Application already), called from the
> still-Infrastructure `AddMultiplayer()`, which now registers only `IMatchNotifier`/`BanchoMatchNotifier`,
> `IMatchRepository`/`SqliteMatchRepository`, the 26 packet handlers, the round-end-outbox hosted
> service, and the metrics publisher.
>
> Test count unchanged at **1710**. Per-project: ArchitectureTests 10, Domain 207, Protocol 158,
> Application 93, Infrastructure 851, Host 28, Integration 363 (not run this batch — not a milestone;
> deferred to Batch 9, immediately next, which requires it anyway).
> **Batch 9 done — Milestone: v3 without hosts.** Moved `DirectSearchService.cs` (Beatmaps) to
> `Basil.Application` — clean, no blockers. Finished the DI split for the remaining slices with an
> Application-only piece: `Beatmaps` (`AddBeatmapsApplication`: `DirectSearchService`, `MirrorService`
> — the latter had stayed registered from Infrastructure since Batch 3 despite being Application
> already), `Auth` (`AddAuthApplication`: `CredentialVerifier`, `AuthenticationService`,
> `AdminKeyService`, `ClientIntegrityService` — `LoginService` stays registered from Infrastructure,
> since the class itself hasn't moved yet, still blocked on the open `MenuIconService` gap), `Content`
> (`AddContentApplication`: `MotdService` only — everything else in Content is filesystem-permanent or
> a concrete repository). `Diagnostics` and `Users` need no split ("no Application part" / "nothing →
> A" per plan §1.2) — confirmed by inspection, no registrations there are Application-only. All 11
> `*ServiceCollectionExtensions.cs` named in plan §4 are now split.
>
> Architecture tests pass 1 (§7's "Application references no Infrastructure and no host" row): added
> `DependencyDirectionTests.Application_Should_Not_HaveDependencyOn_InfrastructureOrHost`, a NetArchTest
> assertion over the `Basil.Application` assembly — the compiler already enforced this (no
> `ProjectReference` from Application to Infrastructure or any Host), this just makes the invariant
> visible and pinned the same way Domain's isolation already is. +1 test, **1710 → 1711**. The rest of
> §7's table (`HostBoundaryTests`, per-project `SliceAdjacency`/`SliceBoundaryTests` splits) needs the
> host projects that don't exist until Batches 10-12, so it's explicitly out of scope for this pass.
>
> `plans/`+`docs/` pass 1: `CLAUDE.md`'s Architecture section's migration note was actively wrong —
> it described the *pre-v3* merge (`Basil.Application`/`Basil.Infrastructure`/`Basil.Web` "merged into
> `Basil.Server` and gone"), which v3 has since reversed twice over. Rewrote the note to state the
> actual current status (Batches 0-9 done, what moved where) and point to `HANDOVER.md` + the
> architecture tests, without touching the stale diagram/prose above it — that full rewrite is
> explicitly Batch 13's job, not this one.
>
> Full `Basil.IntegrationTests`: **363/363**, no failures this run (the known-flaky SSE tests passed
> cleanly; a `MenuIconManagementEndpointTests` class-cleanup-failure line appeared in the log but did
> not affect the pass/fail count). Release build: 0 errors. `get_endpoint_map` not checked — not in
> this batch's milestone list (only 0, 12, 13 per §5).
>
> Per-project: ArchitectureTests 11 (was 10), Domain 207, Protocol 158, Application 93, Infrastructure
> 851, Host 28, Integration 363. **Total 1711.**
>
> **Batch 10 done**: created `Basil.Host.Irc` (`Microsoft.NET.Sdk`, `Hosting.Abstractions`, refs
> Application/Infrastructure/Protocol.Irc — exactly the project table in plan §2) and moved
> `TcpIrcListener.cs`, `TcpIrcConnection.cs`, `IrcMetricsPublisher.cs` into it — all three were already
> clean (only Application/Domain/Protocol.Irc dependencies once the stale `Basil.Infrastructure.Shared`
> duplicate using in `IrcMetricsPublisher.cs` was dropped). Added `IrcHostServiceCollectionExtensions.AddIrcHost()`
> registering the two hosted services (`TcpIrcListener`, `IrcMetricsPublisher`). Deleted the now-empty
> `Basil.Infrastructure/Irc/IrcServiceCollectionExtensions.cs` (after the move it only forwarded to
> `AddIrcApplication`, which `SliceRegistration.AddAll` now calls directly, followed by `AddIrcHost()`).
> `Basil.Host.csproj` gained a `ProjectReference` to `Basil.Host.Irc`; `Basil.slnx` gained both new
> projects under a new `/Sources/Host/` folder and `/Tests/`.
>
> Created `Basil.Host.Irc.Tests` (mirrors the other test projects' csproj shape) and moved
> `TcpIrcConnectionTests.cs` (plan-named) plus `IrcMetricsPublisherTests.cs` (not named in the plan, but
> moved out of necessity — `Basil.Infrastructure.Tests` has no reference to `Basil.Host.Irc` and never
> should, so keeping a test of a type that no longer lives reachably from Infrastructure.Tests wasn't an
> option, unlike the Batch 4/7/9 precedent where Infrastructure.Tests could still reach the moved
> Application type). `Basil.Host.Tests.CompositionRootTests` was calling the now-deleted `AddIrc()`;
> switched it to `AddIrcApplication()` (it doesn't need the TCP listener hosted service to resolve
> `IrcQueryService` etc., matching how it never called `AddIrcHost()` either).
>
> Test count unchanged at **1711** — 5 tests moved from `Basil.Infrastructure.Tests` (846, was 851) to
> the new `Basil.Host.Irc.Tests` (5, new). Per-project: ArchitectureTests 11, Domain 207, Protocol 158,
> Application 93, Infrastructure 846, Host 28, Host.Irc 5, Integration 363 (not run this batch — not a
> milestone).
> **Two naming-cleanup commits landed between Batch 10 and Batch 11**, per the user's standing
> instruction to fix naming/terminology on sight while the migration continues, never pausing the
> batch sequence for it: **`2a8b3c40`** verified and committed renames already staged on disk —
> `BeatmapsetSearchFilters`/`BeatmapsetSearchQueryParser` → `BeatmapFilters`/`BeatmapFilters.Parser`,
> `BeatmapObjectCounts`/`Osu-`/`Taiko-`/`Catch-`/`ManiaObjectCounts` → `BeatmapObjects`/`OsuObjects`/
> etc., `UserSearchFilters`/`UserSearchQueryParser` → `UserFilters`/`UserFilters.Parser`,
> `Submission.FromSubmission` → `Submission.From`, plus a real pre-existing bug found while
> verifying it: `SqliteBeatmapRepository`'s SQL used the renamed column `Objects` but the schema
> migration and an anonymous-object property still said `ObjectCounts` — fixed with a new migration
> `007_beatmaps_objectcounts_rename.sql` (never edit a shipped migration) and matching property
> renames. **`9be67c32`** split `Basil.Domain.Login` (a stray namespace with no slice) three ways:
> the audit record `Login` → `LoginEvent`, merged into `Auth`; `Country` → `Users`, the slice it
> actually describes; `ClientDetails`/`Geolocation`/`OsuVersion` → a new `Client` namespace (genuinely
> shared across Auth/Scores/beyond, not owned by either). Also renamed `Basil.Protocol.Bancho`'s
> `MatchState` → `MatchStatePacket` to match its sibling `MatchPacket`/`MatchSlotPacket`'s
> "-Packet" wire-type convention. `DomainAdjacency`'s edge list and rationale rewritten to match;
> `DomainBoundaryTests` needed no changes (fully reflection-based). Both commits re-verified with the
> full fast suite plus a full `Basil.IntegrationTests` run (363/363) as an extra safety margin, since
> renames this wide-reaching risk missing a reference the compiler can't catch (a Dapper column name,
> a migration).
>
> **Batch 11 done, two commits (`76f38e5a` handlers, then routes) — largest batch by file count.**
> Moved into the new `Basil.Host.Bancho` (`Microsoft.NET.Sdk` + `FrameworkReference` to
> `Microsoft.AspNetCore.App`, project-level `<Using>` items for the ASP.NET Core/DI/Logging/Hosting
> namespaces Sdk.Web would otherwise have given it implicitly): all 46 packet handlers, the 4
> packet-transport notifiers (`ChannelNotifier`/`ChatNotifier` unchanged;
> `BanchoMatchNotifier`/`BanchoSpectatorNotifier` renamed to `MatchNotifier`/`SpectatorNotifier` —
> the `Bancho` prefix was stutter once they live in a project already named `Basil.Host.Bancho`),
> `IPacketHandler`/`PacketDispatcher`, `MatchPacketDataMapper`, `LogoutBroadcastHandler`,
> `BanchoProtocolRoutes`, `OsuWebRoutes`, `BeatmapAssetRoutes`, and the new D8 pair
> `IAnnouncementNotifier` (`Basil.Application.Content`) / `AnnouncementNotifier`
> (`Basil.Host.Bancho.Content`).
>
> **Three deviations from the plan's file list, each forced by a real constraint found while
> executing, not a preference:**
> 1. **`BanchoIrcBridgeConnection` stays in `Basil.Application`**, not `Basil.Host.Bancho` as the
>    plan named it. `GameSession`'s constructor self-wires it as the default `IIrcConnection`
>    (`IrcConnection = new BanchoIrcBridgeConnection(this)`); moving the concrete type to a host
>    project would make `Basil.Application` depend on a host, which is exactly the direction this
>    whole migration exists to prevent. It stays pinned on
>    `TransportSeamTests.Application_Types_Should_Not_Reference_Protocol` for the same reason it
>    always was — its own doc comment now says so explicitly.
> 2. **`BanchoProtocolRoutes` moved in the *handlers* commit, not the routes commit**, despite the
>    plan's "two commits: handlers, then routes" implying otherwise. It's `PacketDispatcher`'s only
>    caller; splitting it into the second commit would have left the tree unbuildable between the two.
> 3. **`BanchoHostGroups.cs` split three ways**, not moved whole: `Create`/`HostNamesFor`/the
>    host-group record went to `Basil.Host` as `HostGroups` (renamed — the old name was actively
>    misleading once you notice it builds all six host groups, not just bancho's three; it's
>    composition-root code, `SliceRegistration.MapAll`'s exact job, so it was never going to move to
>    a bancho-specific host regardless). `BuildBeatmapsetArchiveAsync`/`BuildAudioPreviewAsync` and
>    their helpers extracted into a new `Basil.Infrastructure.Beatmaps.BeatmapsetAssetBuilder`
>    instead: both `Basil.Host.Bancho`'s `BeatmapAssetRoutes` (this batch) and the still-Infrastructure
>    `BeatmapsetAssetRoutes` (Batch 12's `api.`-host scope) call them, and neither host may depend on
>    the other — this is pure filesystem/ffmpeg I/O with no host-shaped concern in it, so it needed an
>    Infrastructure-owned home rather than living inside either host.
>
> **DI split, one level up from the Application/Infrastructure pattern**: unlike `AddXApplication()`
> (called *from* the outer layer's `AddX()`), `Basil.Infrastructure` cannot call into
> `Basil.Host.Bancho` — both `AddX()` and the new `Basil.Host.Bancho.AddBanchoHost()` are called
> side by side from `Basil.Host`'s composition root instead. `AddBanchoHost()` is one method, not
> one per slice (`AddIrcHost()`'s precedent, one batch old, is the same shape) — every slice's
> `AddX()` in Infrastructure dropped its packet-handler/notifier registrations down to this one
> place, since a per-slice split here would only have added five files for no isolation benefit.
>
> **Cross-project test references, all pinned on the same underlying fact**: most of
> `MultiplayerTestSupport.cs`'s ~30 consumers (real-notifier constructor wiring, not mocks — the
> whole point of the test) stay in `Basil.Infrastructure.Tests`, so the fixture itself stays there
> too and went `public` (was `internal`) rather than splitting or duplicating it. That forced
> `Basil.Infrastructure.Tests` → `Basil.Host.Bancho` (for the notifier concrete types) and
> `Basil.Host.Bancho.Tests` → `Basil.Infrastructure.Tests` (for the fixture) — verified empirically
> that xunit only discovers a project's own `[Fact]`s even across a `ProjectReference` to another
> test project, so no double-counting risk. `Basil.Host.Irc.Tests` needed the same `Basil.Host.Bancho`
> reference for the identical reason (`TcpIrcConnectionTests` wires a real cross-transport chat path).
> None of this is covered by `HostBoundaryTests` (production assemblies only) or violates any rule —
> rewriting a dozen tests' real-notifier wiring into mocks to dodge the edges would have been a
> behavioral change smuggled into a file-move batch, which the naming/quality directive explicitly
> asks to avoid.
>
> **Two new `InternalsVisibleTo` grants on `Basil.Application`** (`Basil.Host.Bancho`,
> `Basil.Host.Bancho.Tests`), mirroring the existing `Basil.Infrastructure` grant:
> `MatchScoreUpdateHandler` needs `MatchStreams.Score` and `MatchSession.AllocateScoreVersion`, both
> `internal` cross-cutting helpers already shared with Infrastructure the same way. Separately,
> `Basil.Infrastructure.Shared.Http.ContentTypes` went from `internal` to `public` — used by
> `BeatmapAssetRoutes`/`OsuWebRoutes` (now Host.Bancho) and still by `BeatmapsetAssetRoutes`/
> `AvatarRoutes`/`UserRoutes` (Infrastructure, Batch 12 scope); a small MIME-lookup helper with no
> reason to stay artificially internal once genuinely used cross-project.
>
> Architecture tests: `TransportSeamTests.Business_And_Api_Types_Should_Not_Reference_Protocol`'s
> pinned list is now **empty** — `MatchPacketDataMapper` moved out with the handlers it served, and
> `AnnounceRoutes` (D8) now sends through `IAnnouncementNotifier` instead of building a
> `ServerPacketWriter` packet itself. Its `BusinessAndApiTypes()` helper also dropped a
> `.DoNotResideInNamespaceContaining(".Packets")` filter that had gone dead: no `Basil.Infrastructure`
> namespace contains `.Packets` anymore, all of them moved this batch.
> `SliceBoundaryTests.Shared_Should_Not_Reference_Features`'s pinned list dropped `BanchoHostGroups`
> (split away, per above — neither half touches a Features slice).
>
> Full `Basil.IntegrationTests`: **362/363** — the one failure was
> `DiagnosticEndpointTests.GetGcLive_FirstEventIsARealGcReading` (`IOException: client aborted the
> request`, first-event-timing/connection flake, not a code defect — see §8's already-recorded
> investigation), under heavier memory pressure than usual on the run machine (two prior attempts
> this batch were killed outright before completing, by an out-of-memory condition unrelated to this
> change; a `dotnet build-server shutdown` and a third attempt got a clean run at 10m33s, longer than
> this suite's usual ~8m30s). Matches the test's own stated tolerance for a full run exactly.
> `get_endpoint_map`: **151**, matching plan §7's target exactly, routes correctly attributed to
> their new `Basil.Host.Bancho` files. Build 0 errors.
> Per-project: ArchitectureTests 11, Domain 207, Protocol 158, Application 93, Infrastructure 689
> (was 846; 157 moved out), Host.Bancho 157 (new), Host 28, Host.Irc 5, Integration 363. **Total
> 1711, unchanged** — pure code movement plus the D8 contract swap, no test added or removed.
>
> **Batch 12 done — the last host extraction, one commit.** Created `Basil.Host.Api`
> (`Microsoft.NET.Sdk` + `FrameworkReference` to `Microsoft.AspNetCore.App`, the same project-level
> `<Using>` items `Basil.Host.Bancho` needed, `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi`/
> `Scalar.AspNetCore`/`SixLabors.ImageSharp.Web`) and `Basil.Host.Api.Tests` (starts with zero
> tests: no route/endpoint file in this codebase has ever had a unit test outside
> `Basil.IntegrationTests`, confirmed by search before assuming otherwise). Moved every remaining
> route/endpoint/view (Auth 2, Beatmaps 2 — see the `BeatmapViews` deviation below, Content 8,
> Diagnostics 1, Multiplayer 16, Scores 2, Users 3), all of `Shared/Http/**`'s remainder (21 actual
> files, not the plan's approximate "24" — `ApiHostRoutes`, `AssetsHostRoutes`,
> `AbbreviationRedirectRoutes`, `Middleware/*` ×5, `OpenApi/*` ×8, `NumericIdRouteConstraint`,
> `Pagination`, `RouteDocs`, `HttpMetrics`), and `Shared/Media/Assets/*` (8). `AdminKeyRoutes` +
> `AdminKeyAuthenticationHandler` moved together; `Basil.Host/AuthSetup.cs`'s existing
> `AddAuthentication().AddScheme<...AdminKeyAuthenticationHandler>()` call — already living in
> `Basil.Host`, not Infrastructure, since before this batch — just switched its `using` to
> `Basil.Host.Api.Auth`. `Spectating`'s "1" (`PlayerLiveRoutes`) turned out to already be a plain
> `internal static class` helper called from `UserRoutes.HandleInput`, not its own route-group
> mapper, so it moved alongside `UserRoutes` with no separate wiring.
>
> **Three deviations from the plan's file list, found before moving anything (not discovered by a
> broken build) by checking each named file's current location first, per the advisor's own
> warning about D5's `PlayerStatusView`/`MatchChatMessage` trap:**
> 1. **`BeatmapViews.cs`** was already in `Basil.Application/Beatmaps` — pulled forward all the way
>    back in Batch 2, not Batch 12 as the plan's file list still implied. Nothing to move; Beatmaps'
>    real count this batch is 2 files (`BeatmapsetRoutes`, `BeatmapsetAssetRoutes`), not 3.
> 2. **`DateTimeExtensions.cs`** was already in `Basil.Application/Shared/Json` — Batch 2 again.
>    Nothing to move.
> 3. **`SseEndpoints.cs`/`SseSubscriberRegistry.cs`/`BoundedSseChannel.cs`** were already in
>    `Basil.Application/Shared/Eventing` — also Batch 2. `SseEndpoints` itself is `internal` and
>    builds `IResult`/reads `StatusCodes` (legitimate: `Basil.Application` already carries a
>    `FrameworkReference` to `Microsoft.AspNetCore.App` for exactly this kind of helper, same as the
>    already-accepted `HttpContext` usings). Every route file calling it
>    (`MatchLiveRoutes`/`MatchLiveStreamEndpoints`/`PlayerLiveRoutes`/`DiagnosticRoutes`/`UserRoutes`)
>    moved to `Basil.Host.Api` this batch, so `Basil.Application.csproj` gained
>    `InternalsVisibleTo Basil.Host.Api`/`.Tests`, mirroring the existing `Basil.Host.Bancho` grant.
>
> **A fourth, real one found mid-batch, not before it: `ContentTypes` cannot live in `Basil.Host.Api`
> at all.** The plan's `Shared/Http/**` table row puts it there, written before Batch 11 existed —
> but `Basil.Host.Bancho`'s `BeatmapAssetRoutes`/`OsuWebRoutes` (moved Batch 11) also call
> `ContentTypes.Resolve`, and a host may never depend on another host. Moved it to
> `Basil.Application.Shared.Http` instead (alongside the existing `Envelope`) — both hosts already
> depend on Application, `Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider` (its
> one framework dependency) is already available through Application's existing
> `FrameworkReference`, and no new package was needed. Same shape as `BeatmapsetAssetBuilder`
> (Batch 11): a type two mutually-forbidden hosts both need moves to the nearest layer both already
> depend on, not into either host.
>
> **DI split, following Batch 11's route-mapping precedent, not its `AddXHost()` one**: `Basil.Host.Api`
> needs zero new DI registrations of its own (confirmed by search — nothing in it calls
> `IServiceCollection`), so there is no `AddApiHost()`. What each of the six route-bearing slices'
> `*ServiceCollectionExtensions.cs` (Auth, Beatmaps, Content, Multiplayer, Scores, Users) *did* carry
> was a slice-level `MapXRoutes(RouteGroupBuilder)` aggregator alongside its `AddX()`, now split into
> a new `<Slice>RouteMapping.cs` file per slice in `Basil.Host.Api` (`AuthRouteMapping`,
> `BeatmapsRouteMapping`, `ContentRouteMapping`, `MultiplayerRouteMapping`, `ScoresRouteMapping`,
> `UsersRouteMapping`) — `AddX()` stays in Infrastructure, `MapXRoutes()` moves out, mirroring
> exactly how `AddXApplication()`/`AddX()` already split one layer up. Diagnostics needed no such
> split: its one route file (`DiagnosticRoutes`) was always its own `MapDiagnosticRoutes` aggregator
> with no slice-level wrapper. `Basil.Host/SliceRegistration.cs` now imports six `Basil.Host.Api.*`
> namespaces alongside the existing `Basil.Host.Bancho`/`Basil.Infrastructure` ones; its `MapAll`
> body is otherwise unchanged — same calls, same order, matching the endpoint map's unchanged 151.
>
> **Nine `internal` classes flipped to `public`** so `Basil.Host`/`Basil.ArchitectureTests` could
> reach them across the new project boundary, the same requirement Batch 11 hit for
> `BanchoProtocolRoutes`/`OsuWebRoutes`/`BeatmapAssetRoutes`: `AvatarRoutes`, `ApiHostRoutes`,
> `AssetsHostRoutes`, `DiagnosticRoutes`, `MenuAssetRoutes`, `BeatmapsetAssetRoutes` (all
> group-mapper classes `SliceRegistration.MapAll` calls directly), plus `EnvelopeSchemaTransformer`,
> `SecuritySchemeTransformers`, `SchemaTypeTransformers` (their `AddXTransformer()` C# 14 extension
> methods were already `public`, but effective accessibility caps at the containing class, which
> `Basil.Host/OpenApiSetup.cs` needs to call). `Basil.Infrastructure.csproj` gained
> `InternalsVisibleTo Basil.Host.Api`/`.Tests` for the diagnostics sampler/snapshot types
> (`DiagnosticStreams`, `HttpSampler.SampleAndResetDuration`, etc.) that `DiagnosticRoutes` still
> needs but that stayed `internal` on purpose (they're implementation detail, not a public contract).
>
> **`Basil.Infrastructure.csproj` dropped `Sdk.Web`** (`Microsoft.NET.Sdk.Web` → plain
> `Microsoft.NET.Sdk`) and the four ASP.NET-named packages
> (`Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi`/`Scalar.AspNetCore`/`SixLabors.ImageSharp.Web`,
> all moved to `Basil.Host.Api`) plus `Serilog.AspNetCore` (unused — no
> `UseSerilog`/`RequestLoggingOptions` call anywhere left in Infrastructure; its three sibling
> `Serilog.Sinks.*` packages were *also* already unused before this batch, so left alone as
> out-of-scope pre-existing dead weight, not something this move orphaned). Verified **zero** real
> ASP.NET framework usage remained first (`grep` for `IResult`/`RouteGroupBuilder`/`HttpContext`/
> `IEndpointRouteBuilder`/`Microsoft.AspNetCore` across `src/Basil.Infrastructure`; the one hit,
> `RuntimeMeterListener`'s "ASP.NET Core" mention, is a meter-name string, exactly as plan §1.2
> already said). No `FrameworkReference` needed either — Infrastructure genuinely has none of the
> HttpContext-adjacent usage the advisor flagged as a risk (`LoginService`'s
> `using Basil.Infrastructure.Shared.Http;` turned out to be a stale unused import, not real usage).
> Plain `Microsoft.NET.Sdk` doesn't provide `Sdk.Web`'s implicit usings, so the csproj gained
> explicit `<Using>` items for `Microsoft.Extensions.Configuration`/`.DependencyInjection`/
> `.Hosting`/`.Logging` and `System.Net.Http.Json` (`HttpMirrorSearchClient.ReadFromJsonAsync`),
> found by build-error iteration exactly like `Basil.Host.Bancho.csproj` needed in Batch 11.
>
> **New `HostBoundaryTests.cs`** (plan §7's row): four `NotHaveDependencyOnAny` checks — each host
> assembly has no dependency on the other two, plus `Basil.Host.Api` has no dependency on
> `Basil.Protocol` (a plain namespace-prefix check, not `"Basil.Protocol.Bancho"` as first written:
> `Basil.Protocol.Bancho`'s own root namespace is bare `Basil.Protocol`, no `.Bancho` suffix, so the
> project name and the namespace diverge — this also catches `Basil.Protocol.Irc`, which is correct,
> Host.Api has no business with either protocol). Proven to actually fail per the plan's explicit
> instruction (C6's rule): added a real `ProjectReference` from `Basil.Host.Api` to
> `Basil.Protocol.Bancho` plus a `using`, watched the check fail, then reverted both. That proof run
> surfaced a genuine finding, not a false positive: `OpenApiExampleExtensions.cs` legitimately
> references `Basil.Protocol.Multiplayer` for `ReplayFrame`/`ScoreFrame` — it builds its `input`-event
> OpenAPI example by serializing a real `SpectateFramesEvent` (the same Application-level type
> already pinned in `TransportSeamTests.Application_Types_Should_Not_Reference_Protocol` for
> carrying these exact wire types directly), so the documented example always matches the endpoint's
> real wire shape instead of a hand-written literal that could silently drift from it. Pinned as one
> named offender in a `knownOffenders` array (the same exact-equality style as every other pinned
> list in this codebase, not an exclusion filter — an offender's disappearance fails the test too, as
> a reminder to delete its row), with the parallel to `SpectateFramesEvent`'s own pin spelled out in
> the doc comment. `SliceBoundaryTests.Shared_Should_Not_Reference_Features`'s pinned list is now
> **empty** — all four remaining offenders (`SecuritySchemeTransformers`, and the three Media asset
> providers) left `Basil.Infrastructure` entirely this batch, so the check no longer sees them.
>
> Full `Basil.IntegrationTests`: **363/363**, clean pass, no flake this run (the usually-flaky GC
> SSE test passed cleanly too). Release build: 0 errors. `get_endpoint_map`: **151**, matching plan
> §7's target exactly, every route now correctly attributed to its `Basil.Host.Api`/`.Bancho` file.
> Per-project: ArchitectureTests 15 (was 11, +4 `HostBoundaryTests`), Domain 207, Protocol 158,
> Application 93, Infrastructure 689 (unchanged — no unit test existed for any moved route file),
> Host.Bancho 157, Host 28, Host.Irc 5, Host.Api.Tests 0 (new), Integration 363. **Total 1715** (was
> 1711, +4).
>
> Next: **Batch 13** — Close: architecture tests pass 2 (§7 complete); `CLAUDE.md` Architecture
> section and `docs/for-developers/architecture.md` full rewrite (old Task H3, both currently
> describe the pre-v3 five-project structure and say so); `HANDOVER.md` itself; full suite, Release,
> endpoint map, `Basil.LoadTests` publish smoke (`dotnet publish src/Basil.Host` — untested by any
> batch so far; `Basil.Host.csproj`'s `<SelfContained>true</SelfContained>` and its
> `RemoveUnusedOsuRulesetRuntimeFiles` post-publish target now also cover two new host assemblies
> flowing into the publish output, worth running before any doc edits so a publish failure isn't
> confused with a doc-rewrite mistake).

---

## 1. Where things stand

Branch **`feat/vsa-migration`**, at `0aaf326a`, pushed. A second worktree sits at
`V:\Code\cs\osuBasil-diagnostics` on `investigate/diagnostic-live-flake`, branched from
`9a4265ab`, for the flake diagnosis in §8. As of this checkpoint it is a clean build with no
uncommitted investigation output — the diagnosis has not run to completion yet; check `git status`
there before starting.

**The suite is green.** Verified 2026-09-15 at Unit 8's commit (`928dd1ac`), the last one that
touched a source file — Unit 9 changed only documentation:

| Project | Count |
|---|---:|
| `Basil.ArchitectureTests` | 9 |
| `Basil.Domain.Tests` | 235 |
| `Basil.Protocol.Tests` | 158 |
| `Basil.Server.Tests` | 944 |
| `Basil.IntegrationTests` | 363 |
| **Total** | **1709** |

(+1 in `Basil.ArchitectureTests` since D1: `DependencyDirectionTests`' single Protocol check split
into one per new assembly.)

One integration test, `DiagnosticEndpointTests.GetOverviewLive_FirstEventCarriesTheCuratedFields`,
failed once on a slow full run (8 min 02 s where 6 minutes is usual) and passed in isolation. It
waits ten seconds for a one-second broadcast tick; it is load-sensitive, not broken, and is listed in
§8 next to the other one.

Route table: **140** literal patterns from
`grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Server --include=*.cs | sort -u | wc -l`.
Treat that number with suspicion — see §4.

### Stages

| Stage | State |
|---|---|
| **A** — make constant-mediated coupling visible | Done. ADR-008. |
| **B** — untangle before anything moves | Done, all six tasks. |
| **F** — the Diagnostic API | Done and merged. |
| **C** — extract the business layer | C4, C3, C6, **C1a done** (pinned list 21 → 3), **C1b done** (nine units, per-feature table resolved), **C5 run and reported for both**. C2 **off the path**. |
| **D** — split the transports | **Running.** D1 done (`Basil.Protocol` split into `.Bancho`/`.Irc`); D2–D4 not started. See `plans/execution/stage-d-progress.md`. |
| **E** — declare what survives, enforce it | Not started. |
| **G** — the load harness | **Done.** Task G5 fixed (`e2931b33`); the `ReloginGuardWindowSeconds` duplication stays as accepted debt (see §8); the `DiagnosticEndpointTests` flake investigated, no defect found (see §8). |
| **H** — documentation and final verification | Not started. |

**Stage C runs C4 → C3 → C6 → C1a → C5.** `plans/execution/stage-c-order-decision.md` says why the
project boundary is crossed last; `c1-transport-seam-decision.md` says why C1 split.

---

## 2. C1a and C1b are both done. Stage D is next

**C1 as written cannot run.** Found 2026-09-14, measured from the compiled assembly: the services C1
would move into `Basil.Domain` — every match, chat, spectating and login service — encode bancho
packets with `ServerPacketWriter` and IRC lines with `IrcMessageWriter` inline, and take
`GameSession` (which carries `IIrcConnection` and `MatchSession`) as their parameter type. Domain may
not reference `Basil.Protocol` (invariant 9), so the first feature moved would fail on arrival.
The plan's sizing table checked five framework packages and never checked the protocol.
`plans/execution/c1-transport-seam-decision.md` has the per-type table.

**The instrument already exists.** `tests/Basil.ArchitectureTests/TransportSeamTests.cs` pins the
21 offending types by exact set equality, in the same shape as the `Shared -> Features` list. It
fails both when a new business type reaches for the protocol and when an entry is removed without
deleting its row — proven by deleting one row and watching it fail.

**C1a is done (2026-09-15).** All five steps landed: step 1 `SpectatorService` (`9a4265ab`,
21 → 20), step 2 the match services (`IMatchNotifier`, 20 → 17), step 3 the chat seam (`c152c026`,
`16b53d66`, `cd3edf84`, `087902df`, `666e704c`; `ChatLine`, `IChatNotifier`, `IChannelNotifier`,
NAMES/LIST moved to `IrcQueryService`; 17 → 10), step 4 `Auth.LoginService` (`0cea3160`; not a
notifier — a concrete, interface-free `LoginResponseEncoder` replaced 22 own-response encoder calls
1:1; 10 → 9), and step 5 (`c0e2f6e3`; `MatchCreationData` + `MatchCreationDataMapper` replace
`MatchState` as match-creation's input, `MatchLiveSnapshotBuilder.BuildPlayerScore` moved into its
only caller; 9 → 3). `plans/execution/phase-stage-c.md` "C1a" has every step's record,
`plans/execution/chat-seam-decision.md` the chat design.

**Pinned list: 3, all staying deliberately, not candidates for a future step:**

* `AnnounceRoutes` — needs Stage D's "Domain publishes, Hosts.Bancho encodes" event mechanism,
  which does not exist until Stage D builds the host split. Task D3's to close.
* `Spectating.SpectateFramesEvent` — a documented decision in the record's own remarks: it reuses
  the wire `ReplayFrame`/`ScoreFrame` types deliberately rather than duplicate an API-layer copy.
* `Multiplayer.MatchPacketDataMapper` — an adapter by design, outside `.Packets` only because C1b
  hasn't decided where adapters live yet.

**C5 has run (`32aaed40`) and its verdict is "ask the user," not "proceed."** Every slice-boundary
instrument — `SliceAdjacency` (44), `Shared -> Features` pinned list (12), `DomainAdjacency` (6),
the script's two counts (43 features-only / 50 solution-wide) — reads identical to `e286cc26`,
before C1a started. That is expected, not a failure: none of those four instruments has
`Basil.Protocol` in its population, and C1a never touched a slice boundary, only the protocol
dependency inside slices that already existed. The plan's C5 prediction (features-only ≈17) was
written for the original, unsplit C1 — the ~96-file move into `Basil.Domain` — which is now **C1b**
and has not run. Full writeup in `plans/execution/architecture-progress.md`, section "C5, run after
C1a".

**The user chose to run C1b.** It is in progress, moved in small verified units rather than all at
once — see `plans/execution/c1b-project-move-decision.md` for why (a text classifier missed both a
`GameSession`-typed parameter with no import naming it, and peer coupling between two candidate
files, and once nearly moved the whole Diagnostics slice, which the target architecture never
scoped as a Domain concern at all — reverted before anything committed).

**Units 1-8 are done and pushed** (`214ff845`, `365c351c`, `89cb120a`, `9b498f96`, `812b0c2d`,
`41e57269`, `5b9131c6`, `928dd1ac`): the fourteen repository/store interfaces plus their
filter/parser pairs (18 files), `MotdService`/`IReplayStorage`/`IScoreDecryptor`/`ReplayService` (5
files, first `Shared -> Features` pinned-list movement since C3), the mirror search
contract/`MirrorService`/`MirrorOptions` (5 files, `Basil.Domain`'s second package reference), the
password/token contracts plus `LoginForm`/`AdminKeyService` (6 files, second pinned-list movement),
Chat's `ChannelSession`/`IChannelRegistry`/`InMemoryChannelRegistry` plus Spectating's
`IPlayerInputEvents`/`IPlayerStatusEvents`/`PlayerInputEvents`/`PlayerStatusEvents` (10 files),
Bot's `ICommandReplySink` (1 file — the finding that Bot's other 5 files gate on Multiplayer, not a
separate survey), `AuthenticationService`'s `CredentialVerifier` extraction (1 new file, password
verification split from session lookup), and `MatchSession`'s phase-1 split: a new
`Basil.Domain.Multiplayer.MatchRoomState` carries every plain business field/method,
`Basil.Server`'s `MatchSession` keeps its full public surface and forwards to it, the lock/mutation-
scope/SSE machinery is untouched (2 files). Four blockers a text classifier cannot see are now
documented from direct experience — `GameSession`/`UserSession`/Shared-typed parameters with no
import naming them; peer coupling to a candidate that isn't itself eligible; direct filesystem I/O
with no forbidden `using`; an `IOptions<T>`/wrapped type argument that is itself a Server type, which
only a physical file move plus rebuild proves safe — plus a fifth found in Unit 8: a `const` field
crossing a namespace boundary is invisible to `DomainBoundaryTests` (ADR-008's documented
const-inlining gap), so a real edge must be declared even when the instrument cannot yet catch its
absence.

**Unit 9 closed C1b: `MpCommandService`/`MpReplies` measured and found blocked, the same as
`ScoreSubmissionService`.** `MpCommandService` writes `sender.MpScopeMatchId` directly at three sites
and calls `BeginMutationAsync` — live session/match mutation, not a lookup a Domain contract can
wrap. `MpReplies` reads through `Basil.Server.Shared.Localization.LocaleCatalog`, the same blocker
`BotReplies` hit in Unit 6. Both stay in `Basil.Server`, which resolves Bot's remaining five files
too (`CommandDispatcher`, `ICommandDispatcher`, `BotBootstrapService`, `BotReplies` were gated on this
pair). **Every C1b slice's Domain-eligible surface is now identified and, where safe, moved.**
Multiplayer landed at 2 files (`MatchRoomState`, `MatchSlot`) against the target table's estimate of
16; Bot at 1 (`ICommandReplySink`) against 6 — the third and fourth instance this session of that
table's per-feature counts over-stating what a text classifier's framework-import check can actually
verify, after Diagnostics (no row at all) and Bot's own first pass. Treat every number in that table
as an upper bound to verify per file, not a count to reach — this is the standing lesson for whoever
scopes Stage D next.

**C5, re-run after C1b (`architecture-progress.md`'s "C5, run after C1b" section has the full
table):** `SliceAdjacency` and the solution-wide script count are unchanged (44, 50) — expected, C1b
never touches `Features`-to-`Features` edges. `Shared -> Features` dropped 12 → 10.
`DomainAdjacency` roughly doubled, 6 → 14 — the instrument built to watch exactly what C1b moved,
showing the movement. The plan's features-only ≈17 prediction assumed C1's original scope including
the 24 Multiplayer packet handlers and the `!mp` command surface; those were never real C1b
candidates once measured directly, so that gap is Stage D's to close, not a miss here.

**Stage D — splitting `Basil.Server` into `Basil.Hosts.Bancho`/`Basil.Hosts.Irc`/`Basil.Hosts.Api`
(and renaming what remains to `Basil.Infrastructure`) — is the next stage of work**, per
`architecture-target-20260908.md` §8 step 8. It is a separate, larger undertaking outside C1b's
scope: it needs the host projects to exist before `GameSession`'s split (§8 step 5), the 24
Multiplayer packet handlers' relocation, and `MatchSession`/`MatchRoomState`'s eventual full
separation (the projection machinery moving out) can happen safely. Not scoped or started as of this
checkpoint.

---

## 3. Decisions already taken — do not re-litigate these

Each was decided with evidence and is recorded. A successor re-opening one costs a session and
usually reaches the same answer.

| Document | Settles |
|---|---|
| `docs/adr/ADR-008-dependency-enforcement.md` | values crossing a boundary are `static readonly`, not `const`, because NetArchTest reads IL and C# inlines constants |
| `stage-c-order-decision.md` | the order of Stage C, and the measured payoff of C4 |
| `c2-deferred-decision.md` | why C2 is off the path — its own proof is unreachable by it |
| `c1-transport-seam-decision.md` | C1 is split: the seam is cut in place first, the project move is decided after; invariant 9 stays |
| `chat-seam-decision.md` | `IrcMessage` is a leaked wire type; `ChatLine` + two notifier contracts; the five-commit order for the chat seam |
| `hub-adoption-decision.md` | the event hub carries deltas only; the seed handshake was deleted because `SeedIfNotSuperseded` had no callers |
| `logout-as-event-decision.md` | logout uses an ordered handler list, **not** an event bus — there is no domain-event bus in this codebase, and `Shared/Eventing` is entirely SSE machinery |
| `diagnostics-boundary-decision.md` | `Diagnostics → Auth` authorised; four other edges refused in favour of published gauges |
| `architecture-progress.md` | the instrument map in §4, and what the C5 gate is allowed to gate on |

**Two plan documents are stale in specific places and say so inline.**
`phase-1-multiplayer.md`'s "Task 1.3's design question" weighs two candidates that
`hub-adoption-decision.md` rejects in favour of a third, and `architecture-target-20260908.md` §2
carries a visible correction block about a measurement error. Prefer the decision documents.

---

## 4. The instrument map — the most expensive knowledge here

Five things measure coupling on this project. **They cover different populations and they fail in
different directions.** Most of a day went into learning this, and a successor who assumes one number
means "the coupling" will draw a false conclusion.

| Instrument | Population | Enforced | Blind to |
|---|---|---|---|
| `SliceAdjacency` + `SliceBoundaryTests` | `Features/<Slice>` → `Features/<Slice>` | every build | `const` values, until ADR-008 |
| `Shared_Should_Not_Reference_Features` pinned list | `Shared/` → `Features/` | every build, exact set equality | nothing known |
| `DomainAdjacency` + `DomainBoundaryTests` | inside `Basil.Domain`, population read from the assembly at run time | every build | nothing known |
| `TransportSeamTests` pinned list | `Features/` types outside `.Packets` and outside `Irc` → `Basil.Protocol` | every build, exact set equality | nothing known; added 2026-09-14 |
| `plans/execution/measure-slice-graph.py` | files owned by a slice-named directory, in `Features/` or `Domain/` | nothing — it is a report | three things, below |

The script has **three proven blind spots**:

1. **Inferred types.** `sender.IrcConnection` is typed `IIrcConnection`, a `Features.Irc` type, but
   that name is never written in the file. The script reads source text, so it cannot see it. Task C4
   hit exactly this: it predicted `Bot → Irc` was gone, the script agreed, and deleting the allowlist
   row failed the build.
2. **`Shared/` as a source.** It counts edges *sourced from* slice-named directories, so
   `Shared/Sessions/PlayerLogoutService.cs` importing five slices was invisible. Task C3 removed that
   coupling and **every script number stayed identical**.
3. **Namespaces that are not slices.** It takes slice names from the directory listing under
   `Features/`, so `Channels`, `Login` and `Social` in `Basil.Domain` were never examined. C6 measured
   the Domain graph from the assembly instead and found **six edges where the record said three**.

### The rules that follow

* **An edge counts as removed only when its `SliceAdjacency` row can be deleted and the architecture
  suite stays green.** The script says where to look. The allowlist says whether it worked.
* **Name the currency before claiming a win.** C3's was the `Shared -> Features` pinned list; C4's
  was allowlist rows; C1a's is the `TransportSeamTests` pinned list; C1b's would be the
  `Shared -> Features` list and `DomainAdjacency`. Without this, a task that moved nothing reads as
  progress on whichever number happened to drift.
* **The route grep is weak.** It records only the string inside the `Map*` call, so for a route mapped
  inside a `MapGroup` it captures the *suffix* and cannot see a changed group prefix; it deduplicates;
  and it cannot resolve an interpolated pattern. The Roslyn endpoint map reports 151 endpoints with
  full patterns. **Stage D moves routes into three host projects — exactly when group prefixes move —
  so verify Stage D with the endpoint map, not the grep.** Task H4 has this written in.

---

## 5. Operating rules learned the expensive way

Every one of these cost a worker session or a wrong answer.

* **Never run `dotnet test` over the whole solution in one call.** It exceeds the 600-second tool
  ceiling, the tool backgrounds it, and the worker stalls. Build once with
  `dotnet build --configuration Debug`, then one **foreground** call per test project with
  `--no-build`, `Basil.IntegrationTests` last (about six minutes alone), `timeout: 600000` on each.
  Following the earlier version of this rule — "foreground with a 600-second timeout" — still failed,
  because the whole suite does not fit inside it.
* **Never `git add -A` or `git add .`** Stage explicit paths. A blanket add swept another worker's
  unverified files into a documentation commit twice.
* **A resumed tree must be built before its contents are treated as progress.** `git status` cannot
  tell a finished task from a half-rewired one. Two trees once looked identical in `git status`; one
  was green and one did not compile at all.
* **Use the Rider MCP refactorings** — `rename_refactoring`, `move_type_to_namespace`,
  `change_api_signature`, `safe_delete`, `find_references`. They work on the reference index, so they
  also fix `nameof` and `<see cref>` that a text edit leaves silently wrong. They are bound to the
  solution open in the **main tree only**; pass `rootFolder: "V:/Code/cs/osuBasil"` when asked. The
  one behavioural bug found in all of Stage C was in hand-applied work.
* **The worker keeps its own checkpoint, in the same commit as each green step** — not the
  orchestrator afterwards. A worker here dies mid-task roughly every session, and when it does its
  report dies with it. State what is **applied but uncommitted** separately from what is next: a
  checkpoint once listed five already-applied steps as "next", and the successor reviewed a diff it
  believed it was about to write, with a real bug sitting in it.
* **One worker per tree.** Never coordinate manually around overlapping edits — serialise, assign
  ownership, or move the boundary.
* **Model tiers.** Opus for architectural judgement only; Sonnet for implementation; Haiku for pure
  verification. The account is Claude Pro, not Max — budget is a real constraint, and a fixed model
  for every task wastes it.

---

## 6. Tooling worth reaching for, and one that lies

Audited by running the tools, not by reading their descriptions.

**Use:** the Rider refactorings; `cwm-roslyn-navigator get_endpoint_map` (151 endpoints with full
patterns, constraints, file and line) and `find_references` (found an `<inheritdoc cref>` that grep
reads as a comment); `codegraph_explore` instead of a grep-then-read loop; `context7` for NetArchTest
and xunit v3 API questions, which Stage E2 will need; `deepwiki` against `osuAkatsuki/bancho.py` for
scope questions.

**Do not trust:** `cwm-roslyn-navigator get_dependency_graph` **errors** on this solution at both
project and namespace scope, and `detect_circular_dependencies` returns zero cycles where the feature
graph is demonstrably one strongly connected component of ten slices. On a migration whose central
gate is a cycle count, that is the most expensive possible false comfort.

---

## 7. Where I was wrong, so you do not trust this record blindly

* I claimed four services touched the database, from an unanchored grep for `ExecuteAsync` that
  matched every `BackgroundService` override. A worker disproved it; Task B3 closed with no code
  change. The correction is a visible block in `architecture-target-20260908.md` §2.
* I wrote a checkpoint saying B5 still owed an invariant test. It already existed, bounded with a
  five-second `CancellationTokenSource`, committed two commits earlier. I had not checked.
* I reported the `Shared → Features` pinned list as "12 down to 11" twice. The array held 13 and then
  12; I was quoting a comment that had drifted before I arrived. The comment no longer states a count.
* My prediction table for C4 listed `Bot → Scores` as an edge that would disappear. It was
  `using Basil.Domain.Scores` — a Domain namespace, never a slice-boundary violation, never an
  allowlist row.
* I recorded the Domain graph as three edges. It is six; see §4.
* The plan sized C1 as a file move with a table that checked five framework packages and not the
  one dependency that actually blocks it. Found 2026-09-14 by measuring from the compiled assembly
  instead of trusting the table; §2.
* `architecture-progress.md` carried three values for one number (45/53, 52, 43/50) because the
  C5 gate line and the log table were not updated when C4 and C3 landed. Now one value, with the
  commit and the command beside it.

The pattern: **every one of these was a number or a claim written without measuring, and every one was
caught by someone measuring it.** Prefer a probe to the record — this document included.

---

## 8. Open items

* **C1a, then C5.** C5 gates Stage D: if the graph did not move as predicted, stop and report before
  Stage D, whose project split assumes it did. C5 now also reads the `TransportSeamTests` list.
* **`BeatmapsetManagementEndpointTests`'s migration-sweep race is fixed (Stage G, Task G5,
  `e2931b33`).** The test now awaits `BeatmapsetMigrationService`'s `BackgroundService.ExecuteTask`
  before sending its PUT, instead of racing the sweep's own file writes. Attempted the plan's stated
  "real fix" for the separate `Basil.LoadTests` `ReloginGuardWindowSeconds` duplication in the same
  pass (drop `<SelfContained>` from `Basil.Server.csproj`, add a real `ProjectReference`) and hit a
  transitive `NU1202`: `Humanizer.Core.*` 2.14.1, pulled in through `ppy.osu.Game.Rulesets.*`, isn't
  `net10.0`-compatible once both projects restore together. Reverted; left as accepted debt with the
  real blocker recorded in `basil-plan-20260909.md`'s Stage G section rather than the originally
  assumed one.

* **`DiagnosticEndpointTests`' live-test flake (`GetOverviewLive_FirstEventCarriesTheCuratedFields`,
  `GetGcLive_FirstEventIsARealGcReading`) investigated 2026-09-15; no code defect found, no fix
  applied.** The prior brief's central hypothesis — that the fix is making the SSE stream deliver an
  immediate first sample on subscribe, not wait for the shared periodic tick — turned out to already
  be exactly how `DiagnosticRoutes.StreamCategory` is written: it calls `sample()` synchronously and
  yields it as the first SSE item *before* ever touching `subscription.Events`, with its own doc
  comment stating this in as many words ("a subscriber takes its own fresh reading immediately on
  connecting, rather than waiting for the next broadcast tick"). `hub-adoption-decision.md` confirms
  this is the decided design ("a subscriber's first item is the state"), not merely permitted by it.
  That rules out the hypothesis the prior dispatch (died on the session limit after building, nothing
  landed) was sent to test. With the code path already correct, the remaining explanation for a
  single failure once on an 8-minute run and once on a 5-min-55s run, always passing in isolation, is
  ordinary resource contention (hundreds of `WebApplicationFactory` hosts and their background
  services running across the full suite) delaying the connect-and-first-read sequence past its
  10-second budget under an unusually loaded run — not a logic bug to patch. **No further action
  planned**; this matches the test's own already-stated tolerance ("a single failure of one of these
  on a full run is not a regression; anything else is"). Re-open only if it starts failing more than
  once per full run, or fails outside a heavy full-suite context.
* **Task H2** — the localization rule set the user supplied becomes developer and agent documentation,
  and `CLAUDE.md` splits into `docs/for-agents/`. Deferred by the user to the documentation phase; the
  source is at `C:\Users\haith\Desktop\osuBasil-docs.md`.
* **Five documents under `plans/`** still link to `../docs/for-developers/known-limitations.md`. That
  file was deliberately moved to `plans/known-limitations.md` in `8d3e9060` — "It's just local
  document for implement PR" — so the links are stale, not the file. They are the pre-migration perf
  investigation, labelled historical in `plans/README.md`, and are not maintained.
* **The user owes a force-push** restoring PR #7 to `97e7d56`. Blocked by a hook; the command was
  handed over and only the user can run it.

---

## 9. Conventions that are contracts, not preferences

From `CLAUDE.md`, and each has bitten: bancho packet layouts are wire contracts; user-visible chat
strings live in named production constants (`MpReplies`, `IrcReplies`, `BotReplies`) and changing the
wording is a contract change; `Privilege`, never `Priv`; no pp in gameplay; the response envelope on
the `api.` host; and the multiplayer concurrency model — hold the match lock across the whole
state-transition-and-broadcast sequence, and never introduce a second synchronisation mechanism for
the same state.

Commit messages, code comments and documentation are written in normal English prose, and comments
explain the reason without citing a document or an ADR number as the reason.
