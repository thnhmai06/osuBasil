# RC11 recurrence — evidence review of the 2026-09-05 `full.json` run

Artifact under review: `.loadtest/reports/full-20260905-094716/`
Server commit under test: `d5ca2d4`
Status of this document: analysis only, at time of writing. The fix plan
([`rc11-fix-plan-20260905.md`](rc11-fix-plan-20260905.md)) was subsequently implemented the same
day; see its "Implementation outcome" section and
[`known-limitations.md`](../docs/for-developers/known-limitations.md)'s RC11 entry for what
followed, including a same-day API-only re-run that did not reproduce the collapse. The analysis
below is preserved as originally written.

---

## Summary

Re-reading the run's own NBomber reports and `resources.csv` contradicts three claims in
`plans/perf-investigation-handoff.md`. The handoff's causal diagram
(`round-end persistence → outbox full → DB-touching work degrades → ThreadPool backlog → collapse`)
is not supported by the timeline in the artifacts.

What the artifacts actually show:

* The server was healthy — 3,688 successful DB-backed requests per second, p50 23 ms — at 500
  concurrency, minutes after the outbox-full burst.
* The collapse is a single abrupt transition inside one 60-second window (10:56 UTC), not a
  progressive degradation across the API ramp.
* From that moment the process consumes essentially no CPU while the ThreadPool injects one new
  thread per second indefinitely and never recovers. That is the signature of threads blocked in a
  kernel wait, not of a saturated but progressing system.
* The `api_health_500` "control observation" is an artifact of scenario ordering. Health ran as
  scenario 13, immediately before the collapse; every scenario that failed ran after it. The
  DB-touching / DB-free split in the handoff is a coincidence of when each scenario ran.

The mechanism that blocks the threads is **not yet established**. One thread dump taken during the
collapse settles it; nobody in either investigation has taken one.

---

## Timeline (from `resources.csv`, aggregated per minute)

| UTC minute | Scenario | CPU % (avg) | Max TP queue | Max TP threads | Max TCP |
|---|---|---|---|---|---|
| 10:51–10:52 | `api_mixed_200` | 50–55 | 216 | 16 | 201 |
| 10:53 | — | 53.9 | 226 | 14 | 200 |
| 10:54–10:55 | `api_health_500` | 39.1 / 32.3 | 461 / 443 | 14 / 11 | 501 |
| **10:56** | **`api_user_500`** | **3.6** | **1203** | **79** | 500 |
| 10:57 | `api_match_list_500` | 0.1 | 1503 | 164 | 82 |
| 10:58 | `api_match_report_500` | 0.1 | 1562 | 252 | 501 |
| 10:59–11:00 | `api_beatmapset_500` | 0.1 | 1846 | 334 → 416 | 130 / 122 |
| 11:01–11:02 | `api_mixed_500` | 0.1 / 0.0 | 2004 | 486 → 488 | 500 / 297 |

At 10:55 the process ran 11 ThreadPool threads at 32 % CPU. One minute later: 79 threads, 3.6 % CPU,
queue tripled. From 10:57 onward CPU is pinned at 0.1 % while ThreadPool threads climb linearly at
roughly 1.4/second — the runtime's starvation hill-climbing injection rate — for six straight
minutes, and every injected thread is consumed and never returns.

Zero CPU with 488 live threads and 2,000 queued work items means the threads are not spinning and
not making progress. They are parked in a blocking wait.

---

## Corrections to the handoff

### 1. `api_user_500` did not run against an already-collapsed server — CONFIRMED

`api/14/nbomber_report_2026-09-05--10-56-*.txt`:

```
requests   total = 224094, ok = 221308, fail = 2786
RPS        total = 3734.9/s, ok = 3688.47/s
latency    p50 = 23.65, p75 = 31.78, p95 = 42.56, p99 = 57.54
```

`GET /users/{idOrName}` is a SQLite-backed read. The server served 221,308 of them at 3,688/s with a
p99 of 57 ms. The failure bucket's own percentiles (p50 2,054 ms, p75 18,465 ms, p95 30,065 ms) place
the failures at the *end* of the scenario. The collapse began part-way through scenario 14, not
before it.

The handoff's statement that "the collapse was already visible during `api_user_500`" — and the
inference drawn from it, that `GET /matches/{matchId}` merely inherited a broken server — both rest
on a misreading of this report.

### 2. The outbox-full burst is not a precursor to the collapse — CONFIRMED

`MatchRoundEndOutboxFullException` appears at 10:39 UTC (17:39 +07:00), at the end of
`multiplayer_64`. The collapse is at 10:56 UTC. In the 17 minutes between them the server ran
`api_match_report_200`, `api_beatmapset_200`, `api_mixed_200` and `api_health_500` — the last at
15,229 requests/second — all healthy.

The outbox exhaustion is a real defect worth its own investigation. It is not a step in the chain
that produced this outage.

### 3. The `api_health_500` control observation does not isolate the database — CONFIRMED

Scenario order is `health → user → match_list → match_report → beatmapset → mixed` at each
concurrency tier. At the 500 tier, health is scenario 13 and the collapse lands in scenario 14.
Every DB-touching scenario at 500 concurrency ran *after* the transition; health is the only one that
ran *before* it. "DB-free endpoint survived, DB-touching endpoints did not" is therefore fully
explained by ordering, and carries no information about the database.

The handoff uses this observation to rule out a generic concurrency explanation and to "strongly
reconnect the current RC11 recurrence with the database path." That reconnection is unsupported.

### 4. The failures are connection-level, not application-level — CONFIRMED

Every post-collapse scenario shows the same distribution:

| Scenario | ok | `connection actively refused` | client timeout |
|---|---|---|---|
| `api_match_list_500` | 0 | 12,808 | 82 + 500 |
| `api_match_report_500` | 0 | 11,065 | 141 + 500 |
| `api_beatmapset_500` | 0 | 12,495 | 82 + 500 |
| `api_mixed_500` | 0 | 5,991 | 500 + 500 |

`WSAECONNREFUSED` on loopback means the client received a TCP RST: the listen backlog was full and
Kestrel's accept loop was not draining it. The overwhelming majority of these requests never reached
application code at all. Whatever is wrong is upstream of route handlers, and consistent with the
accept loop being queued behind 2,000 ThreadPool work items.

### 5. The log truncation is Serilog's documented default, not an unexplained cap — CONFIRMED

`.loadtest/server/Logs/latest.log` is exactly **1,073,742,070 bytes** (1 GiB + 246 B).
`Program.cs:306-311` configures `WriteTo.File` without `fileSizeLimitBytes` or
`rollOnFileSizeLimit`, so the sink stops writing at its 1 GB default and silently discards
everything after.

It stopped at 10:46 UTC — **ten minutes before the collapse**. This was never going to capture the
failure, and the same will be true of any future run until the sink is configured.

The volume itself is worth noting: `ApiRequestLoggingMiddleware` emits one Information line per
completed `basilapi` request. `api_health_500` alone produced 913,782 of them in 60 seconds.

---

## What is actually blocking the threads — open

Two candidate mechanisms survive the evidence. Neither is established.

### Candidate A — the log pipeline blocks on redirected stdout (HYPOTHESIS)

`tests/Basil.LoadTests/Hosting/DotnetServerHost.cs:67-82` starts the server with
`RedirectStandardOutput = true` and drains it with:

```csharp
_ = _process.StandardOutput.ReadToEndAsync(cancellationToken);
```

`StartAsync` is called from `Program.cs:68` with no token, so the drain does run for the process
lifetime — it does not die early. But `ReadToEndAsync` accumulates the *entire run's* stdout into one
`StringBuilder` that is never released, and the task is discarded, so a fault inside it is silent.

The server writes every log line to `WriteTo.Console` as well as to the file sink. Serilog's console
sink writes synchronously under a single process-wide lock. If the harness reader stalls — a
multi-hundred-megabyte `StringBuilder` doubling copy is the obvious way — the 64 KB pipe buffer fills
in milliseconds at this log rate, the writing thread blocks inside `WriteFile`, and every other
thread that logs anything blocks behind the sink lock. That produces exactly the observed shape:
instant transition, zero CPU, unbounded thread injection, no recovery.

Supporting circumstantial evidence: the file sink stopped at 10:46, so from that point *all* log I/O
went to the console pipe; `api_health_500` then pushed roughly 913,782 lines through it in 60
seconds; `TotalMachineCpuPercent` averages 58 % and peaks at 100 %, so the machine was already under
pressure.

Against it: `ReadToEndAsync` is a tight read loop with amortized-O(1) appends and does not naturally
stall. The stall is inferred from harness GC pressure that has not been measured.

If this is the mechanism, RC11 as observed in this run is substantially an apparatus defect — the
harness blocking the process it is measuring — plus a real production-relevant defect in
unbounded synchronous console logging.

### Candidate B — synchronous SQLite blocking ThreadPool threads (HYPOTHESIS, narrowed)

`SqliteConnectionFactory.Open` (`src/Basil.Infrastructure/Persistence/SqliteConnectionFactory.cs:19-27`)
is fully synchronous — `connection.Open()` plus a `PRAGMA` round-trip via `ExecuteNonQuery()` — and is
called from inside `async` repository methods at 21 sites. `Microsoft.Data.Sqlite` provides no true
async I/O, so every database call occupies a ThreadPool thread for its full duration. With
`PRAGMA busy_timeout = 5000`, a contended write parks its thread inside SQLite's busy handler for up
to five seconds of zero CPU. Nothing bounds how many requests may be inside SQLite at once.

This is a genuine ThreadPool hazard and should be addressed regardless of what caused this outage.

But it does not explain *this* collapse as stated. The read path is exonerated at this concurrency:
3,688 successful DB reads/second at p99 57 ms, at 500 concurrency, in scenario 14. Scenarios 14–18
generate almost no write load. The precise position is: **the read path is exonerated at 3.7k RPS
and 500 concurrency in this run; the write path was not exercised at this concurrency and remains
untested.**

---

## The one measurement that settles it

Reproduce the run and take a thread dump of the server process during the collapse window
(`dotnet-dump collect -p <pid>`, then `clrstack -all`; or `dotnet-stack report -p <pid>`).

| Where the ~488 threads are | Conclusion |
|---|---|
| `ConsoleSink.Emit` → `Console.Out.Write` → `WriteFile` | Candidate A confirmed |
| `sqlite3_step` / SQLite busy handler | Candidate B confirmed |
| Anywhere else | Both candidates dead; follow the stacks |

One dump replaces every remaining line of tracing in this investigation.

A cheaper corroborating test, if a dump is inconvenient: re-run `full.json` with `.WriteTo.Console`
removed, or wrapped in `Serilog.Sinks.Async`. If the collapse disappears, Candidate A is confirmed by
intervention.

Both require the observability gap closed first — set `fileSizeLimitBytes` and
`rollOnFileSizeLimit: true` on the file sink, otherwise the next run's log will stop before the
collapse again.

---

## Logging audit

The 1 GB file is not the problem; it is the receipt. The problem is what produced it.

### Composition — measured

`.loadtest/server/Logs/latest.log` sampled with `dd` at 100/300/500/700/900/1000 MB offsets:

| Offset | Dominant source |
|---|---|
| 0–100 MB | `LoginService`, `PlayerLogoutService` (login/idle phases) |
| 100 MB | `MatchCompleteHandler`, `MatchMembershipService` (multiplayer phase) |
| 300 MB → 1 GiB | **`ApiRequestLoggingMiddleware`, exclusively** |

Average line length in the API region: **261 bytes**. So the file holds roughly 4.11 million lines,
of which about **3.07 million (≈ 80 % of the gigabyte) are one-line-per-API-request records**, the
overwhelming majority of the form:

```
[2026-09-05 17:41:02.574 +07:00 INF] [Api] 0HNOB6CQVTAAE:00001E14 Basil.Web.Middleware.ApiRequestLoggingMiddleware: API request completed: GET /users/367 -> 200 in 0 ms { RequestPath: "/users/367", ConnectionId: "0HNOB6CQVTAAE", RemoteIp: "::ffff:127.0.0.1" }
```

During `api_health_500` this is `15,229 requests/second × 261 bytes ≈ 3.97 MB/s` of synchronous log
output for `GET /health -> 200 in 0 ms`, sustained for a minute — 913,782 lines.

### Defect A — silent truncation at 1 GB (CONFIRMED)

`latest.log` is exactly 1,073,742,070 bytes (1 GiB + 246 B). `Program.cs:306-318` configures both
file sinks with neither `fileSizeLimitBytes` nor `rollOnFileSizeLimit`, so each stops writing at
Serilog's 1 GB default and discards everything after, without an error.

Consequence: the log ended at 10:46 UTC, ten minutes before the collapse. This is the second
investigation in a row to lose its evidence to the same default.

### Defect B — one synchronous Information line per API request (CONFIRMED)

`ApiRequestLoggingMiddleware` (`src/Basil.Web/Middleware/ApiRequestLoggingMiddleware.cs:35-41`)
emits `LogInformation` for every completed `basilapi` request, unconditionally — success and
failure, `/health` and `/matches/{id}` alike. Two sinks receive every one of them.

This is production-relevant, not merely a load-test artifact: any tournament instance with a
polling client or an uptime monitor accumulates the same volume.

### Defect C — both sinks are synchronous and sit in the request path (SUPPORTED)

`.WriteTo.Console` and `.WriteTo.File` are used directly, neither wrapped in `Serilog.Sinks.Async`
(the package is not referenced). Serilog's console sink serializes on a process-wide lock and
writes-then-flushes per event.

That matters here because the load harness runs the server with
`RedirectStandardOutput = true` (`DotnetServerHost.cs:71`). A redirected pipe whose reader stalls
blocks the writing thread inside the lock, and every other thread that logs anything then queues
behind that lock. This is the mechanism behind Candidate A above.

Note the ordering: the file sink went silent at 10:46, so from that moment **all** log I/O in the
process went to the console pipe — and `api_health_500` began eight minutes later.

### Defect D — `UseSerilogRequestLogging()` builds an event per request and throws it away (CONFIRMED)

`Program.cs:167` registers `UseSerilogRequestLogging()`. Its events carry SourceContext
`Serilog.AspNetCore.RequestLoggingMiddleware`, which matches no rule in `CategoryEnricher.Rules`, so
it is tagged `FallbackCategory` and dropped by the `Filter.ByExcluding` at `Program.cs:301-304`.
The 1 GB sample contains zero such lines, confirming the suppression.

Two problems with that:

* Every request still pays for constructing, enriching and filter-evaluating a log event that is
  then discarded — on top of `ApiRequestLoggingMiddleware`'s line, which is kept.
* The suppression is accidental. `MinimumLevel.Override("Microsoft.AspNetCore", Warning)` at
  `Program.cs:296` does **not** cover it — the category filter does. Adding a rule for `Basil.Web.` or
  changing the fallback behaviour would silently double the log volume.

### Defect E — per-request allocation in the logging middleware (CONFIRMED)

`RequestIdLoggingMiddleware.cs:23`:

```csharp
var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
```

This materialises the entire header collection into a new dictionary, with a `ToString()` per header
value, **on every request on every host group** — only in order to test for the presence of two
header keys, which `context.Request.Headers.ContainsKey` answers without allocating.

The dictionary is then passed to `Geolocation.PhraseIpAddress` (`Geolocation.cs:15-22`:
`Split(',')` + `IPAddress.Parse`) and the result `.ToString()`-ed, followed by two
`LogContext.PushProperty` calls, each of which writes an `AsyncLocal` and so copies the
`ExecutionContext`.

At the 15,229 req/s this run reached, that is roughly 15k dictionaries, well over 100k strings and
30k `AsyncLocal` writes per second. It is why `GET /health` can never be free.

### Verdict on the user's question

*Is logging the problem?* Split into what the artifacts support:

| Claim | Status |
|---|---|
| Logging is why the investigation has no evidence of the collapse | CONFIRMED (Defect A) |
| Logging volume is disproportionate — 80 % of a gigabyte is one line per request | CONFIRMED (Defect B) |
| Logging costs measurable per-request work even when the event is discarded | CONFIRMED (Defects D, E) |
| Logging is the *mechanism* of the 10:56 collapse | HYPOTHESIS (Defect C + Candidate A) |

The first three are worth fixing on their own merits regardless of what the thread dump says. The
fourth is exactly what the dump decides.

---

## Defects found during this review, independent of the outage

1. **File sink silently truncates at 1 GB** — `Program.cs:306-318`. No `fileSizeLimitBytes`, no
   `rollOnFileSizeLimit`. Cost the previous investigation its evidence and cost this one too.
2. **Per-request Information logging is unbounded** — `ApiRequestLoggingMiddleware` logs one line per
   completed `basilapi` request, synchronously, to two sinks. 913,782 lines in one 60-second
   scenario. This is production-relevant, not only a load-test concern.
3. **Harness accumulates the server's entire stdout in memory** —
   `DotnetServerHost.cs:81`. `ReadToEndAsync` into a discarded task: unbounded growth, and any
   failure inside it is invisible. The apparatus can block the subject under test.
4. **Synchronous connection open on every repository call** — `SqliteConnectionFactory.cs:19-27`,
   21 call sites. Combined with `busy_timeout = 5000` and no concurrency bound on the database path.
5. **`MatchRoundEndOutbox` drains strictly serially** — `MatchRoundEndOutbox.cs:85-89`. One write at
   a time, each opening its own connection, against a bounded capacity of 128. Whatever made round-end
   writes slow at 10:39 would fill the queue at that drain rate. Separately, `DrainAsync`
   (lines 78-82) polls on a 10 ms `Task.Delay` with no timeout and never returns while the consumer
   is stalled; its only caller is `MatchMembershipService.cs:879`.

---

## Capacity envelope observed in this run

The project wants a scaling curve rather than a pass/fail number. This run supplies the first points
of one, all at commit `d5ca2d4` on 8 logical processors:

| Endpoint | Concurrency | RPS (ok) | p50 | p95 | p99 |
|---|---|---|---|---|---|
| `GET /health` (no DB) | 500 | 15,229 | — | — | — |
| `GET /users/{id}` (DB read) | 500 | 3,688 | 23.6 ms | 42.6 ms | 57.5 ms |
| `api_mixed` | 200 | — | — | — | mean 398 ms |

Everything at 500 concurrency after scenario 14 is unusable as capacity data — it measures the
collapsed state, not the server.

---

## Revised status of RC11

| Claim | Previous | Now |
|---|---|---|
| Server collapses under combined multiplayer + API load | CONFIRMED | CONFIRMED (10:56 UTC, this run) |
| Collapse is caused by SQLite write-path saturation | SUPPORTED | **NOT SUPPORTED** by this run's evidence |
| Outbox exhaustion precedes and contributes to the collapse | SUPPORTED | **CONTRADICTED** — 17 minutes and 4 healthy scenarios apart |
| DB-free health endpoint surviving isolates the DB | CONFIRMED | **NOT SUPPORTED** — explained by scenario ordering |
| Threads are blocked in a kernel wait during the collapse | — | CONFIRMED (0.1 % CPU, 488 threads, queue 2,004) |
| Blocking on the console sink via the harness's stdout pipe | — | HYPOTHESIS |
| Blocking in synchronous SQLite | — | HYPOTHESIS, narrowed to the write path |
