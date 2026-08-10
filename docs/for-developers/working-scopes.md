# Scoping

## Overview

Basil is a multiplayer tournament server built on top of **bancho.py**. It is intentionally **not** a full osu! server.

The project implements the parts of the osu! server surface required for multiplayer matches, tournament operation, and
the supporting client protocols. Features from bancho.py are not automatically in scope simply because they exist
upstream.

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
* tournament-oriented match commands
* live match reporting
* SSE-based match updates
* replay and beatmap delivery required by supported workflows

The implementation is built around Basil's multiplayer application services rather than attempting to reproduce the
complete bancho.py feature set.

### Chat commands and IRC

Chat is in scope because tournament operation depends on chat-based match control.

This includes:

* general supported chat commands
* `!mp` tournament commands
* BasilBot
* the embedded IRC gateway

The IRC gateway allows real IRC clients and tools such as osu-ahr to connect alongside osu! clients.

See [`chat.md`](chat.md) for the command dispatch architecture and the BasilBot Commands documentation at
`api.<domain>/docs/basil-bot/` for the complete supported command list.

### Tournament API

The `api.<domain>` host provides the narrower API surface required by tournament tooling.

It includes:

* tournament match reports
* live match updates through SSE
* replay and beatmap downloads
* admin-key-gated management operations

This is intentionally not a general-purpose replacement for osu-web's public API.

### In-game registration

`POST /users` is supported for in-game account registration.

The endpoint operates in bypass mode when no admin key is configured. Otherwise, the registration request must provide
an email value matching the configured server admin key.

This endpoint exists for the supported game-client workflow and should not be interpreted as a complete public
account-management API.

## Out of scope

The following features are deliberately not part of Basil's supported surface.

| Feature                                                                          | Status          | Rationale                                                                                                                                                     |
|----------------------------------------------------------------------------------|-----------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `!pool` and `!mp loadpool/unloadpool/ban/unban/pick`                             | ❌ Out of scope | A complete tournament mappool system requires persistence such as `tourney_pools` and `tourney_pool_maps`, which Basil does not currently have.               |
| Scrim engine: `!mp scrim`, `autoref`, `endscrim`, `rematch`                      | ❌ Out of scope | The race-safe match-point engine (`MatchScoringService`) was removed with the previous command layer and is not part of the current design.                   |
| `!mp force`                                                                      | ❌ Out of scope | Administrative forced-player insertion is not implemented.                                                                                                    |
| `!block`, `!unblock`, `!reconnect`, `!changename`, `!apikey`                     | ❌ Out of scope | These are personal/social account commands outside Basil's multiplayer and tournament scope.                                                                  |
| `ApiKey` on `User` / `UpdateApiKeyAsync`                                         | ❌ Out of scope | The separate API-key model was removed because it was unused. IRC authentication uses the osu! password directly.                                             |
| Friends: `osu-getfriends.php`, `FriendAddHandler`, `FriendRemoveHandler`         | ❌ Out of scope | Friend relationships are social functionality unrelated to tournament operation.                                                                              |
| General-purpose public JSON API v1/v2                                            | ❌ Out of scope | Basil has no concrete requirement for OAuth, public API versioning, or general-purpose external API access.                                                   |
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
| `osu-screenshot.php`    | ⚠️ Stub | Returns HTTP 400 with `not available`.                                                          |
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

## Lessons from reversed decisions

### Removing the entire chat command layer

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

Basil reintroduced `BanchoBot` as a real session through [`BotBootstrapService`](../../src/Basil.Application/Services/Bot/BotBootstrapService.cs).

The current command architecture uses:

```text
ICommandDispatcher
        ↓
CommandDispatcher
        ↓
MpCommandService
        ↓
MatchSession / MatchMembershipService
```

This provides the tournament commands Basil actually needs without restoring the entire historical command surface.

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

It does **not** attempt to reproduce a general osu-web API.

There is currently no OAuth, public API versioning, or general-purpose API contract.

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
