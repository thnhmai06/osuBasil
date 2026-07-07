# API reference

Everything the game client actually talks to: bancho packets over the persistent binary connection, and the `/web/osu-*.php`-style HTTP endpoints it calls alongside that connection. For what's stubbed vs. real and why, see [`scope-decisions.md`](scope-decisions.md).

## Where endpoints are hosted

All endpoints below — bancho protocol, osu-web, beatmap assets, api — are one single ASP.NET Core app (`Bancho.Web`), routed apart by **hostname, not path prefix**. `BanchoHostGroups.MapAll` (`src/Bancho.Web/Routing/BanchoHostGroups.cs:33`) builds the actual host list at startup from one config value:

```
ServerBehavior:Domain      (appsettings.json)
ServerBehavior__Domain     (environment variable — used in docker-compose.yml)
```

For that configured domain **and** the hardcoded `ppy.sh` (so a reverse-proxied production deployment answering on the real osu! domains works without extra config), it generates:

| Host group | Hostnames | Route group |
| --- | --- | --- |
| bancho protocol | `c.<domain>`, `ce.<domain>`, `c4.<domain>`, `c5.<domain>`, `c6.<domain>` | `MapBanchoGroup` |
| osu-web | `osu.<domain>` | `MapOsuWebGroup` |
| beatmap assets | `b.<domain>` | `MapBeatmapAssetGroup` |
| api | `api.<domain>` | `MapApiGroup` |

`Program.cs:20` reads `ServerBehavior:Domain` and passes it into `MapAll` — that's the only place the domain is threaded through; there's no per-route host config anywhere else.

**Where that config value itself lives, per environment:**

| Environment | Where `ServerBehavior:Domain` (and everything else) is set |
| --- | --- |
| `dotnet run` (local dev) | `src/Bancho.Web/appsettings.Development.json` → `ServerBehavior:Domain` (defaults to `bancho.local`) |
| `docker compose up` (production compose) | `docker-compose.yml` → `app.environment.ServerBehavior__Domain` (defaults to `${BANCHO_DOMAIN:-localhost}`, override via a `BANCHO_DOMAIN` env var on the host) |

**Network binding (which port/protocol the app actually listens on) is a separate, unrelated config axis:**

- Local dev: `Kestrel:Certificates:Default:Path`/`Password` in `appsettings.Development.json` (HTTPS cert for real-client testing, see [`getting-started.md`](getting-started.md)), combined with `--urls` passed to `dotnet run` (e.g. `--urls "http://*:80;https://*:443"`).
- `docker-compose.yml`: the `app` service publishes `8080:8080`, and `ASPNETCORE_URLS=http://+:8080` is baked into the `Dockerfile`'s runtime stage — plain HTTP only, meant to sit behind a real reverse proxy (nginx/Caddy) that terminates TLS and forwards `X-Forwarded-For`.

## Bancho packets

42 client packet handlers, registered in `Bancho.Application`'s DI container and counted by `CompositionRootTests`. Grouped by the same folder split the code uses under `PacketHandlers/`.

### Core (session lifecycle)

| Packet | Handler | Purpose |
| --- | --- | --- |
| `Ping` | `PingHandler` | keepalive, no-op reply |
| `ChangeAction` | `ChangeActionHandler` | update own status (action/mode/map/mods) |
| `RequestStatusUpdate` | `RequestStatusUpdateHandler` | client asks the server to resend its own stats |
| `UserStatsRequest` | `UserStatsRequestHandler` | request stats for a specific set of user IDs |
| `UserPresenceRequest` | `UserPresenceRequestHandler` | request presence for a specific set of user IDs |
| `UserPresenceRequestAll` | `UserPresenceRequestAllHandler` | request presence for every online player |
| `ReceiveUpdates` | `ReceiveUpdatesHandler` | set the presence-filter (all/friends/none) |
| `SetAwayMessage` | `SetAwayMessageHandler` | set/clear the away message shown to others |
| `Logout` | `LogoutHandler` | clean disconnect: leaves channels/match, notifies others |

### Channels (chat)

| Packet | Handler | Purpose |
| --- | --- | --- |
| `ChannelJoin` | `ChannelJoinHandler` | join a chat channel |
| `ChannelPart` | `ChannelPartHandler` | leave a chat channel |
| `SendPublicMessage` | `SendPublicMessageHandler` | send a message to a channel |
| `SendPrivateMessage` | `SendPrivateMessageHandler` | send a DM to another player |
| `ToggleBlockNonFriendDms` | `ToggleBlockNonFriendDmsHandler` | toggle whether non-friends can DM you |

### Spectating

| Packet | Handler | Purpose |
| --- | --- | --- |
| `StartSpectating` | `StartSpectatingHandler` | begin spectating a player |
| `StopSpectating` | `StopSpectatingHandler` | stop spectating |
| `SpectateFrames` | `SpectateFramesHandler` | forward replay frames to fellow spectators |
| `CantSpectate` | `CantSpectateHandler` | notify the host the spectator doesn't have the map |

### Multiplayer

| Packet | Handler | Purpose |
| --- | --- | --- |
| `CreateMatch` | `CreateMatchHandler` | create a match, join as host |
| `JoinMatch` | `JoinMatchHandler` | join an existing match |
| `PartMatch` | `PartMatchHandler` | leave the current match |
| `MatchChangeSlot` | `MatchChangeSlotHandler` | move to a different slot |
| `MatchChangeSettings` | `MatchChangeSettingsHandler` | change map/mode/win-condition/team-type |
| `MatchChangePassword` | `MatchChangePasswordHandler` | change the match password |
| `MatchChangeMods` | `MatchChangeModsHandler` | change own or match-wide mods |
| `MatchChangeTeam` | `MatchChangeTeamHandler` | switch team (team modes only) |
| `MatchLock` | `MatchLockHandler` | lock/unlock a slot (host only) |
| `MatchTransferHost` | `MatchTransferHostHandler` | transfer host to another slot |
| `MatchReady` | `MatchReadyHandler` | mark self ready |
| `MatchNotReady` | `MatchNotReadyHandler` | mark self not ready |
| `MatchStart` | `MatchStartHandler` | start the match (host only) |
| `MatchLoadComplete` | `MatchLoadCompleteHandler` | signal gameplay load finished |
| `MatchSkipRequest` | `MatchSkipRequestHandler` | request skipping the intro |
| `MatchNoBeatmap` | `MatchNoBeatmapHandler` | signal missing beatmap |
| `MatchHasBeatmap` | `MatchHasBeatmapHandler` | signal beatmap now available |
| `MatchFailed` | `MatchFailedHandler` | signal a fail during play |
| `MatchScoreUpdate` | `MatchScoreUpdateHandler` | forward live score-frame updates |
| `MatchComplete` | `MatchCompleteHandler` | signal gameplay finished for this slot |
| `MatchInvite` | `MatchInviteHandler` | invite another player to the match |
| `TournamentMatchInfoRequest` | `TourneyMatchInfoRequestHandler` | tourney client: request match info without joining |
| `TournamentJoinMatchChannel` | `TourneyMatchJoinChannelHandler` | tourney client: join the match's chat channel only |
| `TournamentLeaveMatchChannel` | `TourneyMatchLeaveChannelHandler` | tourney client: leave the match's chat channel |

## HTTP endpoints

All routes are registered in one place, `src/Bancho.Web/Routing/BanchoHostGroups.cs` — host-based subdomain routing selects which group handles a request (not a path prefix). The "Handled by" column points to the actual class doing the work; "inline" means the logic lives directly in the route lambda in that file, with no separate service class.

### `c.`/`ce.`/`c4.`/`c5.`/`c6.` — bancho protocol

The client's persistent realtime connection. Every request is a `POST /` — there's no other path on this host group.

| Method | Path | Handled by | What it does |
| --- | --- | --- | --- |
| `POST` | `/` | `MapBanchoGroup` (inline) → `OsuLoginUseCase` (no `osu-token` header) or `BanchoPacketDispatcher` (with `osu-token` header) | No token: treats the request body as a login attempt and hands it to `OsuLoginUseCase`, which returns a `cho-token` header plus the initial packet stream. With a token: looks the session up by it, and if found, forwards the raw body to `BanchoPacketDispatcher.DispatchAsync`, which reads the packet ID and calls the matching handler from the bancho-packets table above; the accumulated outgoing packets for that session are then flushed as the response body. An unknown token (server restarted since the client's last request) gets a "Server has restarted" notification + reconnect packet instead. |

### `osu.` — osu!-web endpoints (`/web/*.php`, `/d/*`, `/users`, `/difficulty-rating`)

Everything the client's web-request layer calls outside the persistent bancho connection — beatmap lookups, score submission, replay download, and a handful of deliberately inert stubs.

| Method | Path | Handled by | What it does |
| --- | --- | --- | --- |
| `POST` | `/` | `MapOsuWebGroup` (inline) | placeholder response `"osu"` for a bare request to the host — not a real client-facing endpoint |
| `GET` | `/web/osu-osz2-getscores.php` | `BeatmapLeaderboardService` | fetches the leaderboard for one beatmap (score list, ranked status), scoped by leaderboard type (global/country/friends/mods) |
| `GET` | `/web/osu-search.php` | `DirectSearchService` | beatmap search from the in-game "osu!direct" panel; queries the local `maps` table, no mirror/internet dependency |
| `GET` | `/web/osu-search-set.php` | `IMapRepository.FetchOneAsync` (inline) | looks up a single beatmap set by set ID, map ID, or checksum (exactly one is expected per request) |
| `GET` | `/d/{mapSetId}` | `MapBanchoGroup` (inline, via `MirrorOptions`) | beatmap set download — redirects to a configured mirror if `MirrorOptions.DownloadEndpoint` is set; otherwise reports downloads as unavailable (no local `.osz` storage) |
| `GET` | `/web/maps/{mapFilename}` | inline stub | always reports the map file as unavailable — no local `.osu` file storage, and (unlike bancho.py) no real-osu.ppy.sh fallback redirect either |
| `POST` | `/web/osu-submit-modular-selector.php` | `IScoreDecryptor` + `ScoreSubmissionUseCase` | the actual score-submission endpoint: decrypts the client's score payload, extracts the replay file from the multipart form if present, then runs the full submission pipeline (validation, persistence, response formatting) |
| `GET` | `/web/osu-getreplay.php` | `ReplayService` | fetches a stored replay file by score ID, for in-game replay watching |
| `POST` | `/web/osu-getbeatmapinfo.php` | `BeatmapInfoService` | given a list of `.osu` filenames, returns per-map ranked status and the requesting player's grade on each (for the song-select overlay) |
| `GET` | `/web/lastfm.php` | `ClientIntegrityService` | anticheat flag intake — logs a suspicious client-hash flag (see [`scope-decisions.md`](scope-decisions.md)); never restricts or kicks |
| `GET` | `/web/osu-markasread.php` | `MailReadService` | marks an in-game mail conversation with another player as read |
| `GET` | `/web/osu-getseasonal.php` | inline stub | always returns `[]` — no seasonal background-image feature exists |
| `GET` | `/web/bancho_connect.php` | inline stub | empty response; unauthenticated by design (can be called before a session exists), matching bancho.py |
| `GET` | `/web/check-updates.php` | inline stub | empty response |
| `POST` | `/web/osu-screenshot.php` | inline stub | 400 response, "Screenshots are not available on this server." |
| `GET` | `/web/osu-getfavourites.php` | inline stub | empty response |
| `GET` | `/web/osu-addfavourite.php` | inline stub | empty response |
| `GET` | `/web/osu-rate.php` | inline stub | returns `"not ranked"` — a real bancho.py response code, reused rather than invented |
| `POST` | `/web/osu-comment.php` | inline stub | empty response |
| `POST` | `/users` | inline stub | in-game account registration — returns bancho.py's real "registration disabled, use the website" error shape |
| `POST` | `/difficulty-rating` | inline stub | unconditional 307 redirect to `osu.ppy.sh` — bancho.py does the same unconditionally, not a divergence |

### `b.` — beatmap/thumbnail assets

| Method | Path | Handled by | What it does |
| --- | --- | --- | --- |
| `GET` | `/{**path}` | `MapBeatmapAssetGroup` (inline) | catch-all: 301-redirects every request straight through to `https://b.ppy.sh{path}` (thumbnails, previews) — no local asset storage |

### `api.` — public developer API

| Method | Path | Handled by | What it does |
| --- | --- | --- | --- |
| `GET` | `/` | `MapApiGroup` (inline) | placeholder response `"api"` only — the v1/v2 JSON API itself is not implemented, deferred indefinitely (see [`scope-decisions.md`](scope-decisions.md)) |
