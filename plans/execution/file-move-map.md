# Task 0.3 file move map

Derived from the tree at `d5d1b32`, before any move. Paths on the left are pre-move; `Basil.Web`
paths are written as `Basil.Server` because Task 0.2 renames that project first.

Whole directories move where every file in them belongs to one destination. Individual files are
listed only where a directory splits. Anything not listed here is a gap — stop and escalate rather
than guessing a home for it.

Every move uses `git mv` so history follows. After moving, each file's `namespace` declaration
becomes the dotted form of its new folder, e.g. `namespace Basil.Server.Features.Multiplayer;`.

---

## Features/Multiplayer

```
src/Basil.Application/Services/Multiplayer/            -> Features/Multiplayer/
src/Basil.Application/Sessions/Multiplayer/            -> Features/Multiplayer/
src/Basil.Application/Packets/Multiplayer/             -> Features/Multiplayer/Packets/
src/Basil.Application/Backgrounds/MatchRoundEndOutbox.cs
src/Basil.Infrastructure/Sessions/InMemoryMatchRegistry.cs
src/Basil.Infrastructure/Sessions/MatchLiveEvents.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteMatchRepository.cs
src/Basil.Application/Abstractions/Multiplayer/IMatchRepository.cs
src/Basil.Server/Routing/Api/MatchRoutes.cs
src/Basil.Server/Routing/Api/MatchSubResourceRoutes.cs
```

Exceptions inside those directories, which go elsewhere:
* `Services/Multiplayer/JsonMergePatch.cs` -> `Shared/Http/`
* `Sessions/Multiplayer/IMatchLiveEvents.cs` -> `Shared/Eventing/`

`Routing/Api/LiveSseRoutes.cs` splits: the match streams go to `Features/Multiplayer/`, the
per-player `input`/status stream goes to `Features/Spectating/`, and the shared helpers
(`Subscribe`, `SubscribeWithSnapshot`, `SubscribeMultiWithSnapshot`, `IsLiveRoute`, `SseError`,
`NotLive`, `RegisterWithMatch`) go to `Shared/Eventing/SseEndpoints.cs`. Task 1.3 rewrites the
helpers onto the hub; this task only relocates them.

## Features/Chat

```
src/Basil.Application/Services/Chat/                   -> Features/Chat/
src/Basil.Application/Sessions/Channels/               -> Features/Chat/
src/Basil.Application/Packets/Channels/                -> Features/Chat/Packets/
src/Basil.Application/Abstractions/Channels/           -> Features/Chat/
src/Basil.Infrastructure/Sessions/InMemoryChannelRegistry.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteChannelRepository.cs
```

## Features/Bot

```
src/Basil.Application/Services/Bot/                    -> Features/Bot/
src/Basil.Application/Abstractions/Bot/                -> Features/Bot/
```

`Services/Bot/MpCommandService.cs` and `MpReplies.cs` stay in `Features/Bot/` for this task.
Task 1.8 moves the `!mp` locale keys to the Multiplayer fragment; Task 2.1 splits the dispatcher.
Do not anticipate either here.

## Features/Irc

```
src/Basil.Application/Services/Irc/                    -> Features/Irc/
src/Basil.Application/Sessions/Irc/                    -> Features/Irc/
src/Basil.Application/Sessions/IrcSession.cs           -> Features/Irc/
src/Basil.Infrastructure/Irc/                          -> Features/Irc/
src/Basil.Infrastructure/Sessions/IrcSessionRegistry.cs
```

## Features/Users

```
src/Basil.Application/Services/Users/                  -> Features/Users/
src/Basil.Application/Abstractions/Users/              -> Features/Users/
src/Basil.Application/Abstractions/Social/             -> Features/Users/
src/Basil.Application/Packets/Users/                   -> Features/Users/Packets/
src/Basil.Infrastructure/Cache/CachingUserRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteUserRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteUserStatRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteUserLogRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteRelationshipRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteClientHashRepository.cs
src/Basil.Server/Routing/Api/UserRoutes.cs
src/Basil.Server/Routing/Bancho/AvatarRoutes.cs
```

`Abstractions/Users/IPasswordHasher.cs` and `ITokenGenerator.cs` go to `Features/Auth/` instead —
they are authentication concerns that happen to be filed under Users today.

## Features/Auth

```
src/Basil.Application/Services/Authentication/         -> Features/Auth/
src/Basil.Application/Services/Anticheat/              -> Features/Auth/
src/Basil.Application/Abstractions/Login/              -> Features/Auth/
src/Basil.Application/Abstractions/Users/IPasswordHasher.cs
src/Basil.Application/Abstractions/Users/ITokenGenerator.cs
src/Basil.Infrastructure/Security/                     -> Features/Auth/
src/Basil.Infrastructure/GuidTokenGenerator.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteIngameLoginRepository.cs
src/Basil.Server/Auth/                                 -> Features/Auth/
src/Basil.Server/Routing/Api/AdminKeyRoutes.cs
```

`Infrastructure/Security/RijndaelScoreDecryptor.cs` goes to `Features/Scores/` — it decrypts score
submissions, not credentials.

## Features/Beatmaps

```
src/Basil.Application/Services/Beatmaps/               -> Features/Beatmaps/
src/Basil.Application/Abstractions/Beatmaps/           -> Features/Beatmaps/
src/Basil.Infrastructure/Beatmaps/                     -> Features/Beatmaps/
src/Basil.Infrastructure/Performance/PpyOsuCalculator.cs
src/Basil.Infrastructure/Cache/CachingBeatmapRepository.cs
src/Basil.Infrastructure/Cache/CachingBeatmapsetRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteBeatmapRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteBeatmapsetRepository.cs
src/Basil.Server/Routing/Api/BeatmapsetRoutes.cs
src/Basil.Server/Routing/Assets/BeatmapsetAssetRoutes.cs
src/Basil.Server/Routing/Bancho/BeatmapAssetRoutes.cs
```

## Features/Scores

```
src/Basil.Application/Services/Scores/                 -> Features/Scores/
src/Basil.Application/Abstractions/Scores/             -> Features/Scores/
src/Basil.Infrastructure/Persistence/Repositories/SqliteScoreRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteLeaderboardStore.cs
src/Basil.Infrastructure/Security/RijndaelScoreDecryptor.cs
src/Basil.Server/Routing/Api/ScoreRoutes.cs
```

`Abstractions/Scores/IReplayStorage.cs` stays with Scores; its implementation
`Infrastructure/Storage/FileSystemReplayStorage.cs` goes to `Shared/Storage/` — it is a filesystem
concern with a second peer (`FileSystemResponseCache`).

## Features/Spectating

```
src/Basil.Application/Services/Spectating/             -> Features/Spectating/
src/Basil.Application/Sessions/Spectating/             -> Features/Spectating/
src/Basil.Application/Packets/Spectating/              -> Features/Spectating/Packets/
src/Basil.Infrastructure/Sessions/PlayerInputEvents.cs
src/Basil.Infrastructure/Sessions/PlayerStatusEvents.cs
```

## Features/Content

```
src/Basil.Application/Services/Content/                -> Features/Content/
src/Basil.Application/Abstractions/Content/            -> Features/Content/
src/Basil.Application/Abstractions/Settings/           -> Features/Content/
src/Basil.Infrastructure/Cache/CachingSettingsRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteSettingsRepository.cs
src/Basil.Infrastructure/Persistence/Repositories/SqliteMenuBannerRepository.cs
src/Basil.Server/Routing/Api/MenuBannerRoutes.cs
src/Basil.Server/Routing/Api/MenuIconRoutes.cs
src/Basil.Server/Routing/Api/MenuSeasonalRoutes.cs
src/Basil.Server/Routing/Api/FaqRoutes.cs
src/Basil.Server/Routing/Api/MotdSettingsRoutes.cs
src/Basil.Server/Routing/Api/MirrorSettingsRoutes.cs
src/Basil.Server/Routing/Api/AnnounceRoutes.cs
src/Basil.Server/Routing/Assets/MenuAssetRoutes.cs
```

`AnnounceRoutes.cs` is a judgement call: it pushes an in-game notification to online players, which
is closer to Chat than to Content. Filed under Content because the endpoint is grouped with the
other operator-facing settings surfaces. If Task 2.x finds it wants Chat internals, move it then
and record the reason.

## Shared/Eventing

```
src/Basil.Application/Services/SnapshotChannel.cs
src/Basil.Application/Services/SequenceGate.cs
src/Basil.Application/Services/SseSubscriberRegistry.cs
src/Basil.Application/Services/BoundedSseChannel.cs
src/Basil.Application/Sessions/Multiplayer/IMatchLiveEvents.cs
(the shared SSE helpers extracted from Routing/Api/LiveSseRoutes.cs)
```

## Shared/Sessions

```
src/Basil.Application/Sessions/GameSession.cs
src/Basil.Application/Sessions/UserSession.cs
src/Basil.Application/Sessions/ISessionRegistry.cs
src/Basil.Application/Sessions/PlayerLogoutService.cs
src/Basil.Application/Backgrounds/GhostDisconnectService.cs
src/Basil.Infrastructure/Sessions/GameSessionRegistry.cs
```

`UserSession` is referenced by Auth, IRC, Chat, Bot, Multiplayer and Spectating, which is what makes
it genuinely shared rather than Users-owned. `GameSessionRegistry` and `IrcSessionRegistry` differ:
the IRC one is only used by the IRC slice, so it stays there.

## Shared/Persistence

```
src/Basil.Infrastructure/Persistence/SqliteConnectionFactory.cs
src/Basil.Infrastructure/Persistence/SqlMigrationRunner.cs
src/Basil.Infrastructure/Persistence/SqliteInstrumentation.cs
src/Basil.Infrastructure/Persistence/DatabaseConnectionStringBuilder.cs
src/Basil.Infrastructure/Persistence/Migrations/*.sql
```

The `.sql` files are an `EmbeddedResource` loaded by `Assembly.GetExecutingAssembly()`. Their
resource names change with the assembly and folder, and `SqlMigrationRunner` filters by name — check
that filter after the move, because a silently empty migration set looks like success.

## Shared/Localization

```
src/Basil.Application/Services/ReplyLocale.cs
src/Basil.Application/Data/Localization/*.json          -> (content, output path Data/Localization/)
```

Task 0.8 replaces `ReplyLocale` and splits the JSON. Move it as-is here.

## Shared/Logging

```
src/Basil.Server/Logging/CategoryEnricher.cs
src/Basil.Server/Logging/HardLinkFileLifecycleHooks.cs
```

## Shared/Http

```
src/Basil.Server/Middleware/                           -> Shared/Http/Middleware/
src/Basil.Server/OpenApi/                              -> Shared/Http/OpenApi/
src/Basil.Server/Routing/ContentTypes.cs
src/Basil.Server/Routing/NumericIdRouteConstraint.cs
src/Basil.Server/Routing/Api/Pagination.cs
src/Basil.Server/Routing/Api/RouteDocs.cs
src/Basil.Server/Routing/Api/ApiHostRoutes.cs
src/Basil.Server/Routing/Api/AbbreviationRedirectRoutes.cs
src/Basil.Server/Routing/Bancho/BanchoHostGroups.cs
src/Basil.Server/Routing/Bancho/BanchoProtocolRoutes.cs
src/Basil.Server/Routing/Bancho/OsuWebRoutes.cs
src/Basil.Server/Routing/Assets/AssetsHostRoutes.cs
src/Basil.Application/Formats/                         -> Shared/Http/
src/Basil.Application/Services/Multiplayer/JsonMergePatch.cs
src/Basil.Application/Packets/IPacketHandler.cs        -> Shared/Http/Bancho/
src/Basil.Application/Packets/PacketDispatcher.cs      -> Shared/Http/Bancho/
src/Basil.Application/Packets/PacketBuilders.cs        -> Shared/Http/Bancho/
```

`OsuWebRoutes.cs` (608 lines) is the osu! web-facing surface and spans several slices. Move it whole
here for this task; do not split it. If a later slice phase needs to own part of it, that phase
splits it and says so.

## Shared/Configuration

```
src/Basil.Application/Configurations/                  -> Shared/Configuration/
```

## Shared/Media

```
src/Basil.Infrastructure/Media/                        -> Shared/Media/
src/Basil.Application/Abstractions/Media/              -> Shared/Media/
```

## Shared/Storage

```
src/Basil.Infrastructure/Storage/                      -> Shared/Storage/
src/Basil.Infrastructure/System/HardLink.cs            -> Shared/Storage/
src/Basil.Application/Abstractions/Storage/            -> Shared/Storage/
```

## Split in a later task, moved whole here

```
src/Basil.Application/Diagnostics/BasilMetrics.cs      -> Shared/BasilMetrics.cs   (Task 0.7 splits)
src/Basil.Server/Program.cs                            -> Host/Program.cs          (Task 0.5 splits)
src/Basil.Application/DependencyInjection.cs           -> Host/ApplicationDependencyInjection.cs
src/Basil.Infrastructure/DependencyInjection.cs        -> Host/InfrastructureDependencyInjection.cs
```

Task 0.6 replaces both DI files with per-slice extensions. Moving them into `Host/` for now keeps
the build green without pretending the seam already exists.

## Deleted, not moved

```
src/Basil.Application/AssemblyMarker.cs
src/Basil.Infrastructure/AssemblyMarker.cs
```

Both exist only so the old architecture tests could name an assembly. Task 0.4's replacements
reference `Basil.Server.Host.Bootstrap` instead. `Basil.Domain/AssemblyMarker.cs` and
`Basil.Protocol/AssemblyMarker.cs` stay — those assemblies are still asserted against.

---

## Expected `Shared` segment set after the move

`Eventing`, `Sessions`, `Persistence`, `Localization`, `Logging`, `Http`, `Configuration`, `Media`,
`Storage` — and nothing else. Task 0.4's allowlist test enforces exactly this list. A file that
seems to need a tenth segment is a signal that it belongs in a feature.
