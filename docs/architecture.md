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
| `Users` | Accounts: `Name`/`SafeName`, `PwBcrypt`, `Privilege`, `Country`, `SilenceEnd`. `Id = 1` seeded for `BanchoBot`. Trimmed to fields actually read back somewhere — no clan/preferred-mode/play-style/custom-badge/userpage columns (dead weight ported from bancho.py, no reader anywhere; clans/public profiles are out of scope, see [`working-scopes.md`](working-scopes.md)) |
| `Mapsets` | One row per beatmapset: `Artist`/`Title`/`Creator`/`Status`/`LastUpdate`/`CreatedAt` — kept live by `BeatmapWatcherService` reconciling `StorageOptions.MapsetsPath` (a per-set folder `"{Id} {Artist} - {Title}"` holding the full original `.osz` contents), not just a bare FK anchor. No osu!api staleness tracking (offline server, mapsets added only through local ingestion) |
| `Beatmaps` | One row per difficulty, keyed by `Md5`, FK'd to `Mapsets` via `MapsetId` (`on delete cascade` — deleting a mapset drops its beatmaps automatically); source for score-submission map lookup and locally-computed star rating display. Also carries `BackgroundFile` (the mapset folder's background image filename, resolved by `GET /beatmapsets/{mapsetId}/{beatmapId}/background`, never itself serialized in any API response — `[JsonIgnore]` on `Beatmap.BackgroundFile`) and `ObjectCounts` (a JSON blob of per-mode hit-object counts, e.g. `{"circle":120,"slider":45,"spinner":2}`, computed once at ingestion via `IOsuCalculator.Analyze` alongside star rating). `Frozen` (C# `Beatmap.IsFrozen`) hides a row from every client-reachable lookup while keeping it in the DB — admin-only |
| `Channels` | Static chat channel catalog (`#osu`, `#lobby`, ...) with `ReadPriv`/`WritePriv`/`AutoJoin` flags |
| `Relationships` | `(User1, User2)` pairs of type `friend`/`block` |
| `ClientHashes` | Hardware fingerprint log (`OsuPathMd5`/adapters/uninstall id/disk serial) per login, `LastSeenAt`/`Occurrences` — used only for anticheat, no automatic blocking logic |
| `IngameLogins` | Login log: IP, client version, stream, `LoggedInAt` — write-only, no consumer reads it back |
| `Logs` | General action log `FromId`/`ToId`/`Action` (e.g. moderation), `CreatedAt` — write-only |

Tables important for the **tournament flow**:

| Table | Purpose |
| --- | --- |
| `Matches` | One row per multiplayer room. `Id` is the stable ID external consumers use — distinct from `MatchSession.Id`, the in-memory 0-63 slot that the bancho wire protocol itself uses. Stores only `Name`, `CreatedAt`, `EndedAt` — no Mode/WinCondition/TeamType/HostId (those moved per-round into `Rounds`) |
| `Rounds` | One row per beatmap played within a match, created at `MATCH_START`/`!mp start`. Carries per-round `Mode`, `WinCondition`, `TeamType`, `Aborted` flag, `Mods`, and only `MapMd5` for beatmap identification — no denormalized `BeatmapArtist`/`Title`/`Version`/`Creator` (removed; every other beatmap fact is resolved live at report-build time by looking up that md5 through the cached `IMapRepository`, `null` if it no longer resolves — a beatmap that changed content since, or was deleted, with no historical-label fallback) |
| `Scores` | Links to a `Round` via `RoundId`, submitted through the existing `osu-submit-modular-selector.php` pipeline. New `SubmittedAt` = server wall clock when the score arrived (not `ClientTime`). No `IsInvalidated` column — a score's stored `MapMd5` failing to resolve via `IMapRepository` (surfaced as `beatmap: null` on the API response) is the equivalent signal, computed live instead of stored |
| `MatchEvents` | Lifecycle audit log: `EventType` (0=Created…7=Closed), optional `ActorUserId`/`TargetUserId`, `Detail` text. Written by `MatchMembershipService`, `MpCommandService`, packet handlers, and `MatchRecoveryService` |
| `UserStats` | `Tscore`/`Rscore`/`Plays`/`Acc`, seeded once at zero, never updated by score submission — this server has no singleplayer ranking/progression; per-mode stats are static display data, not computed. Trimmed to the 4 columns actually read at login (`Playtime`/`MaxCombo`/`TotalHits`/`ReplayViews`/grade-count columns were dead — the only code that would have consumed them, `ScoreStatsCalculator`, was itself never called from anywhere) |
| `Counters` | `(Name, Value)` running totals kept in sync by SQLite triggers on `Mapsets`/`Scores` inserts, deletes, and `IsPrivate` transitions (`Mapsets:Total`, `Mapsets:Public`, `Scores:Total`) — backs `GET /beatmapsets`'s and `GET /scores`'s `meta.totalRecords` without a per-request `COUNT(*)`. `GET /matches` has no counter row — its list is already fully materialized in memory from `IMatchRegistry` merged with persisted rows, so `meta.totalRecords` is just that list's count |

> [!IMPORTANT]
> **Score-to-round linking has no race window by design**
>
> `MatchMembershipService.StartAsync` creates the `Round` row and stores its id in `MatchSession.CurrentRoundId` *before* gameplay begins. Score submission (an HTTP request) and `MATCH_COMPLETE` (a bancho packet) arrive on two unrelated connections with no ordering guarantee between them — so score submission reads `CurrentRoundId` at submit time rather than having a "collect scores after match ends" step that must wait for both.

## Tournament match report (TRT)

**TRT is never stored in the Database** — `MatchReportService` (`UseCases/Multiplayer/`) builds it at read time from `Matches`/`Rounds`/`Scores` (for a finished match) merged with the live `MatchSession` (for an in-progress match, looked up via `IMatchRegistry.GetByDbId`).

`WinningTeam` is also computed at read time, not stored: completed `Scores` are grouped by team if non-neutral teams exist, otherwise falls back to the highest individual score.

`GET /matches/{matchId}` itself is plain JSON only (the full TRT above). Live pushes are on dedicated, always-SSE `.../live` sibling paths — every match sub-resource is a **JSON-only base path plus a `.../live` SSE sibling on the same resource**, never one path branching on `Accept` (each is a separate route, so OpenAPI/codegen tooling sees one unambiguous shape per operation instead of "JSON or SSE depending on a header"). All SSE channels follow the same convention: the first event on a connection is a full snapshot, every event after that is an RFC 7396 JSON Merge Patch against the previous one (computed per-connection, not globally — a client that just connected always gets a full snapshot regardless of what earlier clients already received). A `.../live` route on a match that isn't currently live returns a `409` enveloped JSON error instead of ever opening the stream.

| Endpoint | Purpose |
| --- | --- |
| `GET /matches/{matchId}` / `GET /matches/{matchId}/live` | One-shot TRT JSON / full match live snapshot (slots/host/referees/beatmap/`inProgress`) — this SSE channel absorbed the old separate "currently playing" status channel, there's no smaller variant anymore |
| `GET /matches/{matchId}/settings` / `.../settings/live` | Room-configuration fields (name/password presence/size/map/mods/team type/win condition/host/referees) — never the raw password, only `hasPassword` |
| `GET /matches/{matchId}/live/{slotIndex}` | Always-SSE, no JSON sibling (there's no meaningful one-shot form of "whoever is currently in this slot") — multiplexes `slot`/`score`/`input` events for whoever currently occupies the slot, so an occupant change takes effect on the next event with no reconnect needed |
| `GET /matches/{matchId}/hosts`, `/refs`, `/ban`, `/timer`, `/slots` (+ each one's `.../live`) | One resource per match sub-resource — current host, referee list, ban list, countdown timer state, and the full 16-slot **index-addressed list** (not a dict), respectively — each mutated by its own `MatchControlService` method and its own admin-key-gated route (`PUT` full replace / `PATCH` partial, both required-vs-optional fields reflected in their own distinct request record types). `/slots` also carries `POST` (invite a player onto it) and `DELETE` (kick a seated player) — both moved here from the old standalone `/invite` and `/kick` actions |
| `GET /users/{idOrName}/live` ("Spectate User") | Decoded replay-frame bundles for one player (keyed by `Users.Id`, not match id — a rename of the old `/spec/{id}`), published from `SpectateFramesHandler` any time that player is logged in — BasilBot spectates every player from login onward specifically so this channel always has a source. The bancho-protocol relay to native spectators stays a raw, unparsed forward (matching the ported Python source's "fastpath" comment) — the same bytes are independently decoded a second time (`BanchoPacketReader.ReadReplayFrameBundle`) only for this SSE payload, into button state / cursor position per frame plus the trailing scoreframe; a malformed bundle only skips this publish, never the native relay |

Every `/matches/{matchId}*` channel publishes through `IMatchLiveEvents`; `/users/{idOrName}/live` publishes through the player-scoped `IPlayerInputEvents`. Both are plain C# events — `Publish*` just raises the event, and each SSE connection's own subscriber does a non-blocking `ChannelWriter.TryWrite` into its own buffer — safe to call from code still holding `MatchSession.Lock` (as `EnqueueState` and the score/spectate handlers do), since the actual response writes happen on a per-connection pump, fully decoupled from the publish call. `abort`/`close` remain plain `POST /matches/{matchId}/{action}` routes (no SSE channel of their own, no sub-resource CRUD shape to attach to) and now return `200` with a body (the post-abort live snapshot / a close confirmation) instead of a bare `204`. All of the above route through the shared `MatchControlService` that `!mp` chat commands also call. In the generated OpenAPI/Scalar docs, each sub-resource keeps its own tag (so `hosts`/`refs`/`ban`/`timer`/`slots` are each their own documented section, and `.../live` siblings share their base resource's tag rather than getting one of their own) but all `Matches`-prefixed tags are grouped together in the sidebar via the `x-tagGroups` extension — grouping is by resource, never by whether a route happens to support SSE.

## `api.` host response conventions

**Enveloped Response Standard.** Every JSON response on the `basilapi` OpenAPI document (i.e. every route under the `api.` subdomain, except file downloads and SSE) is wrapped by `Basil.Web/Middleware/EnvelopeMiddleware` into `{ success, code, message, data, meta, errors, timestamp }`:

- `success`/`code` mirror the actual HTTP status (`success = code < 400`); `message` is a generic method-derived string on success (`"Created successfully"`, `"Retrieval successful"`, ...) or the original error body's `error`/`detail`/`title` field (falling back to the HTTP reason phrase) on failure.
- `data` is the original response body — or, for a paginated list route, just the `items` array, with `page`/`pageSize`/`totalRecords` promoted into a sibling `meta: { page, pageSize, totalRecords, totalPages }` object (`totalPages = ceil(totalRecords / pageSize)`). Detected structurally (an exact 4-key `{page,pageSize,totalRecords,items}` match), not via a per-route marker.
- Every route that used to return `204 No Content` now returns `200 OK` with `data: null` instead — 204 can't carry a body at all, and the standard has no second bodyless exception.
- The middleware buffers the response (swaps `Response.Body` for a `MemoryStream`, rewrites after `next()` completes) — skipped entirely (no buffering, passed straight through) for a route tagged `Basil.Web/Middleware/SseEndpointMarker`. Every SSE route on this host is now a dedicated, unconditionally-SSE `.../live` path (see the TRT section above) — no route branches on the `Accept` header anymore, so the marker check alone is sufficient. Buffering a live push stream until the handler completes would otherwise silently turn it into one that never delivers a single event until the connection closes. Any non-2xx status on that same route (a synchronous error returned *before* a stream opens — 409 "not live", 404 out-of-range, 400 no-stream-to-expose) is still a genuine JSON body and hand-envelopes itself instead (`Basil.Web/Routing/LiveSseRoutes.SseError`/`NotLive`), since the middleware can't buffer-and-rewrite it without risking the buffering skip above.
- `Basil.Web/OpenApi/EnvelopeBuilder` holds the actual envelope-construction logic (`DescribeSuccess`/`DescribeError`/paged-shape detection/`meta` building) shared verbatim by `EnvelopeMiddleware` (the real runtime response) and `Basil.Web/OpenApi/OpenApiExampleExtensions.WithExample` (a route's `.WithExample(...)` fake-data payload) — previously two independent copies that had to be kept in sync by hand. `WithExample` skips the envelope wrap the same way the middleware does, for an SSE route's own 2xx status only — any other status on that route is still wrapped, matching the middleware's own per-status behavior above.
- **The declared OpenAPI *schema*, not just the example, is also enveloped.** `Basil.Web/OpenApi/EnvelopeSchemaTransformer` rewrites every basilapi response schema to `allOf: [Envelope, { data: T }]` — `Envelope` is a single shared component holding the six non-`data` fields (`success`/`code`/`message`/`meta`/`errors`/`timestamp`, `meta`/`errors` themselves `$ref`ing shared `PageMeta`/`FieldError` components), combined via `allOf` instead of re-declaring all seven properties inline on every response (the previous inline-per-operation version made the generated document thousands of lines longer than it needed to be). `T`'s schema is reused from the framework's own default per-operation generation (`jsonMediaType.Schema`, captured before this transformer runs) rather than resynthesized — resynthesizing it (e.g. via `GetOrCreateSchemaAsync` inside the transformer) does not participate in the framework's `$ref`-promotion pipeline and silently re-inlines every nested type instead. SSE routes are excluded the same per-status way as above (their own 2xx only). `Basil.Web/OpenApi/SecuritySchemeTransformers` declares `X-Admin-Key` as a real `apiKey` security scheme and attaches it to every operation that carries `RequireAuthorization(AdminKeyDefaults.Policy)`, so Scalar's Authorize button and generated SDKs pick it up instead of it being prose-only in each route's `.WithDescription`.
- **Enum wire convention.** A field that's conceptually an enum stays typed as that enum on the C# record (`UserPrivileges`, `Mods`, `GameMode`, `MatchTeam`, `SlotStatus`, `Grade`, `RankedStatus`, `MatchEventType`, ...) — never swapped for a bare `int`/`string` — and serializes to a plain number by System.Text.Json's default enum handling (no `JsonStringEnumConverter` anywhere). `Country` is the one exception: it serializes to its 2-letter lowercase acronym (`"vn"`, `"xx"`) via `CountryJsonConverter`, registered globally in `Program.cs` and shared with the SSE pipeline's `BasilJsonOptions`. `Beatmap.TotalLength` stays a domain `TimeSpan` throughout — only the API-facing `BeatmapView.TotalLength` carries a property-level `TimeSpanSecondsJsonConverter` so the wire form is a plain integer number of seconds, not `TimeSpan`'s default `"hh:mm:ss"` string.
- **PUT vs PATCH request shapes are genuinely distinct types**, not one shared all-optional record: a `ReplaceXRequest` (PUT) has every field required with no default value, a `UpdateXRequest` (PATCH) has every field `T?` *with* a `= null` default — the default is what makes the generated OpenAPI schema actually mark the field non-required (a nullable record parameter without a default is still schema-required in ASP.NET Core's generator; only nullable-with-default is treated as omittable).

**Consistent embedding, not bare ids.** Every user reference in a `basilapi` response embeds `{ id, name, country }` via one reused `UserBrief` record (`MatchLiveSnapshotBuilder.cs`, `Country` typed as the `Country` enum per the wire convention above) — never a bare `userId`. `UserBriefResolver` resolves it by trying the in-memory `IPlayerSessionRegistry` first (online players, no DB hit), falling back to the cached `IUserRepository` for offline ids; genuinely unresolvable ids in most contexts fall back to a placeholder (`"Unknown"`/`Country.Xx`) rather than being silently dropped from a list, reserving an actual `null` field for structural absence (no host, empty slot). Every beatmap reference embeds a `BeatmapView`-derived record (`BeatmapDetail`, which nests the parent beatmapset as `BeatmapsetSummary`; or `BeatmapInSet`, used only inside a `BeatmapsetDetail.Beatmaps[]` list to avoid embedding a beatmapset inside its own children) rather than the bare domain `Beatmap` record — keyed by looking up the relevant stored `Md5` (never `Id`, since a beatmap's id can persist across a re-ingestion that changes its content, but its md5 can't) through the same cached `IMapRepository`; `null` once that md5 no longer resolves (the beatmap changed or was deleted since). Domain-internal naming stays `Mapset`; only the API-facing type name is `Beatmapset`.

**In-memory read-through caching.** `Basil.Infrastructure/Caching/` (`CachingUserRepository`, `CachingMapRepository`, `CachingMapsetRepository`) wrap the real `Sqlite*Repository` implementations behind the same interfaces, backed by `Microsoft.Extensions.Caching.Memory.IMemoryCache`. Each hot single-row lookup (`User` by `Id`; `Beatmap` by `Id`/`Md5`; `Mapset` by `Id`) is cached with a TTL *and* is explicitly invalidated on every write to that row — the TTL is a staleness/memory safety net, not a substitute for immediate invalidation. List/search methods pass through uncached (only the single-row lookups this embedding convention introduces N+1 risk for need it). This is what makes embedding a `UserBrief`/`Beatmap` on every list item cheap instead of reintroducing an N+1 query per response.

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
> **Passwords:** IRC PASS requires the **account password** (same as osu! client login) — unlike official osu!Bancho (irc.ppy.sh uses a separate password "different from your account password").

## BanchoBot handler

`BanchoBot` (`UseCases/Bot/BotBootstrapService`) is bootstrapped as a real `PlayerSession` at startup — it has no client connection behind it, so it is exempted from `GhostDisconnectService`'s reap sweep via `PlayerSession.IsBot` (it never sends real ping packets, so `LastRecvTime` would never advance without this exemption).

`SendPublicMessageHandler`/`SendPrivateMessageHandler` forward every message to `ChatDispatchService.SendPrivmsgAsync`, which routes messages starting with `!` to `ICommandDispatcher` — the dispatcher routes to either the general command table (`!help`, `!roll`) or `MpCommandService` for `!mp <subcommand>` — the latter only when the message is sent in the match's own chat channel and the sender passes `MatchSession.IsReferee`.

Bot replies are broadcast via `ChannelMembershipService.BroadcastPrivmsg` (IRC-shaped) rather than building packets directly — so real IRC clients in the channel also see BasilBot's responses.

## Logging

Serilog is wired entirely in code (`Basil.Web/Program.cs`'s `ConfigureSerilog`, called first thing in `Main`, before any other startup logic) — no `Serilog.Settings.Configuration` package, no standard ASP.NET Core `Logging` section. The one configurable knob is the minimum level for stdout + the full file, read from `Basil:Logging:MinimumLevel` in `appsettings.json` (any `Serilog.Events.LogEventLevel` name, defaults to `Information`) — the errors file always stays fixed at Error+ regardless. Two sinks besides stdout, both plain-text, both daily-rolling with 30-day retention:

| Sink | Path | Minimum level |
| --- | --- | --- |
| Full | `Logs/full/basil-<date>.log` | Information |
| Errors | `Logs/errors/basil-<date>.log` | Error (and Fatal) |

Each sink also maintains a fixed pointer file — `Logs/latest.log` / `Logs/errors_latest.log` — that's a **hardlink** (not a copy) to whichever dated file is currently open, recreated via a custom `Serilog.Sinks.File.FileLifecycleHooks` (`Basil.Web/Logging/HardLinkFileLifecycleHooks`) every time the sink rolls to a new file. The actual hardlink syscall (`CreateHardLinkW`/POSIX `link()`) lives in `Basil.Infrastructure/Logging/HardLink.cs`.

**Scopes** — correlation ids pushed via `Serilog.Context.LogContext`/`ILogger.BeginScope`, rendered through the template's `{Properties}` token so they only appear on lines that actually carry them:

| Scope | Where it's pushed | Notes |
| --- | --- | --- |
| `RequestId` | `RequestIdLoggingMiddleware`, first middleware in the pipeline | `HttpContext.TraceIdentifier`, wraps the whole request including `UseSerilogRequestLogging()`'s own summary line |
| `RemoteIp` | Same middleware | Resolved via `Geolocation.PhraseIpAddress` (proxy headers), not the raw Kestrel connection address |
| `UserId` / `PacketType` / `MatchId` | `BanchoPacketDispatcher.DispatchAsync`, one push per packet | `MatchId` only when `player.Match` is set; 3 handlers that resolve a match without touching `player.Match` (`TourneyMatchInfoRequestHandler`/`JoinChannel`/`LeaveChannel`) push it separately |
| `MatchId` + `Subcommand` | `MpCommandService.TryHandleAsync` | Uses the *effective* target match (correct for `!mp in`-scoped referees), overriding the dispatcher's physical-match push when they differ |
| `ScoreId` | `ScoreSubmissionService.SubmitAsync`, right after the DB insert | Every earlier failure branch returns before an id exists, so it logs plain properties instead |
| `ConnectionId` | `TcpIrcConnection.RunAsync` | IRC only — the bancho binary protocol is HTTP long-poll (a fresh request per poll, no held socket), so it has no connection-lifetime concept of its own; `RequestId`+`UserId` cover that case instead |

**Fixed categories** — a `Category` property added by `Basil.Web/Logging/CategoryEnricher` based on `SourceContext` (the class name `ILogger<T>` already carries), not a per-call push:

| Category | Matches |
| --- | --- |
| `Database` | Any `Basil.Infrastructure.Persistence.Repositories.*` class |
| `Cache` | Any `Basil.Infrastructure.Caching.*` class |
| `Background` | Exactly `BeatmapWatcherService`/`MapsetGarbageCollectorService` (not the whole `Beatmaps` namespace) |
| `Host` | `Basil.Web.Program` and the framework's own `Microsoft.Hosting.Lifetime` — server startup/shutdown |

Business-event logging follows one rule to avoid triple-logging: for multiplayer, log inside the shared `MatchMembershipService`/`MatchControlService` methods, not in the bancho packet handler / `!mp` subcommand / HTTP route that calls into them — all three surfaces converge on the same service call.

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
