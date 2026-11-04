# Database

## Overview

Basil keeps its records in PostgreSQL. Live objects (users online, rooms, channels) are held by their registries and are
the truth of the running server; the database keeps what must survive a restart. Repositories await their reads and
writes, and a write that fails reaches the caller. Small, hot records may be cached aside (`CacheAside`), and the cache
entry is dropped only after the awaited write succeeded. The storage rules are in
[`persistence.md`](../for-agents/persistence.md).

This document explains why the storage works this way, what is stored, and how writes, reads and migrations work.
Files (beatmapsets, replays, avatars, menu images, screenshots, FAQs) are not in the database; see
[`assets.md`](assets.md) and [`beatmap-ingestion.md`](beatmap-ingestion.md).

## Why awaited persistence

Basil runs one server process for one deployment. Earlier versions wrote behind memory and queued copies of the
changes; that hid failures from the caller, and the queues needed ordering, retry and drain logic of their own. Now a
write is finished when the call returns and a refusal is the caller's to handle. The live state a player depends on
(the room being played, the online user) does not come from the database, so play continues while the database is
briefly unreachable; what needs the database (a new account, a stored score) waits for it or fails.

One database belongs to one Basil process; nothing else writes it behind the server's back. PostgreSQL replaced the
earlier SQLite file to write in parallel, so a deployment needs a database server and its credentials.

## What is stored

| Group        | Tables                                                                      | Notes                                                                 |
|--------------|-----------------------------------------------------------------------------|-----------------------------------------------------------------------|
| Accounts     | `users`, `credentials`, `restrictions`, `relationships`, `logins`           | `logins` is append-only history                                       |
| Server       | `settings` (one row per setting), `channels`, `menu_banners`               | resident: loaded at startup, never released                           |
| Beatmaps     | `beatmapsets`, `beatmaps`, sequences `local_beatmapset_ids`, `local_beatmap_ids` | mirror the stored beatmapset files; local ids start at 1 000 000 000 |
| Multiplayer  | `matches`, `rounds`, `match_events`, `round_scores`                         | `match_events` is append-only history                                 |
| Scores       | `scores`, `user_stats`                                                      | a score belongs to its player                                         |

Conventions: snake_case names, every time is `timestamptz` in UTC (the presentation converts it to local time), MD5
values are lowercase hex text, an asset is stored as the canonical text of its `AssetReference`.

The tournament tables separate the match from what changes per round:

```text
matches ──┬── match_events        lifecycle history (creation, closure, referees, …)
          └── rounds (match_id, number)
                 └── round_scores ──► scores
```

Beatmap, mode, mods, team type and win condition can change every round, so they live on `rounds`, and a round
refers to its beatmap by content hash rather than by copying its names. Which round a score was played in is a
relation of its own (`round_scores`) so the score stays a record of its player. What was decided for a round (who won,
by how much, the ranking of the scores) is stored as JSON in `rounds.result` when it is decided.

## Invariants

* **Awaited writes.** A repository change returns when the database has committed it, and a failure reaches the
  caller. A change that stores several rows together runs in one transaction.
* **Records are copies.** A repository returns fresh records; the live object in a registry is the truth of the
  running server.
* **Unique values.** The insert checks them; a unique violation becomes `AlreadyExistsException` (or `false` for a
  rename).
* **Parents before children.** A child refers to a parent only once the parent is committed.
* **Appends are stored exactly once.** Logins and match events carry no business id; the database assigns it.
* **Scores attach to the round they were played in.** A round is identified by its match and its number; the live
  room numbers the round when it starts, so the number is known before any score arrives. The room's latest round is
  replaced only when the next round starts, so a score that arrives after the round-ending packet still resolves to
  the round it was played in, and only when it was played on that round's beatmap. See
  [`multiplayer.md`](multiplayer.md).
* **Beatmaps are referenced by content hash**, which survives a row being replaced; ids are for lookups.
* **Deleting a beatmapset deletes its beatmaps**, enforced by the foreign key (`on delete cascade`).
* **`user_stats` is not derived.** Basil has no singleplayer ranking, so the per-mode totals are kept as they are and
  are not rebuilt from `scores`.

## How it works

### Writes

* `DatabaseSession` runs each write on a context of its own whose commit is confirmed, then saves what the change
  added or modified. `TransactAsync` does the same inside one transaction.
* Opening the connection is retried up to three times (after 50, 200 and 800 ms) when the database is briefly
  unreachable; the change itself runs once and is never retried, so a committed insert cannot be duplicated.
* A unique violation becomes `AlreadyExistsException`; every other failure reaches the caller unchanged.
* At most `Basil:Database:Write:PoolSize` connections (8 by default) write at once.

### Reads

A read runs on a context of its own, on a pooled connection, and sees only what is committed. At most twice the
processor count of reads run at once. Reads run on their own data source built from `ConnectionStrings:Read` (the write
connection string when it is empty), so a read replica can serve them; writes and migrations use
`ConnectionStrings:Write`.

### Migrations

Each module owns its schema through a dedicated Entity Framework context and migration history. This keeps a module's
tables and schema changes together while preserving foreign keys between modules; a referencing context maps the
principal table without taking ownership of its migration. Because project references forbid one module's Infrastructure to
reference another's, the principal is not the other module's row type: `ContextModel.MapUsersReference` and
`MapScoresReference` (Infrastructure.Common) map the `users` and `scores` tables as shared-type entities named
`UsersPrincipal` and `ScoresPrincipal`, excluded from migrations, and a row declares the key with
`HasOne(ContextModel.UsersTable, null).WithMany().HasForeignKey("UserId")`. The generated constraint is the same as with a row type.

At startup `DatabaseMigrator` applies the module migrations in foreign-key order: Common, Users, Authentication, Beatmaps, Content,
Scores, Multiplayer, then Chat. Each records its migrations in `__ef_migrations_<module>`. `write_tickets` (no longer written; removed with the regenerated migrations) belongs to
Common, the beatmap id sequences belong to Beatmaps, and the seeded channels belong to Chat. Persistence holds no SQL
text: schema changes are Entity Framework migrations and reads and writes use LINQ and row objects.

The module `InitialCreate` migrations replace the former single `InitialCreate`. This is safe only because no database
has applied that old migration; an already-migrated database would need a separate one-time adoption before using the
new histories.

## Related code

* `src/Infrastructure/Common/Persistence/` — `DatabaseSession`, `Database`, `DatabaseMigrator`, `ContextModel`
* `src/Infrastructure/Common/Registry/` — `CacheAside`
* `src/Infrastructure/<Module>/Persistence/` — the module's `DbContext`, the `XxxRow` types and the repositories
* `src/Infrastructure/<Module>/Migrations/` — the module's migrations and their history table
* `src/Infrastructure/Common/Workers/StorageStartup.cs` — migration and resident loading at startup; `AddBasilStorage()` registers the
  module migrations in the order above

## See also

* [`architecture.md`](architecture.md) — layers and where repositories live
* [`multiplayer.md`](multiplayer.md) — rounds and score attachment
* [`assets.md`](assets.md) — files and asset references
* [`configuration.md`](../for-technicians/configuration.md) — `Basil:Database` settings
