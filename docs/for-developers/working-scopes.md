# Scoping

## Overview

Basil is an osu! (stable) backend platform specialized for multiplayer, built on top of **bancho.py**. It is
intentionally **not** a full osu! server.

The server implements the core capabilities that multiplayer needs: accounts, sessions and permissions, rooms, chat,
scores, beatmaps, the client protocols, and the events other programs observe. BasilBot's chat commands and
announcements are built into the server; tournament automation beyond those commands and overlays remain client
features that sign in as ordinary users through the HTTP API. Features from bancho.py are not automatically in scope
simply because they exist upstream.

This page defines:

* what Basil currently supports
* what is deliberately out of scope
* what exists only as a compatibility stub
* why previously considered features were rejected or replaced

When adding a feature, use this document as the scope boundary. A feature being present in bancho.py is not, by itself,
a reason to port it to Basil.

## In scope

### Multiplayer and tournament operation

Basil's primary scope is multiplayer tournament infrastructure, including:

* multiplayer match lifecycle
* player slots and slot state
* teams
* host and referee state
* match access control
* tournament-oriented match operations (driven by clients such as BasilBot's `!mp` commands)
* live match reporting
* SSE-based match updates
* replay and beatmap delivery required by supported workflows

The implementation is built around Basil's multiplayer application services rather than attempting to reproduce the
complete bancho.py feature set.

### Chat and IRC

Chat is in scope because tournament operation depends on chat-based match control.

This includes:

* general, room and spectator channels and private messages
* the embedded IRC gateway

The IRC gateway allows real IRC clients and tools such as osu-ahr to connect alongside osu! clients.

Chat commands (`!mp`, `!help`, `!roll`, `!where`, `!faq`, and the staff commands) and chat announcements (anticheat
warnings, round and room notices) are part of the server, provided by BasilBot: a fixed user (id 1) that nobody can
sign in as or act for, and that is the voice the server speaks with. The command handler and announcers are implemented in `Basil.Bot`; host
integration is still pending. A command is an entry point like IRC: it runs against objects scoped to the connection
of the user who typed it, and the bot only replies. Room countdowns (a delayed start, a plain timer, cancelling it)
are features of the room; BasilBot announces their marks. See the generated BasilBot reference at
`api.<domain>/docs/basil-bot/` for the supported command list.

### HTTP API

The `api.<domain>` host is user-centric: a client signs in with an account's credentials, receives a session token,
and every action it takes is checked against that account's permissions and attributed to it. The API covers what
clients built on Basil need, such as BasilBot, tournament tools and overlays:

* rooms and their full management
* chat channels and private messages
* users, their permissions and restrictions
* match reports, replays and beatmap downloads
* live updates and events through SSE

This is intentionally not a reproduction of osu-web's public API: there is no v1/v2 compatibility and no OAuth.

### Assets host and main-menu banner

`assets.<domain>` hosts the client-facing main-menu banner manifest
(`GET /menu-content.json`, matching `assets.ppy.sh`'s real path and schema) plus the image serving
for menu backgrounds/icon and beatmapset covers, all through ImageSharp.Web. See
[`assets.md`](assets.md).

This deliberately does not replicate `assets.ppy.sh`'s user-profile-cover, team, contest, or
artist/media asset families — Basil has no public profile pages, teams, or contest system for them to
back.

Screenshots uploaded by the osu! client (`osu-screenshot.php`) are in scope: the server stores them and hands back
a link the client opens.

### Account creation and operator console

New accounts receive only the default permissions; being the first account grants no special rights. The first
administrator is created by the operator through a console command. The console is planned for the Host phase in
`Basil.Cli`; it is not implemented yet. The design has no administrator key or bootstrap credentials. Account
registration remains part of the supported game-client workflow, subject to `ServerSettings.LockedCreation`.

The hosts have not been migrated to the new Application contracts, so do not treat the planned console or the
redesigned account-creation rules as already integrated into every transport.

### Friends and private messages

The osu! client's friend list (`osu-getfriends.php`, adding and removing friends) is supported, together with the
setting that accepts private messages from friends only and with blocking: a block refuses private messages,
spectating and invitations (staff who manage the matching area are not bound by a block; see "a block yields to a
higher right" in `AGENTS.md`). A blocked user's messages in general channels are filtered silently per recipient: the
server offers the recipients of a message (Channel.RecipientsAsync) and the transports deliver to them. Players use friends and blocks during tournaments to reach each other and referees, so they stay
even though Basil has no other social features.

### Moderation

Restricting a user (a silence is the restriction of chat and multiplayer), lifting it, adding a note, and a moderation
log that keeps the trace of these actions and of every view of a user's login history. The log is for people; it never
changes a right.

## Out of scope

The following features are deliberately not part of Basil's supported surface.

| Feature                                                                          | Status          | Rationale                                                                                                                                                     |
|----------------------------------------------------------------------------------|-----------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `!pool` and `!mp loadpool/unloadpool/ban/unban/pick`                             | ❌ Out of scope | A complete tournament mappool system requires persistence such as `tourney_pools` and `tourney_pool_maps`, which Basil does not currently have.               |
| Scrim engine: `!mp scrim`, `autoref`, `endscrim`, `rematch`                      | ❌ Out of scope | The race-safe match-point engine (`MatchScoringService`) was removed with the previous command layer and is not part of the current design.                   |
| `!mp force`                                                                      | ❌ Out of scope | Administrative forced-player insertion is not implemented.                                                                                                    |
| `!reconnect`, `!changename`, `!apikey`                                           | ❌ Out of scope | These are personal account commands outside Basil's multiplayer and tournament scope. (`!block` and `!unblock` are in scope.)                                 |
| Multi-account detection, hardware bans, client hash checks, the `Verified` grant | ❌ Out of scope | The server does not judge who owns which account.                                                                                                             |
| Blocking old osu! clients (`DISALLOW_OLD_CLIENTS`)                               | ❌ Out of scope | The server does not decide which client versions may sign in; score submission still checks the version it received at login.                                 |
| Updating a country from the IP at login, or any third-party geolocation service | ❌ Out of scope | The stored country is the source. Only registration through the game client takes a country from the host (proxy header or an offline GeoLite2 file).        |
| PM to an offline user (`mail`, `osu-markasread.php`)                             | ❌ Out of scope | A message to someone offline is not kept.                                                                                                                     |
| Seating or hosting through the API                                                | ❌ Out of scope | Taking a slot or becoming host requires an osu! client; the API manages rooms using the caller's own rights but does not play.                                 |
| `ApiKey` on `User` / `UpdateApiKeyAsync`                                         | ❌ Out of scope | Clients sign in with the account's password and receive a session token; a separate API-key model has no use.                                             |
| osu-web API v1/v2 compatibility and OAuth                                        | ❌ Out of scope | Basil's own user-centric API serves the clients built on it; reproducing osu-web's API or OAuth has no concrete requirement.                                   |
| `!clan` and moderation/clan commands                                             | ❌ Out of scope | These belonged to the removed command surface and are outside the project's current purpose.                                                                  |
| Discord audit webhook                                                            | ❌ Out of scope | No current requirement exists for this integration.                                                                                                           |
| Datadog / bancho metrics such as `bancho.online_players` and `bancho.login_time` | ❌ Out of scope | Basil does not use the corresponding Datadog integration.                                                                                                     |
| `/matches` and `/online` debug HTML pages                                        | ❌ Out of scope | These pages are administrative/debug views that are not consumed by the game client.                                                                          |
| pp calculation                                                                   | ❌ Out of scope | Performance points are deliberately absent. Star rating is display-only and follows ppy's osu!lazer ruleset rather than being used as a Basil scoring engine. |

## Compatibility stubs

Some legacy client endpoints exist only because the osu! client may request them. They do not represent implemented
features.

| Endpoint                | Status  | Behavior                                                                                        |
|-------------------------|---------|-------------------------------------------------------------------------------------------------|
| `osu-screenshot.php`    | ⚠️ Pending | Still returns HTTP 400 with `not available`; screenshots are stored by `IScreenshotStorage` once the host is migrated. |
| `osu-getfavourites.php` | ⚠️ Stub | Returns an empty favourite list.                                                                |
| `osu-addfavourite.php`  | ⚠️ Stub | No favourite functionality is implemented.                                                      |
| `osu-rate.php`          | ⚠️ Stub | Returns `not ranked`, matching the expected real-bancho response for the unsupported operation. |
| `osu-comment.php`       | ⚠️ Stub | Returns an empty result.                                                                        |

A compatibility stub should remain intentionally small. Do not gradually turn a stub into an implicit feature without
first changing the project's scope.

## Scope rules for contributors

When considering a new feature, ask:

1. Is it required for multiplayer operation?
2. Is it required for tournament operation?
3. Is it required by a supported osu! client workflow?
4. Is it required by Basil's documented API or IRC surface?
5. Does it introduce a new persistence or protocol surface that the project does not currently need?

If the first four answers are no, the feature is probably out of scope.

In particular:

> **"bancho.py implements it" is not sufficient justification for adding it to Basil.**

Basil intentionally has a smaller surface than bancho.py. Porting unrelated functionality increases maintenance cost,
persistence requirements, testing requirements, and compatibility obligations without improving the core tournament use
case.

No account acts for another. Every tool signs in as itself and receives only its own rights; a BasilBot command uses
the connection of the person who typed it.

## Lessons from reversed decisions

### Keeping chat commands inside the server

**Previous decision**

During the move toward a multiplayer-and-tournaments-only server, the original `BanchoBot` and chat command layer were
removed, including the full set of `!mp` commands.

**Problem**

The narrower project scope still required chat-based tournament control:

* `!mp start`
* map changes
* slot management
* match configuration
* other tournament operations

Removing chat completely was therefore an overcorrection.

**Current resolution**

Basil first reintroduced `BanchoBot` as a special session inside the server. The identity redesign
([`plans/identity-permissions-plan-20261005.md`](../../plans/identity-permissions-plan-20261005.md)) moved the commands
out to BasilBot as a separate client. The 2026-10-16 redesign brought them back as a server entry point: BasilBot is a
fixed user (id 1) that nobody can sign in as, commands run using the typist's connection, and the bot only replies
and announces. The command and announcement handlers are implemented in `Basil.Bot`; transport
integration remains planned for the Host phase; the bot's own connection is kept by the server. The earlier "special session" failed
because commands acted with the bot's own rights; that does not return: commands use the typist's rights, and the bot
only speaks for the server.

### Deferring the API indefinitely

**Previous decision**

A general API v1/v2 was deferred on the assumption that it could be implemented later if needed.

**Problem**

Tournament tooling still required live match information, reporting, and management even without a full public API.

**Current resolution**

The `api.<domain>` host was introduced with a deliberately narrower API surface:

* tournament match reports
* SSE live updates
* replay and beatmap downloads
* admin-key-gated management CRUD

It does **not** attempt to reproduce a general osu-web API, and has no OAuth.

Since the identity redesign the API is user-centric: management is no longer gated by an admin key acting for the
server, but by the permissions of the signed-in user, so clients built on Basil can do through the API what their
accounts are allowed to do.

### Automatic Bancho/Basil parity testing

**Previous decision**

One proposed validation strategy was to run bancho.py and Basil simultaneously and compare their behavior automatically.

**Problem**

This became impractical once Basil deliberately removed most of bancho.py's feature surface. There is no longer a
one-to-one implementation target for large parts of the upstream server.

**Current resolution**

Multiplayer and tournament behavior is validated manually using real osu! clients.

For client-facing multiplayer testing, use two real osu! clients and exercise the supported match workflows. See [
`getting-started.md`](../for-client/bancho/getting-started.md).

## Adding a feature

A new feature that appears to expand Basil's scope should normally include:

* a clear reason it is required
* an identified client or tournament workflow that consumes it
* an explicit API/protocol contract where applicable
* persistence requirements, if any
* tests for the new observable behavior
* documentation of the resulting scope change

Do not add a feature merely to achieve parity with bancho.py.

If a feature is useful but intentionally not part of Basil's current purpose, document it as out of scope rather than
implementing a partial version without a defined contract.

## See also

* [`architecture.md`](architecture.md): how the in-scope functionality is structured
* [`chat.md`](chat.md): the command dispatch architecture
* [`../for-client/bancho/getting-started.md`](../for-client/bancho/getting-started.md): setting up real osu! clients
  for multiplayer testing
