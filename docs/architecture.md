# Architecture

## System architecture

**Basil** follows **Monolith Clean Architecture** — a single deployment, with clean-architecture dependency inversion enforced between layers.

This rule is checked by automated tests (`tests/Basil.ArchitectureTests`, using [NetArchTest](https://github.com/BenMorris/NetArchTest)), not left as a convention — a PR that violates the dependency direction will fail CI.

![Clean Architecture](docs\assets\clean-architecture.jpg)

| Project                | References                          | Purpose                                                                                  |
| ---------------------- | ----------------------------------- | ---------------------------------------------------------------------------------------- |
| `Basil.Domain`         | None                                | Pure C#: enums, records, value calculators                                               |
| `Basil.Protocol`       | None                                | Reads/writes Bancho wire-format packets                                                  |
| `Basil.Application`    | Domain, Protocol                    | Use cases, packet handlers, and *ports* (interfaces) describing what Infrastructure provides |
| `Basil.Infrastructure` | Application, Domain                 | SQLite/filesystem/osu!lazer-ruleset-library implementations                               |
| `Basil.Web`            | Application, Infrastructure, Protocol| ASP.NET Core host: subdomain routing, DI composition root, Program.cs                    |

**Dependency rule:**

- **Domain and Protocol depend on nothing else in the solution.**
- Application depends on Domain and Protocol, but never on Infrastructure or Web.
- Infrastructure implements those interfaces but is never referenced by Application.
- Web is the only project aware of all four others — it is the composition root wiring interfaces to their concrete implementations at startup.

This means:

- All SQLite/filesystem details live in `Basil.Infrastructure`
- Application use cases can be unit-tested by substituting fakes for those interfaces — no database needed.
- `tests/Basil.Infrastructure.Tests` is the only test suite that talks to a real SQLite (a temp file created per run, deleted on completion), verifying concrete implementations match the schema.

## Layer layout

The three largest directories are organized by feature area rather than kept flat:

| Directory | Purpose |
| --- | --- |
| **`PacketHandlers/`** | One class per Bancho client packet, split into `Core/` (session lifecycle: login, presence, stats), `Channels/` (chat), `Spectating/`, and `Multiplayer/` (match + tournament packets, the largest group) |
| **`Abstractions/`** | Ports that Infrastructure implements, organized by domain concept: `Beatmaps/`, `Scores/`, `Users/`, `Channels/`, `Social/` (relationship + moderation logging) |
| **`Sessions/`** | In-memory session state (`PlayerSession`, `ChannelSession`, `MatchSession`) and the registries tracking them, split into `Channels/`, `Irc/` (IIrcConnection — bridge for bancho packets or real TCP), `Multiplayer/` with per-player state at the root, and `Spectating/`. `Sessions/Multiplayer/IMatchLiveEvents` and `Sessions/Spectating/IPlayerInputEvents` are non-blocking, C#-event-based pub/sub pushing match/player state directly to the `api.` host's live SSE layer |
| **`UseCases/`** | One directory per feature (`Authentication/`, `Beatmaps/`, `Multiplayer/`, `Scores/`, `Spectating/`, `Anticheat/`, `Bot/`, `Chat/`, `Irc/`), each containing the actual business logic that a packet handler or HTTP route delegates to. `UseCases/Chat/ChatDispatchService` is the single entry point for all chat traffic — used by both bancho handlers and IRC PRIVMSG. `UseCases/Irc/IrcAuthenticationService` authenticates IRC TCP connections and creates virtual PlayerSessions. `UseCases/Multiplayer/MatchReportService` builds tournament match reports (TRT) at read time. `UseCases/Bot/` contains BanchoBot's session bootstrap plus the `!help`/`!roll`/`!mp` command dispatcher |

`Basil.Domain`, `Basil.Protocol`, and `Basil.Infrastructure/Persistence` follow the same pattern (subdirectories per topic like `Login/`, `Beatmaps/`, `Scores/`, `Multiplayer/`, `Users/`, `Repositories/`) — namespace matches folder path, so `grep` on an import tells you exactly where the file lives.

## Request flow

### Login

The osu! client sends login as an HTTP POST without an `osu-token` header. There is no separate packet for this.

1. `Basil.Web/Routing/BanchoHostGroups.cs` — the `POST /` route of the `c.`/`ce.`/`c4.`/`c5.`/`c6.` subdomain group reads the raw body, resolves the client IP (`Basil.Domain.Login.ClientIpResolver`), and calls `OsuLoginUseCase.ExecuteAsync`.
2. `OsuLoginUseCase` (`Basil.Application/UseCases/Authentication/`) parses the login body (`LoginDataParser`, `OsuVersionParser`, `AdaptersStringParser` — all in `Basil.Domain.Login`), authenticates via `IUserRepository`/`IPasswordHasher`, checks for existing sessions, loads per-mode stats via `IStatsRepository`, and builds a `PlayerSession`.
3. Concrete implementations of `IUserRepository`/`IStatsRepository`/`IPasswordHasher` (`SqliteUserRepository`, `SqliteStatsRepository`, `BCryptPasswordHasher`) live in `Basil.Infrastructure` and are wired at startup by `InfrastructureServiceCollectionExtensions`/`ApplicationServiceCollectionExtensions` — `OsuLoginUseCase` never references a concrete class, only interfaces.
4. The response is a stream of bancho packets (`Basil.Protocol.Packets.ServerPacketWriter`) — protocol version, login reply, privileges, channel list, and cached presence/stats of all online players.

Every subsequent client request carries an `osu-token` header; `BanchoHostGroups.cs` looks up the session by token and dispatches the packet body via `BanchoPacketDispatcher` to the appropriate handler in `PacketHandlers/`.

### Multiplayer

1. Client sends packet `CREATE_MATCH` → `BanchoPacketDispatcher` routes it to `CreateMatchHandler` (`PacketHandlers/Multiplayer/`).
2. `CreateMatchHandler` delegates to `MatchMembershipService.Create` (`UseCases/Multiplayer/`), which atomically allocates a match ID from `IMatchRegistry`'s 64 slots, builds a `MatchSession` (`Sessions/Multiplayer/`), registers its chat channel, and places the host in slot 0.
3. Every subsequent match packet handler (`MatchChangeSlotHandler`, `MatchReadyHandler`, `MatchStartHandler`, etc.) acquires `MatchSession.Lock` — a per-match `SemaphoreSlim(1, 1)` — before reading or mutating slot state, then broadcasts the updated match state before releasing it.

   > [!NOTE]
   > This lock is specific to **Basil**: the Python source from **Akatsuki** relies on asyncio's single-threaded event loop for atomicity between `await` points, which ASP.NET Core's real thread pool does not provide for free.
4. `tests/Basil.Application.Tests/Sessions/MatchSessionRaceTests.cs` demonstrates the lock works — it reproduces a real lost-write race when the lock is removed, then shows the same scenario has no race with the lock in place.

## Database Schema

*Schema uses PascalCase table/column names, ids auto-incrementing from 1 (no Akatsuki-style gaps).*

Tables serving **general management**:

| Table | Purpose |
| --- | --- |
| `Users` | Accounts: `Name`/`SafeName`, `PwBcrypt`, `Priv`, `Country`, `SilenceEnd`. `Id = 1` seeded for `BanchoBot`. Trimmed to fields actually read back somewhere — no clan/preferred-mode/play-style/custom-badge/userpage columns (dead weight ported from bancho.py, no reader anywhere; clans/public profiles are out of scope, see [`working-scopes.md`](working-scopes.md)) |
| `Mapsets` | One row per beatmapset: `Artist`/`Title`/`Creator`/`Status`/`LastUpdate`/`CreatedAt` — kept live by `BeatmapWatcherService` reconciling `StorageOptions.MapsetsPath` (a per-set folder `"{Id} {Artist} - {Title}"` holding the full original `.osz` contents), not just a bare FK anchor. No osu!api staleness tracking (offline server, mapsets added only through local ingestion) |
| `Beatmaps` | One row per difficulty, keyed by `Md5`, FK'd to `Mapsets` via `MapsetId` (`on delete cascade` — deleting a mapset drops its beatmaps automatically); source for score-submission map lookup and locally-computed star rating display. `Frozen` (C# `Beatmap.IsFrozen`) hides a row from every client-reachable lookup while keeping it in the DB — admin-only |
| `Channels` | Static chat channel catalog (`#osu`, `#lobby`, ...) with `ReadPriv`/`WritePriv`/`AutoJoin` flags |
| `Relationships` | `(User1, User2)` pairs of type `friend`/`block` |
| `ClientHashes` | Hardware fingerprint log (`OsuPathMd5`/adapters/uninstall id/disk serial) per login, `LastSeenAt`/`Occurrences` — used only for anticheat, no automatic blocking logic |
| `IngameLogins` | Login log: IP, client version, stream, `LoggedInAt` — write-only, no consumer reads it back |
| `Logs` | General action log `FromId`/`ToId`/`Action` (e.g. moderation), `CreatedAt` — write-only |

Tables important for the **tournament flow**:

| Table | Purpose |
| --- | --- |
| `Matches` | One row per multiplayer room. `Id` is the stable ID external consumers use — distinct from `MatchSession.Id`, the in-memory 0-63 slot that the bancho wire protocol itself uses. Stores only `Name`, `CreatedAt`, `EndedAt` — no Mode/WinCondition/TeamType/HostId (those moved per-round into `Rounds`) |
| `Rounds` | One row per beatmap played within a match, created at `MATCH_START`/`!mp start`. Carries per-round `Mode`, `WinCondition`, `TeamType`, denormalized beatmap fields (`BeatmapArtist`/`Title`/`Version`/`Creator`), `Aborted` flag, and `Mods` |
| `Scores` | Links to a `Round` via `RoundId`, submitted through the existing `osu-submit-modular-selector.php` pipeline. New `SubmittedAt` = server wall clock when the score arrived (not `ClientTime`) |
| `MatchEvents` | Lifecycle audit log: `EventType` (0=Created…7=Closed), optional `ActorUserId`/`TargetUserId`, `Detail` text. Written by `MatchMembershipService`, `MpCommandService`, packet handlers, and `MatchRecoveryService` |
| `UserStats` | `Tscore`/`Rscore`/`Plays`/`Acc`, seeded once at zero, never updated by score submission — this server has no singleplayer ranking/progression; per-mode stats are static display data, not computed. Trimmed to the 4 columns actually read at login (`Playtime`/`MaxCombo`/`TotalHits`/`ReplayViews`/grade-count columns were dead — the only code that would have consumed them, `ScoreStatsCalculator`, was itself never called from anywhere) |

> [!IMPORTANT]
> **Score-to-round linking has no race window by design**
>
> `MatchMembershipService.StartAsync` creates the `Round` row and stores its id in `MatchSession.CurrentRoundId` *before* gameplay begins. Score submission (an HTTP request) and `MATCH_COMPLETE` (a bancho packet) arrive on two unrelated connections with no ordering guarantee between them — so score submission reads `CurrentRoundId` at submit time rather than having a "collect scores after match ends" step that must wait for both.

## Tournament match report (TRT)

**TRT is never stored in the Database** — `MatchReportService` (`UseCases/Multiplayer/`) builds it at read time from `Matches`/`Rounds`/`Scores` (for a finished match) merged with the live `MatchSession` (for an in-progress match, looked up via `IMatchRegistry.GetByDbId`).

`WinningTeam` is also computed at read time, not stored: completed `Scores` are grouped by team if non-neutral teams exist, otherwise falls back to the highest individual score.

Live updates are pushed through several ASP.NET Core SSE (`TypedResults.ServerSentEvents`) channels under the `api.` host, all following the same convention: the first event on a connection is a full snapshot, every event after that is an RFC 7396 JSON Merge Patch against the previous one (computed per-connection, not globally — a client that just connected always gets a full snapshot regardless of what earlier clients already received).

| Endpoint | Purpose |
| --- | --- |
| `SSE /matches/{matchId}` | Full match state (slots/map/status, no per-player score/input data), published from `MatchMembershipService.EnqueueState` — the single bottleneck every state-changing match packet already routes through |
| `SSE /matches/{matchId}/settings` | Just the room-configuration fields (name/password presence/size/map/mods/team type/win condition/host/referees) — never the raw password, only `hasPassword` |
| `SSE /matches/{matchId}/live` and `/matches/{matchId}/live/{slotIndex}` | Room-wide "currently playing" status (`inProgress`/`currentRoundId`/`mapId`/`mode`), idle outside an active round; the per-slot variant merges one slot's membership/score/input into a single stream tagged by SSE event name (`slot`, `score`, `input`) — follows whoever currently occupies the slot, so an occupant change takes effect on the next event with no reconnect needed |
| `SSE /matches/{matchId}/hosts`, `/refs`, `/ban`, `/timer`, `/slots` | One channel per match sub-resource — current host, referee list, ban list, countdown timer state, and the full 16-slot dict-keyed layout, respectively — each mutated by its own `MatchControlService` method and its own admin-key-gated route (`PUT`/`PATCH`/`DELETE` as applicable), replacing the old single generic `POST /match/{id}/{action}` dispatch |
| `SSE /users/{userId}/live` | Raw spectator input frames for one player (keyed by `Users.Id`, not match id — a rename of the old `/spec/{id}`), published from `SpectateFramesHandler` any time that player is logged in — BasilBot spectates every player from login onward specifically so this channel always has a source |

Every `/matches/{matchId}*` channel publishes through `IMatchLiveEvents`; `/users/{userId}/live` publishes through the player-scoped `IPlayerInputEvents`. Both are plain C# events — `Publish*` just raises the event, and each SSE connection's own subscriber does a non-blocking `ChannelWriter.TryWrite` into its own buffer — safe to call from code still holding `MatchSession.Lock` (as `EnqueueState` and the score/spectate handlers do), since the actual response writes happen on a per-connection pump, fully decoupled from the publish call.

Every one of these routes is content-negotiated rather than SSE-only: `GET /matches/{matchId}` (and each sub-resource GET above) without an `Accept: text/event-stream` header returns the same data as a one-shot JSON snapshot instead of opening a stream. Single-target one-shot actions that don't fit the CRUD shape of a sub-resource (`kick`, `invite`, `abort`, `close`) are plain `POST /matches/{matchId}/{action}` routes with no SSE channel of their own. All of the above route through the shared `MatchControlService` that `!mp` chat commands also call. In the generated OpenAPI/Scalar docs, each sub-resource keeps its own tag (so `hosts`/`refs`/`ban`/`timer`/`slots` are each their own documented section) but all `Matches`-prefixed tags are grouped together in the sidebar via the `x-tagGroups` extension — grouping is by resource, never by whether a route happens to support SSE.

## IRC Gateway

Basil runs an **embedded IRC gateway** (no separate executable, no Docker) — any real IRC client (or tournament tool like osu-ahr) can connect via TCP on port 6667 and chat/`!mp` alongside osu! clients.

Every `PlayerSession` has an `IIrcConnection` (`Sessions/Irc/`):

| Implementation | Used for | Behavior |
|---|---|---|
| `BanchoIrcBridgeConnection` | Default — every normal osu! client login | `Send(IrcMessage)` only responds to `PRIVMSG`, re-encoded as a `SEND_MESSAGE` bancho packet for the session's poll |
| `TcpIrcConnection` | Real IRC client via TCP socket | Runs read-loop (PASS/NICK/USER → PRIVMSG/JOIN/PART/AWAY/PING/QUIT), non-blocking write-pump via bounded channel (DropOldest), ping loop 60s |

**Unified chat core:** All chat — whether from osu! client (SendPublicMessage/SendPrivateMessage handler) or IRC PRIVMSG — goes through `ChatDispatchService.SendPrivmsgAsync`. This layer decides:

1. Channel (`#` prefix): broadcast via `ChannelMembershipService.BroadcastPrivmsg` (sends to each member's IIrcConnection), then runs `ICommandDispatcher` for `!` commands.
2. Bot DM: sends directly to `ICommandDispatcher` (prefix not required).
3. Regular DM: checks block/silence, delivers via `target.IrcConnection.Send` — online only, no offline persistence.

`BanchoIrcBridgeConnection.Send` filters out every IRC command except `PRIVMSG` — bancho clients don't need JOIN/PART/QUIT numerics (channel presence is already handled by the ChannelInfo packet). Real IRC clients receive everything.

### IRC login flow

1. `TcpIrcListener` (`Infrastructure/Irc/`, `BackgroundService`) accepts TCP connections on the configured port (`IrcOptions.Port`, default 6667).
2. `TcpIrcConnection.ReadLoopAsync` reads PASS + NICK + USER. When both nick and pass are available, calls `IrcAuthenticationService.AuthenticateAsync`.
3. `IrcAuthenticationService` looks up `IUserRepository.FetchByNameAsync`, gets the password hash via `FetchPasswordHashAsync`, **MD5-hashes the plaintext PASS then bcrypt-verifies** (identical to the osu! client login flow). Creates a virtual `PlayerSession` (no bancho socket) with `IrcConnection = the TcpIrcConnection itself`, joins auto-join channels, returns RplWelcome + RplTopic + RplNamReply numerics.
4. After registration, every `PRIVMSG`/`JOIN`/`PART`/`AWAY`/`QUIT` from the IRC client is dispatched by `TcpIrcConnection` to `ChatDispatchService`/`ChannelMembershipService`.

> [!NOTE]
> **Passwords:** IRC PASS requires the **account password** (same as osu! client login) — unlike official osu!Bancho (irc.ppy.sh uses a separate password "different from your account password"). `ApiKey` and `UpdateApiKeyAsync` in `IUserRepository` were once staged but were dead code and have been removed.

## BanchoBot handler

`BanchoBot` (`UseCases/Bot/BotBootstrapService`) is bootstrapped as a real `PlayerSession` at startup — it has no client connection behind it, so it is exempted from `GhostDisconnectService`'s reap sweep via `PlayerSession.IsBot` (it never sends real ping packets, so `LastRecvTime` would never advance without this exemption).

`SendPublicMessageHandler`/`SendPrivateMessageHandler` forward every message to `ChatDispatchService.SendPrivmsgAsync`, which routes messages starting with `!` to `ICommandDispatcher` — the dispatcher routes to either the general command table (`!help`, `!roll`) or `MpCommandService` for `!mp <subcommand>` — the latter only when the message is sent in the match's own chat channel and the sender passes `MatchSession.IsReferee`.

Bot replies are broadcast via `ChannelMembershipService.BroadcastPrivmsg` (IRC-shaped) rather than building packets directly — so real IRC clients in the channel also see BasilBot's responses.

## TL;DR

When adding a new feature, remember:

| Feature | How to add |
| --- | --- |
| A new bancho packet | A new class in the corresponding `PacketHandlers/*` subdirectory, registered in `ApplicationServiceCollectionExtensions`, counted in `CompositionRootTests` |
| A new piece of persisted state | A new method on an existing (or new) interface under `Abstractions/*`, implemented in `Basil.Infrastructure/Persistence/Repositories/` |
| A new HTTP endpoint | A new route in `Basil.Web/Routing/BanchoHostGroups.cs`, under the applicable host group (`osu.`, `b.`, `api.`) |
| A new IRC command | Dispatched in `TcpIrcConnection.HandleRegisteredCommandAsync` (`Infrastructure/Irc/`), calling existing services in the Application layer |
| New chat routing logic | Logic added to `ChatDispatchService.SendPrivmsgAsync` (`UseCases/Chat/`) — the single entry point for all chat |
| A new chat transport (not bancho packet, not IRC TCP) | Implement the `IIrcConnection` interface (`Application/Sessions/Irc/`) — the new class receives `IrcMessage` and encodes it into the corresponding format |
