# Scoping

**Basil** is a multiplayer tournament server built on top of **bancho.py**, not a full osu! server.

This page lists what is *in scope*, what is not, and the rationale for each exclusion.

## Chat commands

Dispatched by `ICommandDispatcher`/`CommandDispatcher` (general commands) and `MpCommandService` (`!mp` subcommands), both under `UseCases/Bot/`. The full command list and usage is on the BasilBot Commands page (`api.<domain>/basilbot`, or the same page on GitHub Pages) — not duplicated here to avoid drift between two sources.

## IRC Gateway

**In scope**: Basil runs an embedded IRC gateway (port 6667, `TcpIrcListener`/`TcpIrcConnection`) allowing real IRC clients (or tournament tools like osu-ahr) to connect and chat/`!mp` alongside osu! clients. A unified chat core through `ChatDispatchService` + `ChannelMembershipService.BroadcastPrivmsg` — messages from IRC clients reach osu! clients and vice versa, since every `PlayerSession` has an `IIrcConnection` (`BanchoIrcBridgeConnection` for osu! clients, `TcpIrcConnection` for IRC clients).

## Out of scope

| Category | Description | Rationale |
| --- | --- | --- |
| ❌ `!pool` + `!mp loadpool/unloadpool/ban/unban/pick` | Full tournament mappool system (7 `!pool` commands, 4 `!mp` subcommands) | Requires new persistence layer (`tourney_pools`/`tourney_pool_maps`) not present |
| ❌ Scrim engine (`!mp scrim`/`autoref`/`endscrim`/`rematch`) | Race-safe match-point tallying for auto-refereed scrims | Engine (`MatchScoringService`) was removed along with the old command layer; not requested for rebuild |
| ❌ `!mp force` | Admin force player into match | Not implemented |
| ❌ `!block`/`!unblock`/`!reconnect`/`!changename`/`!apikey` | Personal chat commands (block user, rename, manage API key...) | Outside multiplayer/tournament scope |
| ❌ ApiKey (User field + `UpdateApiKeyAsync`) | `ApiKey` field on User and corresponding repository method — dead code (IRC uses osu! password directly, no separate key needed) | Removed — never used |
| ❌ Friends (`osu-getfriends.php`, `FriendAddHandler`/`FriendRemoveHandler`) | HTTP endpoint + packet handlers for friend relationships | Social feature, outside multiplayer/tournament scope |
| ❌ General-purpose public JSON API v1/v2 | REST API like osu-web (OAuth, rate limiting, versioning) for external tools | No concrete demand; users expected to build their own later |
| ❌ Moderation/clan/metrics (`!clan`, admin commands, Discord webhook, Datadog) | Clan/moderator commands, audit-log webhook, `bancho.online_players`/`bancho.login_time` metrics | Depends on removed command layer; project doesn't use Datadog so nothing to port |
| ❌ Debug HTML pages (`/matches`, `/online`) | Admin-facing debug views from Bancho | Never called by game client, explicitly dropped |
| ⚠️ `osu-screenshot.php` | Screenshot upload | Stub — returns 400 "not available" |
| ⚠️ `osu-getfavourites.php`/`osu-addfavourite.php` | Favourite beatmap list | Stub — returns empty |
| ⚠️ `osu-rate.php` | Beatmap star rating | Stub — returns `"not ranked"` (real Bancho response code) |
| ⚠️ `osu-comment.php` | In-game comments | Stub — returns empty |
| ✅ `POST /users` (in-game registration) | Create account via game client — Email field must match `Server:AdminKey` | Active — accepts registration when AdminKey is configured |
| ❌ pp calculation | Performance points for scoring/leaderboard/win conditions | Deliberately absent — star rating is display-only, computed via ppy's osu!lazer ruleset |

## Lessons from reversed decisions

| Decision | Problem | Resolution |
| --- | --- | --- |
| Removed `BanchoBot` + entire chat command layer (including full 25 `!mp` subcommands) when pivoting to "multiplayer + tournaments only" | Tournaments still need chat-based match control (`!mp start`, map change, slot management...) — removing everything was overcorrection | `BanchoBot` re-bootstrapped as a real session (`BotBootstrapService`); fresh dispatch layer (`ICommandDispatcher`/`CommandDispatcher`/`MpCommandService`) wraps existing `MatchSession`/`MatchMembershipService` mutations, narrower than the original command set |
| Deferred "API v1/v2 later" indefinitely | Tournaments need live match tracking (reports, WebSocket) even without a full public API | `api.<domain>` host built for tournament match reports (TRT) via `GET`/WebSocket, replay/beatmap downloads, and admin-key-gated management CRUD — narrower than public v1/v2 (no OAuth, no rate limiting, no versioning); general API not yet built |
| Automatic test-parity plan ("run Bancho and Basil in parallel, compare results") | No longer viable once most of Bancho's feature surface was deliberately cut — nothing left to compare in parallel | Manual single-thread multiplayer/tournament testing with two real osu! clients — see [`getting-started.md`](getting-started.md) |
