# Database

## Overview

Basil uses a single SQLite database file stored alongside the application data.

The database is intentionally local and self-contained. Basil does not require a separate database server for normal
operation.

This document describes:

* why SQLite is used;
* the logical schema;
* invariants that application code must preserve;
* implementation details specific to SQLite and Dapper.

The database is one part of Basil's local state. Some data, particularly locally ingested beatmaps, has a filesystem
source of truth. See [`beatmap-ingestion.md`](beatmap-ingestion.md).

## Why SQLite?

Basil is designed to run as a single server process, typically for one tournament deployment.

A separate database server would add operational requirements that provide little value for this use case:

* no database service to install;
* no connection endpoint to configure;
* no separate credentials to manage;
* no additional process to monitor.

SQLite instead stores the complete database in a single file.

This also makes the database convenient to:

* back up;
* copy to another machine;
* archive after a tournament;
* discard when starting a new deployment.

The deployment unit can therefore remain self-contained:

```text
Basil deployment
├── executable
├── configuration
├── Data/
│   ├── database
│   └── ...
└── Mapsets/
```

Moving a deployment to another machine does not require provisioning a database server.

## Schema

Tables use PascalCase names. Primary keys are integer ids that auto-increment from `1`.

The schema is divided conceptually into two groups:

1. general account and content management;
2. tournament state and reporting.

### General management

| Table                       | Purpose                                                                                                                                                                        |
|-----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Users`                     | User accounts, including name, password hash, privilege bits, country, and silence expiry. `Id = 0` is reserved for BasilBot.                                                  |
| `Beatmapsets` / `Beatmaps`  | Locally ingested beatmapsets and their difficulties. Each difficulty is identified by content hash. These tables mirror the local `Mapsets/` data. See [`beatmap-ingestion.md`](beatmap-ingestion.md). |
| `Channels`                  | Static chat channel catalog such as `#osu` and `#lobby`, including read/write privilege gates and auto-join state.                                                             |
| `Relationships`             | Friend and block relationships between users.                                                                                                                                  |
| `ClientHashes`              | Hardware fingerprint history associated with logins, used by the authentication and ban checks described in [`authentication.md`](../for-client/bancho/authentication.md).     |
| `IngameLogins` / `UserLogs` | Append-only audit data. `IngameLogins` records login information such as IP and client version; `UserLogs` records administrative actions between users.                       |
| `Settings`                  | Key-value server state such as the admin key hash, last-changed timestamp, client menu icon, and MOTD.                                                                         |

### Tournament flow

| Table         | Purpose                                                                                                                  |
|---------------|--------------------------------------------------------------------------------------------------------------------------|
| `Matches`     | One row per multiplayer room. Stores room identity and lifecycle timestamps. Mutable gameplay state belongs to `Rounds`. |
| `Rounds`      | One row per beatmap played in a match. Stores mode, win condition, team type, mods, and the beatmap content hash.        |
| `Scores`      | Submitted scores, optionally associated with a round. See the score-to-round invariant below.                            |
| `MatchEvents` | Append-only match lifecycle history, including creation, closure, and referee changes.                                   |
| `UserStats`   | Per-mode aggregate display statistics. Initialized once and not recomputed from individual scores.                       |
| `Counters`    | Maintained aggregate counters such as `Beatmapsets:Total` and `Scores:Total`. Database triggers keep them synchronized.  |

## Data ownership

Not every database table is the authoritative representation of its data.

For locally ingested beatmaps:

```text
Mapsets/
    │
    ▼
filesystem
    │
    └── authoritative source
            │
            ▼
       Beatmapsets
            │
            ▼
         Beatmaps
       indexed metadata
```

The `Beatmapsets` and `Beatmaps` tables mirror locally stored beatmap content. Application code must not treat those
rows as an independent replacement for the files.

Other entities, such as users, matches, rounds, and scores, are database-owned.

When adding a new table or field, explicitly determine which layer owns the underlying state. This affects
synchronization, deletion, recovery, and startup reconciliation.

## Invariants

The following invariants are part of the application contract. Code that modifies the corresponding workflows must
preserve them.

### Scores must attach to the correct round

A score-to-round relationship must not depend on the ordering of network requests.

When a round starts, its id is recorded on the match immediately:

```text
round starts
    │
    ▼
match.currentRoundId = new round
    │
    ▼
players submit scores
```

Score submission and the packet that ends a round arrive over independent connections. Their relative ordering is
therefore not guaranteed.

The current round id is intentionally advanced only when the next round starts. It is not cleared when the current round
ends.

As a result, a score submission that arrives slightly after the round-ending packet still resolves to the round that was
active when the score was produced.

See [`multiplayer.md`](multiplayer.md) for the complete round lifecycle.

### Beatmaps are referenced by content hash

Beatmap references use the beatmap's content hash rather than relying exclusively on its database id.

The reason is durability: a database id may become invalid after a row is replaced or removed, while the content hash
identifies the underlying beatmap content.

When resolving a beatmap reference, use the content hash as the stable identity.

See [`response-envelope.md`](response-envelope.md) for how this identity is exposed through API responses.

### Beatmapset deletion cascades

Deleting a beatmapset must also remove its associated difficulties.

This relationship is enforced by the database through foreign-key cascading rather than relying on application code to
manually delete every child row.

This keeps deletion correct even when the operation is performed through a different repository or transaction path.

## Tournament state model

The tournament tables deliberately separate long-lived match identity from per-round state.

```text
Match
  │
  ├── MatchEvents
  │
  └── Rounds
        │
        └── Scores
```

A `Match` represents the room itself.

A `Round` represents one beatmap played inside that room.

This distinction matters because gameplay settings such as:

* beatmap;
* mode;
* mods;
* team type;
* win condition;

can change every time a new round begins.

They therefore belong to `Rounds`, not `Matches`.

The beatmap itself is referenced through its content hash rather than copying beatmap names or other mutable metadata
into the round.

## User statistics

`UserStats` is display-oriented state.

Basil does not maintain a singleplayer ranking system, so these values are not periodically reconstructed by scanning
`Scores`.

They are seeded at zero and treated as persistent per-mode totals.

Do not introduce code that assumes `UserStats` can always be regenerated from the score table unless the statistics
model is deliberately redesigned.

## Counters

`Counters` stores running totals used by APIs that need aggregate counts alongside paginated data.

Examples include:

```text
Beatmapsets:Total
Scores:Total
```

These values are maintained by database triggers.

This avoids requiring operations such as:

```sql
SELECT COUNT(*) FROM ...
```

over potentially large tables whenever a paginated endpoint needs a total count.

The counter and its corresponding table therefore form a consistency pair. Any schema change affecting an entity with a
counter must also consider the associated trigger.

## SQLite and Dapper

Basil accesses SQLite through [Dapper](https://github.com/DapperLib/Dapper).

Dapper is intentionally used as a thin SQL mapper rather than as a full ORM. Repositories own the SQL and explicitly
control how database state is loaded and persisted.

[DbUp](https://dbup.readthedocs.io/) runs database migrations during application startup.

The resulting persistence stack is:

```text
Application services
        │
        ▼
Repositories
        │
        ▼
Dapper
        │
        ▼
SQLite
        │
        ▲
      DbUp
   migrations
```

## Implementation notes

### Foreign-key enforcement

SQLite does not enable foreign-key enforcement automatically for every connection.

Basil explicitly enables it through the connection configuration.

Do not assume that declaring a foreign key in the schema is sufficient by itself; enforcement must remain enabled on
every database connection.

### Busy timeout

SQLite permits concurrent readers but serializes conflicting writes.

Basil therefore configures a short busy timeout.

This is deliberate because Basil runs on a normal thread pool rather than the single-threaded execution model of the
original server. Concurrent writes from independent matches are expected.

A short wait is preferable to immediately failing a transaction when another write has temporarily acquired the SQLite
lock.

The timeout should remain short: it is intended to absorb normal contention, not hide persistent database bottlenecks.

### Dapper row mapping

Dapper maps query results onto C# types, but SQLite's raw column types do not always match the constructor parameter
types of narrow C# records.

For example, SQLite commonly returns integer values as `Int64`, while an application record may use a narrower integer
type.

Repositories therefore use private mutable row classes as the database representation:

```text
SQLite row
    │
    ▼
private repository row type
    │
    ▼
domain/application model
```

Do not assume that every database query can be mapped directly into a positional C# `record`.

Keeping the database row type separate also prevents persistence-specific concerns from leaking into application models.

## Migrations

Schema changes are applied through DbUp migrations.

The base schema is defined in:

```text
Basil.Infrastructure/Persistence/Migrations/001_base.sql
```

Subsequent schema changes should be introduced as new migrations rather than modifying an already-applied migration.

This preserves the ability to initialize a fresh database and upgrade an existing deployment through the same migration
mechanism.

## Related code

* [`Basil.Infrastructure/Persistence/Migrations/001_base.sql`](../../src/Basil.Infrastructure/Persistence/Migrations/001_base.sql): base schema
* [`Basil.Infrastructure/Persistence/Repositories/`](../../src/Basil.Infrastructure/Persistence/Repositories/): database repositories
* [`Basil.Application/Services/Multiplayer/MatchReportService.cs`](../../src/Basil.Application/Services/Multiplayer/MatchReportService.cs): tournament data aggregation and reporting

## See also

* [`multiplayer.md`](multiplayer.md): how matches, rounds, and scores form a tournament report
* [`beatmap-ingestion.md`](beatmap-ingestion.md): how locally ingested `Beatmapsets`/`Beatmaps` stay synchronized with disk
* [`response-envelope.md`](response-envelope.md): how stored data becomes an API response
