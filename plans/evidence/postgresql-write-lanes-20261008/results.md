# Write lanes, pg_trgm and fillfactor — measurements (2026-10-08)

Evidence for three choices of the PostgreSQL storage
([`plans/postgresql-plan-20261007.md`](../../postgresql-plan-20261007.md) P8, P9, P13):

| Choice | Decision | Basis |
|---|---|---|
| `StorageOptions.WriteLanes` default | **8** | smallest lane count within 10% of the best throughput, with a steady-load try-operation p99 no worse than the best (1.1 × best + 1 ms) |
| `users_safe_name_trgm` (GIN, `pg_trgm`) | **keep** | partial-name search 19× faster at 100 000 users, no cost at 1 000 |
| `fillfactor = 90` on `users`, `user_stats`, `matches`, `rounds` | **keep** | updates that must touch indexes (non-HOT) drop from 1.8% to 0.6% |

Files: [`bench.cs`](bench.cs) (the benchmark, a .NET 10 file-based app), [`raw.log`](raw.log) (its complete output).

## Environment

- Laptop, Intel Core i7-11370H (4 cores, 8 threads), 15.7 GB RAM, NVMe SSD (Kingston SNV3S 1 TB), Windows 11 Pro.
- PostgreSQL 18.6 (Windows, x64) **on the same machine** as the benchmark; default settings: `shared_buffers = 128MB`,
  `synchronous_commit = on` (the writer turns it off per batch for snapshot-only batches), `wal_writer_delay = 200ms`,
  `max_connections = 100`, `fsync = on`.
- .NET 10.0.12, Release build of `Basil.Infrastructure.Storage` at the commit that added this folder.

## Method

### Write lanes

Each run uses a fresh database (migrated by the storage itself) seeded with 1 000 users and 16 matches, and the real
storage through its public repository contracts (`AddInfrastructureStorage`). Two phases:

1. **Steady load** — 10 000 appends/s (logins and match events; every one a new row, so nothing is coalesced) for
   4 s, while a try-operation (`IRestrictionRepository.CreateAsync`, which waits for its commit) runs every 20 ms. The
   latency of every try-operation is recorded (about 170 per run).
2. **Capacity** — 61 000 writes queued at once (40 000 match events, 20 000 logins, 1 000 user stats of distinct
   users), timed until every row is visible in the database (`count(*)`, i.e. committed). Writes of distinct
   identities are used on purpose: snapshots of the same identity are coalesced while queued, which would let a slower
   configuration look faster by storing fewer rows.

Each lane count (1, 2, 4, 8, 12, 16) runs 5 times; the medians decide.

An earlier version of the benchmark also reported rows/s from `pg_stat_database` counters; it was dropped because
backends publish those counters late (it showed 7 600 rows/s for runs that committed 60 000), and an earlier version
that probed try-operations one after another during a flood measured idle latency rather than latency under load.
Both are superseded by the method above.

### pg_trgm

`explain (analyze)` of the user search query (`safe_name like '%…%'`) on 1 000 and 100 000 users, median of 7, with
the GIN trigram index and after dropping it.

### fillfactor

1 000 users and their stats; 200 batches, each upserting the snapshot of about 50 random users and their stats exactly
as the writer does; the share of HOT updates read from `pg_stat_user_tables`, with `fillfactor` 90 and 100.

## Results

### Write lanes (medians of 5 runs)

| lanes | committed writes/s | try p50 ms | try p99 ms | try max ms |
|---|---|---|---|---|
| 1 | 18 806 | 9.8 | 27.8 | 38.0 |
| 2 | 27 092 | 7.6 | 19.7 | 91.7 |
| 4 | 43 720 | 6.1 | 40.8 | 117.6 |
| **8** | **51 592** | **6.4** | **18.1** | **112.5** |
| 12 | 44 600 | 6.1 | 34.5 | 218.9 |
| 16 | 53 758 | 5.7 | 18.2 | 249.5 |

- Throughput grows up to 8 lanes and then flattens (12 and 16 are within noise of 8); the machine has 8 hardware
  threads shared by the benchmark and PostgreSQL.
- Try-operation p50 is 6–10 ms under 10 000 writes/s for every configuration (a try-operation waits for its lane's
  batch in flight); p99 is noisy across runs (see `raw.log`), and the worst case grows past 8 lanes.
- Rule result: **8**. Only 8 and 16 are within 10% of the best throughput, and 8 is the smaller.

### pg_trgm (median, ms)

| users | with index | without index |
|---|---|---|
| 1 000 | 0.131 | 0.132 |
| 100 000 | 0.690 | 13.236 |

### fillfactor (HOT share of updates)

| table | fillfactor 90 | fillfactor 100 |
|---|---|---|
| users | 99.4% | 98.2% |
| user_stats | 99.5% | 98.3% |

## Limits

- One machine, database on the same host. With PostgreSQL on another host each statement batch costs a network round
  trip, which favours more lanes; re-run `bench.cs` against the real database and set `WriteLanes` from it.
- 1 000 users is the expected size of a deployment; the load figures (10 000 writes/s steady, 61 000 at once) are far
  above what such a server produces and only serve to compare configurations.

## Reproduce

```bash
cd plans/evidence/postgresql-write-lanes-20261008
dotnet run -c Release bench.cs -- "Host=localhost;Database=basil;Username=basil;Password=<password>" 1,2,4,8,12,16 5
```

The role needs `CREATEDB`; every run creates and drops its own `basil_bench_<guid>` database.
