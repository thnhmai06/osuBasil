# Module layout

How a module's Application and Infrastructure projects are organised, folder by folder. The dependency rules and the module graph are in [`AGENTS.md`](../../AGENTS.md) and [`architecture.md`](../for-developers/architecture.md).

Application is sliced by module first, then by folder of responsibility; namespace = project + folder
(`Basil.Application.Chat.Objects`). Application.Shared owns shared values, paging/search syntax and event
contracts/queue; Infrastructure.Shared owns shared persistence and event delivery. The table lists where each folder's
contents are; `Handlers/`, `Logic/` and `Workers/` and the implementations of `Services/` are in the module's
Infrastructure project.

Every module uses these folders, and creates only the ones it needs:

| Folder | Holds |
|---|---|
| `Objects/` | The module's live objects, their parts and contexts, each object's outcome enum, and its event records (`Objects/Events/`) |
| `Registry/` | What keeps live objects in RAM and hands them out (`UserRegistry`, `Lobby`, the channel registries); one of the three stores with Repository (`Persistence/`, awaited, cache-aside) and Storage (`Storage/`) |
| `Capabilities/` | The `Scoped*` views only |
| `Ports/` | The ports to external computation and the data they exchange |
| `Services/` | The contracts of the services the system provides (the implementation is in Infrastructure). Only what a module offers as a service belongs here |
| `Persistence/` | The repository ports, their query records and the data both layers name |
| `Storage/` | The file-storage ports (`IBeatmapsetStorage`, `IReplayStorage`, `IFaqStorage`, the avatars and menu assets) |
| `Messages/` | The Mediator requests the module answers (a message carries the `Connection` as `Actor`) and the values they name |

Infrastructure mirrors those ports: `Persistence/` for the `DbContext`, rows and repositories (PostgreSQL and its migrations are in `Infrastructure.Persistence`), `Storage/` for the
file implementations, `Adapters/` for what needs an external library, `Registry/` for the shared memory
primitives, `Handlers/` for the Mediator handlers, `Logic/` for the object-free application logic, `Services/` for the
service implementations and `Workers/` for the loops and hosted services. Tests mirror the same folders under `tests/`.

```text
Users/         IUserRepository and friends, UserQuery, login inputs    (Persistence)
               IUserAvatarStorage                                      (Storage)
               IAccountRules (internal)                                (Ports)
               User, UserContext, UserGrants, UserProfile, UserOutcome  (Objects)
               Connection, ConnectionContext, ConnectionRules,
               PlayerStatus                                            (Objects/Connections)
               UserRegistry                                            (Registry)
               ScopedUser*, ScopedStableConnection                      (Capabilities)
               AccountRules                                            (Infrastructure Logic)
               ConnectionHandler                                       (Infrastructure Handlers)
Chat/          IChannelRepository                                      (Persistence)
               the channel tree, ChannelContext, ChannelOutcome,
               ChannelEvents                                            (Objects)
               GeneralChannelRegistry, RoomChannels, PmChannels,
               SpectatorChannels                                       (Registry)
               ScopedChannel, ScopedGeneralChannel                     (Capabilities)
               PostChatMessage, ChatTarget                             (Messages)
               PostChatMessageHandler and the room, lobby, connection
               and spectator handlers                                  (Infrastructure Handlers)
Multiplayer/   match, round, event and report repositories             (Persistence)
               Room and its parts, RoomOutcome, the event records       (Objects)
               Lobby, LobbyContext                                      (Registry)
               ScopedLobby and the room's scopes                        (Capabilities)
               the match recorders, ScoreHandler, the lobby handlers    (Infrastructure Handlers)
Beatmaps/      beatmap and beatmapset repositories, import and scan
               data                                                     (Persistence)
               IBeatmapsetStorage                                       (Storage)
               Beatmapset, Beatmapsets, contexts, outcomes, events      (Objects)
               ScopedBeatmapset                                         (Capabilities)
               the reader/analyser/mirror/assets ports and
               IBeatmapsetImporter, IBeatmapsetScanner (internal)       (Ports)
               the importer, scanner and planner                        (Infrastructure Logic)
Scores/        score, stats repositories, submission inputs             (Persistence)
               IReplayStorage                                           (Storage)
               ScoreSubmitter, IScoreBeatmapAcceptance,
               ScoreSubmissionOutcome, ScoreOutcome, ScoreEvents        (Objects)
               ScopedScoreSubmitter                                     (Capabilities)
               IScoreRecorder (internal)                                (Ports)
               ScoreRecorder                                            (Infrastructure Logic)
Anticheat/     IAnticheatReporter                                       (Services)
               AnticheatReporter                                        (Infrastructure Services)
               AnticheatEvents                                          (Objects/Events)
Content/       IMenuBannerRepository                                    (Persistence)
               the FAQ, menu, screenshot and seasonals storages          (Storage)
               IScreenshotInspector                                     (Ports)
Servers/       Server, ServerContext, ServerOutcome, ServerRecovery     (Objects)
               ScopedServer and its parts                               (Capabilities)
Diagnostics/   ServerDiagnostics, DiagnosticsOutcome                    (Objects)
               ScopedDiagnostics                                        (Capabilities)
               IDiagnosticsIntake, IDiagnosticsFeed                     (Services)
```

`Diagnostics` (intake, feed and the maintenance actions behind `ScopedDiagnostics`) is a module of its own; the
contribution contracts every module answers (`CollectDiagnostics`, `IDiagnosticsCollector`, `DiagnosticsValue`) live in
Application.Shared; `DiagnosticsSample`, `IClearTally`, `ICacheCleaner`, `HostLabels` and `DiagnosticsMetrics` are Diagnostics'
own. Feature event **records** live under `Objects/Events/` of the module that owns the object they describe; the shared
event contract, publisher and queue belong to Application.Shared, and the dispatcher belongs to
Infrastructure.Shared's `Workers/`. Concrete handlers live in the `Handlers/` folder of the module's Infrastructure project or in the consumer that
owns them, including `Basil.Infrastructure.Bot`. Host configuration (ports, TLS, data paths) belongs to the hosts. BasilBot commands
and announcements are server features: a command is an entry point that uses the typist's scoped objects, and the bot
only replies. Query **records** (the filter a repository applies, with `Parse` for the search syntax) belong to
Persistence; routes and handlers that answer queries do not. Server settings changed at runtime (`ServerSettings`,
owned by Application.Shared, with its repository and the `ReadSettings` message) exist only while the server runs and are stored through `ISettingsRepository`; they are
not Domain and not host configuration.

* Shared projects depend only on Domain and shared external packages. `Users`, `Connections`, `Beatmaps` and `Scores` do not depend on
  `Multiplayer` objects. Anticheat flags,
  wherever they arrive (score submission, `lastfm.php`), are reported through `IAnticheatReporter.Report`,
  the single source of anticheat events; BasilBot turns them into chat warnings.
* `Chat` references `Multiplayer`, and both reference `Users` and `Connections` (`Room.Host`, `Channel.Members`, the online
  `User`); treat them as one cluster. `Users` and `Connections` reference each other (a user holds its connections, a
  connection knows its user), so those two form one cluster of their own. Their runtime objects and registries live in Objects;
  persistent repositories remain in each module's Persistence folder. Objects change their own state through behaviour, with
  implementation access limited to the owning object and its collaborators. References run from the acted-on
  object to the actor, never back.

