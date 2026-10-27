# Spike: one DbContext per module (Phase E, decision E2)

Status: spike for stage E1 (2026-10-28). Throwaway: nothing under `src/` or `tests/` was changed and nothing is
committed. All code and this report live under `spikes/DbContextSplit/`.

Environment used: .NET SDK 10.0.402, EF Core 10.0.12, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3,
`EFCore.NamingConventions` 10.0.1, `dotnet-ef` 10.0.12, PostgreSQL 18.6 on `127.0.0.1:55432` (trust auth, database
`spike_split`, dropped at the end of every run).

Documentation consulted through the Context7 MCP: `/dotnet/entityframework.docs` and its Microsoft Learn corpus
`/websites/learn_microsoft_en-us_ef_core` (a separate Microsoft Learn MCP was not available in this session). Relevant
pages: *Managing Migrations / history table*, *multiple DbContext types*, *ExcludeFromMigrations*,
`ExcludeForeignKeyFromMigrations`.

## 1. Question and answer

**Question.** With one DbContext per module and one migration history per module, can EF Core still

(a) generate a cross-module foreign key constraint in the migration of the referencing module **without SQL text**,
(b) apply the module migrations in dependency order to an empty database, and
(c) keep one history table per module?

**Answer: yes to all three**, provided the referencing context maps the referenced table as an entity type with
`ToTable("…", t => t.ExcludeFromMigrations())`. EF then emits the `CREATE TABLE … FOREIGN KEY (…) REFERENCES …` for
the referencing table and does **not** create the referenced table; the constraint exists in the database after the
principal module has migrated. Without a principal entity type in the model (plain column) no constraint is generated.

The cost is one new rule per referencing context: it declares the principal table as an excluded entity. Everything
else (history table, ordering, no SQL text) behaves exactly as the single-context case.

## 2. Current foreign keys in BasilDbContext

Read from `src/Infrastructure/Persistence/Migrations/20261021172725_InitialCreate.cs` and the
`*RowConfiguration` classes (16 constraints, all `NO ACTION` unless stated). Module ownership follows
`plans/phase-e-plan-20261028.md` §3.

| # | referencing table | column | principal | modules | on delete |
|---|---|---|---|---|---|
| 1 | `beatmaps` | `beatmapset_id` | `beatmapsets.id` | Beatmaps → Beatmaps | Cascade |
| 2 | `credentials` | `user_id` | `users.id` | Auth → **Users** | Cascade |
| 3 | `logins` | `user_id` | `users.id` | Auth → **Users** | Cascade |
| 4 | `matches` | `creator_id` | `users.id` | Multiplayer → **Users** | NO ACTION (nullable) |
| 5 | `moderation_log` | `by_id` | `users.id` | Users → Users | NO ACTION |
| 6 | `moderation_log` | `target_id` | `users.id` | Users → Users | NO ACTION |
| 7 | `relationships` | `actor_id` | `users.id` | Users → Users | NO ACTION |
| 8 | `relationships` | `target_id` | `users.id` | Users → Users | NO ACTION |
| 9 | `restrictions` | `user_id` | `users.id` | Users → Users | Cascade |
| 10 | `scores` | `user_id` | `users.id` | Scores → **Users** | NO ACTION (nullable) |
| 11 | `user_stats` | `user_id` | `users.id` | Scores → **Users** | NO ACTION |
| 12 | `match_events` | `match_id` | `matches.id` | Multiplayer → Multiplayer | NO ACTION |
| 13 | `match_events` | `actor_id` | `users.id` | Multiplayer → **Users** | NO ACTION |
| 14 | `match_events` | `target_id` | `users.id` | Multiplayer → **Users** | NO ACTION |
| 15 | `rounds` | `match_id` | `matches.id` | Multiplayer → Multiplayer | NO ACTION |
| 16 | `round_scores` | `score_id` | `scores.id` | Multiplayer → **Scores** | NO ACTION |

Eight of them cross a module boundary: 2, 3, 4, 10, 11, 13, 14, 16. Every cross-module FK points at `users` except
`round_scores.score_id`, which points at `scores`. So the module edge that actually needs a constraint is
*Users ← everything* plus *Scores ← Multiplayer*.

Not foreign keys today, although the column names suggest one, and the split does not add them:

* `round_scores (match_id, round_number)` has an index but no FK to `rounds (match_id, number)`.
* `scores.beatmap_hash` has an index but no FK to `beatmaps`/`beatmapsets`; the score stores a hash, not an id.

Tables with no FK at all: `channels`, `settings`, `menu_banners`, `write_tickets`.

## 3. Experiment

`spikes/DbContextSplit/` is a console project with four contexts, each with its own history table:

| context | tables | history table |
|---|---|---|
| `CommonDbContext` | `write_tickets` (owns it) | `__ef_migrations_common` |
| `UsersDbContext` | `users`, plus `write_tickets` excluded | `__ef_migrations_users` |
| `ScoresContextV1` | `scores`, plus `users` excluded via a stub row type | `__ef_migrations_scores` |
| `ScoresContextV2` | `scores`, plus `users` excluded via a shadow principal | `__ef_migrations_scores` |
| `ScoresContextV3` | `scores`, FK column with no relationship | `__ef_migrations_scores` |

Migrations were generated with `dotnet ef migrations add InitialCreate --context <T> --output-dir Migrations/<dir>`
(one output directory per context, so one model snapshot per context) and applied with
`Database.MigrateAsync()` from the program. The program also queries `information_schema` and `pg_constraint`.
Its full output is in `run-output.txt`; the code is `Program.cs` plus the context files.

### 3.1 Variant 1 — stub `users` row type + `HasOne`

```csharp
modelBuilder.Entity<UserRow>(builder =>
{
    builder.ToTable("users", table => table.ExcludeFromMigrations());
    builder.HasKey(row => row.Id);
});

modelBuilder.Entity<ScoreRow>(builder =>
{
    builder.ToTable("scores");
    builder.HasKey(row => row.Id);
    builder.Property(row => row.Id).UseIdentityByDefaultColumn();
    builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.NoAction);
    builder.HasIndex(row => row.UserId);
});
```

Generated migration (only the `scores` table is created):

```csharp
migrationBuilder.CreateTable(
    name: "scores",
    columns: table => new
    {
        id = table.Column<int>(type: "integer", nullable: false)
            .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
        user_id = table.Column<int>(type: "integer", nullable: true),
        total_score = table.Column<int>(type: "integer", nullable: false)
    },
    constraints: table =>
    {
        table.PrimaryKey("pk_scores", x => x.id);
        table.ForeignKey(
            name: "fk_scores_users_user_id",
            column: x => x.user_id,
            principalTable: "users",
            principalColumn: "id");
    });
```

**Verdict: works.** FK in the database:
`fk_scores_users_user_id FOREIGN KEY (user_id) REFERENCES users(id)`.

### 3.2 Variant 2 — shadow principal entity type (no CLR type)

```csharp
modelBuilder.SharedTypeEntity<Dictionary<string, object>>("UsersPrincipal", builder =>
{
    builder.ToTable("users", table => table.ExcludeFromMigrations());
    builder.Property<int>("Id");
    builder.HasKey("Id");
});

modelBuilder.Entity<ScoreRow>(builder =>
{
    builder.ToTable("scores");
    builder.HasKey(row => row.Id);
    builder.Property(row => row.Id).UseIdentityByDefaultColumn();
    builder.HasOne("UsersPrincipal", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.NoAction);
    builder.HasIndex(row => row.UserId);
});
```

**Verdict: works.** The generated migration and the database constraint are byte-for-byte the same as variant 1
(`fk_scores_users_user_id FOREIGN KEY (user_id) REFERENCES users(id)`). The `WithMany` of a named principal is the
non-generic builder, so the FK must be named as a string (`"UserId"`), which is the price of not having a CLR type.

### 3.3 Variant 3 — plain FK column, no relationship

```csharp
modelBuilder.Entity<ScoreRow>(builder =>
{
    builder.ToTable("scores");
    builder.HasKey(row => row.Id);
    builder.Property(row => row.Id).UseIdentityByDefaultColumn();
    builder.Property(row => row.UserId);
    builder.HasIndex(row => row.UserId);
});
```

**Verdict: no constraint** — `user_id integer NULL`, no `pg_constraint` row. The column survives; the database no
longer guarantees that `scores.user_id` names a user.

### 3.4 Order, history and isolation

Measured on a fresh `spike_split` for every variant:

| check | variant 1 | variant 2 | variant 3 |
|---|---|---|---|
| scores applied first, empty database | **fails** | **fails** | applies |
| error text | `Npgsql.PostgresException 42P01: relation "users" does not exist` | same | — |
| residue after the failed run | `__ef_migrations_scores` (empty), no `scores` table | same | — |
| Common → Users → Scores | applies | applies | applies |
| history after both | `__ef_migrations_users` = `…_InitialCreate`, `__ef_migrations_scores` = `…_InitialCreate` | same | same |
| `users` columns changed by Scores | no | no | no |
| re-running Users `MigrateAsync()` | pending `[]`, both histories and all tables unchanged | same | same |

Notes:

* The failure happens inside `CREATE TABLE scores … REFERENCES users`, before any migration is recorded; the history
  table itself is created by EF outside the migration and is left empty, so a retry after the principal module has
  been applied succeeds.
* Each context reads and writes only its own history table. The second context's `MigrateAsync()` never looks at the
  first context's history or tables; the DDL it runs is only its own.
* A follow-up `dotnet ef migrations add FollowUp --context ScoresContextV1` after the split produced an empty `Up()`:
  the excluded `users` entity stays in the model snapshot but generates no DDL, so later migrations in the referencing
  module do not try to create or drop the principal table.
* The shared-table pattern also works at run time: `UsersDbContext` maps `write_tickets` with
  `ExcludeFromMigrations`, `CommonDbContext`'s migration creates it, and `UsersDbContext.SaveChangesAsync()` inserted
  and read back a ticket row (`ticket read back through UsersDbContext: 1`). `ExcludeFromMigrations` affects only
  migration generation, never the runtime model.

## 4. Recommended mapping for Basil

**Use variant 1 with the principal's existing row type**; treat variant 2 as the fallback when the principal row type
is not reachable. Never variant 3 for an existing FK.

Concretely, every referencing module context keeps the referenced table in its model as an excluded entity:

```csharp
// Scores module
modelBuilder.Entity<UserRow>(builder =>
{
    builder.ToTable("users", table => table.ExcludeFromMigrations());  // owned and created by Users
    builder.HasKey(row => row.Id);
});

modelBuilder.Entity<ScoreRow>(builder =>
{
    builder.ToTable("scores");
    // … column configuration …
    builder.HasOne<UserRow>().WithMany().HasForeignKey(row => row.UserId).OnDelete(DeleteBehavior.NoAction);
    builder.HasIndex(row => row.UserId);
});
```

* **During E1 (all contexts in one assembly)** this needs nothing new: `UserRow` is already internal to
  `Infrastructure.Persistence`, so `ScoresDbContext` can use it as-is.
* **From E4/E5 (one assembly per module)** `UserRow` becomes internal to the Users module. Two equivalent ways to keep
  the same DDL, both without SQL text:
  * publish a key-only `UserReference` (an `int Id`) and let `Basil.Users.Infrastructure` map it to `users` and each
    referencing module map it excluded; or
  * switch that one relationship to variant 2 (shadow principal, `HasOne("UsersPrincipal", null)`), which needs no
    shared type at all.
  The DDL is identical in both, so the switch costs no new migration.
* Only the owning module's context may create the table; every other context declares it excluded. Add an architecture
  test that a non-owning context's migrations contain no `CreateTable` for a table it does not own (the generated
  migration is a normal class; the check can scan the `Migration` subclasses of that module).
* Each module context must be built with the same provider conventions as today
  (`UseSnakeCaseNamingConvention()`, `QueryTrackingBehavior.NoTracking`) so the generated table, index and constraint
  names match `InitialCreate` and nothing churns.
* `BasilDbContext.OnModelCreating` currently calls `ApplyConfigurationsFromAssembly` over the whole persistence
  assembly; per module it must apply only its own module's configurations (same assembly while E1 is in place: filter
  by namespace, or move the configuration classes into per-module `Configure` calls).

## 5. DatabaseMigrator: order and history tables

One history table per module, named `__ef_migrations_<module>` and configured on each context's options
(`UseNpgsql(connection, o => o.MigrationsHistoryTable("__ef_migrations_scores"))`). `DatabaseMigrator` applies the
contexts one after another, in this order, and stops at the first failure (a referencing module must never run before
its principal):

| # | context | history table | owns | depends on (FK) |
|---|---|---|---|---|
| 1 | `CommonDbContext` | `__ef_migrations_common` | `write_tickets` | — |
| 2 | `UsersDbContext` | `__ef_migrations_users` | `users`, `restrictions`, `moderation_log`, `relationships` | — |
| 3 | `AuthDbContext` | `__ef_migrations_auth` | `credentials`, `logins` | users |
| 4 | `BeatmapsDbContext` | `__ef_migrations_beatmaps` | `beatmapsets`, `beatmaps`, the two id sequences | — |
| 5 | `ContentDbContext` | `__ef_migrations_content` | `settings`, `menu_banners` | — |
| 6 | `ScoresDbContext` | `__ef_migrations_scores` | `scores`, `user_stats` | users |
| 7 | `MultiplayerDbContext` | `__ef_migrations_multiplayer` | `matches`, `rounds`, `match_events`, `round_scores` | users, matches, scores |
| 8 | `ChatDbContext` | `__ef_migrations_chat` | `channels` (+ the `#lobby`/`#osu` seed) | — |
| 9 | `ServersDbContext` | `__ef_migrations_servers` | nothing today — no context until it has a table | — |

The order follows both the module graph of the plan (Common → Auth/Beatmaps/Content → Users → Scores → Multiplayer →
Chat → Servers) and the FK graph; where they disagree, the FK graph wins (Auth depends on Users through the
`credentials`/`logins` constraint even though the code reference goes Users → Auth).

Shape of `DatabaseMigrator`: an ordered list of context factories, one per module, supplied by the composition:

```csharp
internal sealed class DatabaseMigrator(Database database, IEnumerable<IModuleMigration> modules)
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        foreach (var module in modules.OrderBy(m => m.Rank))
            await module.MigrateAsync(database, cancellationToken);
    }
}
```

`IModuleMigration` is implemented by each module (it knows its own context and history table); the rank comes from the
table above. Nothing else changes: `MigrateAsync` is idempotent per context, and running it on every start touches only
pending migrations of that module (verified: re-running Users after Scores applied left both histories and all tables
untouched).

## 6. Effect on the existing `InitialCreate`

Today there is one context, one migration (`20261021172725_InitialCreate`) and one history table
(`__EFMigrationsHistory`). After the split that history table is not read any more; each module has its own.

Regeneration, under the same precondition as today — **no database has applied the old `InitialCreate`** (the
database is recreated from migrations):

1. Create the module contexts with their `MigrationsHistoryTable`, and remove the old
   `src/Infrastructure/Persistence/Migrations/` migration, designer and snapshot.
2. For each context of the table in §5, in that order:
   `dotnet ef migrations add InitialCreate --context <T>DbContext --output-dir Migrations/<Module> --namespace <...>.Migrations`.
3. Diff each generated migration against the matching tables of the old `InitialCreate`. The DDL must be identical —
   same table names, column types, defaults, check constraints, index names, FK names and delete behaviour, same
   sequences — because the model only moved, it did not change. The only things that move between files are the
   `local_beatmapset_ids` / `local_beatmap_ids` sequences (into Beatmaps) and the two seeded channels (into Chat);
   `write_tickets` moves into Common.
4. Move the sequences: `modelBuilder.HasSequence<int>("local_beatmapset_ids").StartsAt(LocalIdFloor)` and its beatmap
   twin belong to the Beatmaps context; the `InsertData` of `#lobby`/`#osu` belongs to the Chat migration.
5. Keep the `BasilDbContextFactory` role by giving each context its own `IDesignTimeDbContextFactory<T>` (as in this
   spike), otherwise `dotnet ef` cannot tell which context to build.

If a database *has* applied the old migration, `MigrateAsync` cannot upgrade it — every module's `InitialCreate` would
try to create existing tables. Adoption is then a one-off: verify the schema, then insert each module's migration id
into its `__ef_migrations_<module>` table. The spike's precondition says this case does not exist, so it is not part
of E1.

## 7. Can `Database`, `DatabaseWriter`, `DatabaseReader` and `WriteTicketRow` stay generic?

Yes, with one change per type — none of them needs to be duplicated per module.

| type | can stay shared? | change |
|---|---|---|
| `Database` | yes, one instance | keep the two `NpgsqlDataSource` pools, cache `DbContextOptions<TContext>` per context type, expose `CreateConfirmedContext<TContext>()` / `CreateUnconfirmedContext<TContext>()`. Creating a pool (and a `WriteConnections` budget) per module would multiply connections and idle sessions. The module registers its one-line factory (`new UsersDbContext(options)`) so no reflection is involved. |
| `DatabaseWriter` | yes, one instance | the queue is keyed by `Root`, not by context, and must stay one queue so `HoldAsync` over several roots keeps working and there is one retry order, one connection semaphore and one hosted service. Make the operations generic over the context (`WriteAsync<TContext>(Root, Func<TContext, …>)`) and let `WriteCommand` carry its context type (or a typed delegate plus its factory). `PendingTransaction` stores the untyped delegate and the writer obtains the context from the command. |
| `DatabaseReader` | yes, one instance | `ReadAsync<TContext, T>(Func<TContext, …>)`. If call-site noise is a concern, register a thin `DatabaseReader<TContext>` per module inside that module; it is one class, not a copy. |
| `WriteTicketRow` | yes, unchanged, not generic | every write inserts a ticket in the write's own transaction, so every module context must be able to write `write_tickets`: map the same `WriteTicketRow` in each context with `ToTable("write_tickets", t => t.ExcludeFromMigrations())`, and let Common's migration be the only one that creates it. Verified in this spike: Users' migration creates only `users`, yet `UsersDbContext` inserted and read a ticket. |

`BasilDbContextFactory` is the only type that must split: one `IDesignTimeDbContextFactory<T>` per context.

## 8. Files

```text
spikes/DbContextSplit/
  Spike.csproj, SpikeOptions.cs, Program.cs, run-output.txt
  Common/CommonDbContext.cs
  Users/UsersDbContext.cs
  Scores/Variant1StubRow.cs
  Scores/Variant2ShadowPrincipal.cs
  Scores/Variant3PlainColumn.cs
  Migrations/{Common,Users,ScoresV1,ScoresV2,ScoresV3}/
```

## 9. Verdict

| # | variant | migration generates the FK | FK present in the database | scores first on an empty DB | verdict |
|---|---|---|---|---|---|
| 1 | stub `users` row, `ExcludeFromMigrations`, `HasOne<UserRow>()` | yes | `fk_scores_users_user_id FOREIGN KEY (user_id) REFERENCES users(id)` | fails `42P01: relation "users" does not exist` | **works; recommended** |
| 2 | shadow principal entity type, no CLR type | yes | identical to variant 1 | same failure | **works; fallback when no row type is reachable** |
| 3 | plain column, no relationship | no | none | applies (no dependency) | works only as a plain column; **loses the constraint** |

History: two history tables, one per context, each holding only its own migration; a re-run of one module's
`MigrateAsync()` leaves the other module's tables and history untouched. Ordering is required for the first creation
and must follow the FK graph of §5.
