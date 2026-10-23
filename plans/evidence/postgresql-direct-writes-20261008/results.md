# Direct writes — measurements (2026-10-08)

Evidence for the `WriteConnections` default of the writer that sends each change as its own statement, one queue per
root ([`plans/infrastructure-memory-first-plan-20261008.md`](../../infrastructure-memory-first-plan-20261008.md) §6),
which replaced the batching writer measured in [`../postgresql-write-lanes-20261008`](../postgresql-write-lanes-20261008/results.md).

| Choice | Decision | Basis |
|---|---|---|
| `WriteConnections` default (`Basil:Database:WriteConnections`) | **8** | smallest count within 10% of the best capacity, with a steady-load try-operation p99 no worse than the best (1.1 × best + 1 ms) |
| Correctness under backend kills | holds | [`verify.log`](verify.log): ALL PASS (every write stored exactly once after three terminations, 20 racing try-operations stored once each, shutdown stores every queued write) |

Files: [`bench.cs`](bench.cs), [`raw.log`](raw.log) (complete output), [`verify.cs`](verify.cs), [`verify.log`](verify.log).

## Environment

Same machine and settings as the lane measurements: laptop, PostgreSQL 18.6 on Windows on the same machine,
`shared_buffers = 128MB`, `synchronous_commit = on` (the writer turns it off on its snapshot connections), `fsync = on`;
.NET 10.0.12, 8 logical processors.

## Method

Unchanged from the lane measurements, with `WriteConnections` (1, 2, 4, 8, 12, 16) in place of the lane count, 5 runs
each, medians decide: a steady load of 10 000 appends/s for 4 s with a try-operation every 20 ms, then a capacity
flood of 61 000 writes of distinct identities timed until every row is committed.

## Results (medians)

| connections | committed writes/s | try p50 ms | try p99 ms | try max ms |
|---|---|---|---|---|
| 1 | 2 406 | 497.8 | 1 749.4 | 1 749.4 |
| 2 | 8 102 | 3.7 | 39.4 | 139.0 |
| 4 | 13 296 | 1.7 | 27.8 | 75.5 |
| **8** | **18 566** | **2.2** | **15.9** | **111.5** |
| 12 | 19 770 | 3.1 | 64.3 | 156.0 |
| 16 | 19 885 | 3.9 | 62.5 | 193.8 |

## Compared with the batching writer (8 lanes)

| | batching, 8 lanes | direct, 8 connections |
|---|---|---|
| capacity, committed writes/s | 51 592 | 18 566 |
| try-operation p50 / p99 ms | 6.4 / 18.1 | 2.2 / 15.9 |

- The direct writer stores about 2.8 times fewer writes per second at full load, because every write is its own
  statement and commit instead of one statement of a 100-write batch.
- It still sustains the steady 10 000 appends/s with room to spare (capacity 18 566/s; each 4-second steady phase
  issued its appends in 4.02 s, the loop's own overhead). `bench.cs` prints "target met: False" because it compares
  that elapsed time with exactly 4.0 s; the backlog does not grow.
- A try-operation waits less (p50 2.2 ms instead of 6.4 ms): it no longer waits for a batch to fill or age.
- The plan's fallback (pipelining consecutive writes of one root) is not needed at this load.
