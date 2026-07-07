# Scope decisions

bancho-net started as a full port of bancho.py, then was deliberately narrowed mid-project: *"I want to build a system that only serves multiplayer and tournaments, so peripheral features need to be removed."* This page is the condensed, by-topic record of what that meant in practice — what got cut, what got kept, and why. It supersedes the project's old chronological working log.

## Dropped entirely

### Chat commands + BanchoBot + scrim engine

The entire in-game chat command system was built (`!help`, `!roll`, `!block`, `!unblock`, `!reconnect`, `!changename`, `!apikey` from the original port, plus a full `!mp`/`!pool` multiplayer command set — 20 of 25 `!mp` subcommands, a `MatchScoringService` scrim engine with race-safe match-point tallying, and the `BanchoBot` session commands reply from) — then removed in full, by explicit request, once the user decided command handling should be reimplemented later on their own terms rather than carried over from bancho.py's design.

**Why remove `BanchoBot` too:** its only reason to exist was to give command replies and PM auto-responses somewhere to originate from. With commands gone, the bot session was dead weight.

**Why remove the scrim engine too:** its only entry point was `!mp scrim`, and its only feedback channel was `SendBot` — both gone with the bot and the command layer. Keeping a feature with no way to trigger or observe it isn't useful; it can be rebuilt when commands come back.

**What survived, because it isn't a chat command:** the packet-level multiplayer protocol itself (create/join/part match, all `MATCH_*` slot-management packets, referee and tourney-client tracking via `MatchSession.Referees`/`IsReferee`/`TourneyClients`) — these are real bancho packets the client sends directly, not something typed into chat. The `BanchoBot` seed row (`id=1`) in the base schema also stayed, since it's harmless data a future bot implementation can reuse.

### Friends

Both the HTTP endpoint (`osu-getfriends.php`) and the two packet handlers that maintained the underlying relationship (`FriendAddHandler`/`FriendRemoveHandler`, writing to the `relationships` table) were removed — a social feature outside the multiplayer/tournament scope.

### Public JSON API (v1/v2)

Deferred indefinitely, not built. This would be a JSON API for *external* tools — a website, a Discord bot, a tournament bracket tracker — never called by the osu! game client itself. With no client-facing dependency on it, it fell outside the immediate scope; the user plans to build it separately later.

### Moderation, clans, background services, metrics (former "Phase 10")

Dropped in full. Most of it cascaded automatically once chat commands were removed (every `!clan`, moderator, and admin command depended on the command layer; `BotStatus` depended on `BanchoBot`). The remaining pieces — a Discord audit-log webhook and Datadog metrics — were confirmed dropped after checking: only two Datadog metrics ever existed in bancho.py (`bancho.online_players`, `bancho.login_time`), and the project doesn't use Datadog, so there was nothing worth porting.

## Partially kept: HTTP endpoints (`/web/osu-*.php` and friends)

Kept as real, working implementations:

| Endpoint | Purpose |
| --- | --- |
| `osu-getbeatmapinfo.php` | per-map grade/leaderboard-position lookup |
| `lastfm.php` | anticheat flag intake — **logs only, does not restrict** (see below) |
| `osu-markasread.php` | mark a mail conversation as read |
| `osu-getseasonal.php` | stub returning an empty list (no seasonal background system exists) |
| `bancho_connect.php` / `check-updates.php` | empty/no-auth responses, matching bancho.py |
| `b.*` subdomain | 301 redirect to `b.ppy.sh` for beatmap/thumbnail assets |

Stubbed (route exists and responds, but does nothing): `osu-screenshot.php`, `osu-getfavourites.php`/`osu-addfavourite.php`, `osu-rate.php`, `osu-comment.php`, `POST /users` (in-game registration — returns bancho.py's real "registration disallowed" response), `POST /difficulty-rating` (redirects to osu.ppy.sh, same as upstream).

**Anticheat, specifically:** bancho.py's lastfm handler can flag, restrict, and force-log-out a suspected cheater. bancho-net keeps only the *detection and logging* half — a flagged client hash is written to the existing `logs` table (same shape Python's `Player.restrict()` used) — and deliberately does not restrict or kick anyone. Building real restriction requires the moderation system, which is out of scope.

## Deferred, not dropped

- **Mappool** (`!mp loadpool/unloadpool/ban/unban/pick`, all 7 `!pool` commands) — needs a whole new persistence layer (`tourney_pools`/`tourney_pool_maps`) that doesn't exist yet. Deferred alongside the rest of the chat command layer.
- **API v1/v2** — deferred, with the user's own words: "I'll do the API later."
- **HTML debug pages** (`/matches`, `/online`) — bancho.py's admin-facing debug views, never called by the game client. Explicitly declined, not started.

## Design decisions that shaped the cut scope

- **No pp calculation, anywhere.** Star rating/difficulty is computed locally for display only (via the vendored `akatsuki-pp-rs` Rust crate); nothing in scoring, leaderboards, or match win conditions depends on pp. `!mp condition`'s old "pp" win-condition branch was dropped for the same reason.
- **Manual real-client testing over automated parity.** The original Phase 11 plan included a "run bancho.py and bancho-net side by side" parity check. That assumption stopped making sense once so much of bancho.py's feature surface was intentionally cut. The user chose to instead manually test a real multiplayer/tournament flow with two actual osu! clients — see [`getting-started.md`](getting-started.md) for what that setup requires.
