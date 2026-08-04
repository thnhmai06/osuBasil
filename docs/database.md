# Database

## Overview

Basil stores everything in one SQLite file next to the executable. This page covers why, what the schema looks like, and the handful of implementation quirks that come from combining SQLite with Dapper.

## Why SQLite?

Basil is meant to run as a single process for a single tournament, often on a laptop with no prior setup — there's no separate database server to install, configure, or keep running alongside it. A single file that can be copied, backed up, or thrown away is also exactly the right unit for "one tournament, one deployment": moving to another machine is a file copy, not a migration.

## Schema

Tables are PascalCase, ids auto-increment from 1. Two groups: general account/content management, and the tournament flow.

**General management:**

| Table | Purpose |
| --- | --- |
| `Users` | Accounts — name, password hash, privilege bits, country, silence expiry. `Id = 1` is seeded for BasilBot. |
| `Beatmapsets` / `Beatmaps` | One row per set, one row per difficulty (keyed by content hash). Kept in sync with the `Mapsets/` folder on disk — see [`beatmap-ingestion.md`](beatmap-ingestion.md). |
| `Channels` | The static chat channel catalog (`#osu`, `#lobby`, ...), with read/write privilege gates and an auto-join flag. |
| `Relationships` | Friend/block pairs between users. |
| `ClientHashes` | Hardware fingerprint history per login, used for the ban check described in [`authentication.md`](authentication.md). |
| `IngameLogins` / `Logs` | Write-only audit trails — login history and general moderation actions. |

**Tournament flow:**

| Table | Purpose |
| --- | --- |
| `Matches` | One row per room — name and timestamps only. Everything else lives on `Rounds`, since it can change every time a new beatmap is played. |
| `Rounds` | One row per beatmap played in a match: mode, win condition, team type, mods, and the beatmap's content hash (not a denormalized name — that's resolved live, see [`multiplayer.md`](multiplayer.md)). |
| `Scores` | Every submitted score, optionally linked to a round. See the score-to-round invariant below. |
| `MatchEvents` | A lifecycle audit log — created, closed, referee changes, and so on. |
| `UserStats` | Per-mode totals, seeded once at zero. Basil has no singleplayer ranking, so this is static display data, never recomputed from scores. |
| `Counters` | Running totals (`Beatmapsets:Total`, `Scores:Total`, ...) kept current by triggers, so a paginated list's total count never needs a full table scan. |

## Invariants

- **A score-to-round link never has a race window.** A round's id is recorded on the match the moment the round starts, before anyone plays. Score submission (an HTTP request) and the round-ending packet arrive on two unrelated connections with no ordering guarantee — see [`multiplayer.md`](multiplayer.md) for why the round id is only ever moved forward by the *next* round starting, never cleared in between. A submission that lands after the round technically ended still attaches correctly.
- **A beatmap reference always resolves by content hash, never by id** — an id can outlive the content it originally pointed at; a hash can't (see [`response-envelope.md`](response-envelope.md)).
- **Deleting a beatmapset cascades to its beatmaps** at the database level, not in application code.

## Implementation notes

Basil talks to SQLite through [Dapper](https://github.com/DapperLib/Dapper) (a thin query mapper, not a full ORM) with [DbUp](https://dbup.readthedocs.io/) running migrations on every startup. A few things fall out of that pairing:

- The connection string enables foreign-key enforcement explicitly — SQLite has it off by default per connection.
- A short busy-timeout is configured deliberately: unlike the original single-threaded server, Basil runs on a real thread pool, so concurrent writes to different matches are expected and should wait briefly rather than fail immediately.
- Dapper can't map a positional C# `record` straight from a SQLite reader, since the raw column types (`Int64`, `string`) don't line up with a record's narrower constructor types. Every repository maps through a private mutable row class first.

## Related code

- `Basil.Infrastructure/Persistence/Migrations/001_base.sql`
- `Basil.Infrastructure/Persistence/Repositories/`
- `Basil.Application/Services/Multiplayer/MatchReportService.cs`

## See also

- [`multiplayer.md`](multiplayer.md) — how rounds and scores turn into a tournament report
- [`beatmap-ingestion.md`](beatmap-ingestion.md) — how the `Beatmapsets`/`Beatmaps` tables stay in sync with disk
- [`response-envelope.md`](response-envelope.md) — how a stored row becomes an API response
