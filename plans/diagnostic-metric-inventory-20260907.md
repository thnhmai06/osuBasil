# Diagnostic API metric inventory (verified against .NET 10)

Date: 2026-09-07
Scope: candidate metrics for a `/diagnostic/{category}` API that broadcasts snapshots roughly once per second. Every row below was checked against the actual .NET 10 API surface — either by running a throwaway probe console app (`net10.0`, SDK 10.0.400, run on Windows 11 x64, Release config) or by querying `dotnet/runtime` and `dotnet/aspnetcore` source via deepwiki. Probe source: `C:\Users\haith\AppData\Local\Temp\claude\V--Code-cs-osuBasil\fb621edb-2a18-43fd-ad6f-72d763ebd5b9\scratchpad\metricprobe\Program.cs`. Full raw probe output is in the Appendix.

Platform note: the probe ran on Windows 11. Basil also ships in Docker (Linux containers). Every row that behaves differently on Linux is flagged explicitly in "Availability caveats" — do not assume Windows behavior carries over.

Legend for "Sampling cost": **field read** = reads an already-maintained counter/field, no lock, sub-microsecond; **cheap call** = a few tens to low hundreds of nanoseconds, no syscall; **syscall** = crosses into the OS kernel (memory info, times), measured in the probe at ~3.7–4.0 ms on Windows for a `Process` refresh; **allocates** = allocates a managed object per call (e.g. `GC.GetConfigurationVariables()` returns a `Dictionary<string, object>`); **requires running listener** = the value only exists/accumulates while a `MeterListener`/`EventListener` has been attached and left running — it cannot be point-sampled from a cold start.

---

## process

| Metric | How obtained (exact API or meter+instrument) | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Process ID | `Environment.ProcessId` (or `Process.GetCurrentProcess().Id`) | gauge; integer, constant for process lifetime | field read, <0.01 µs | Cross-platform. | Snapshot-only (never changes). |
| Uptime | `DateTime.Now - Process.GetCurrentProcess().StartTime` (`StartTime` is cached on first read of a `Process` handle you keep) | derived gauge; `TimeSpan` | `StartTime` itself is a syscall the first time a `Process` handle is queried (~4 ms, see below), then it's a cached field — cheap on every subsequent read since it never changes | `StartTime` throws `InvalidOperationException` if the process has exited (not applicable to "current process"). | Live-eligible cheaply once `StartTime` is cached once. |
| Working set (bytes) | `Environment.WorkingSet` **or** `Process.WorkingSet64` **or** Meter `System.Runtime` / `dotnet.process.memory.working_set` (`ObservableUpDownCounter<long>`) | gauge; bytes | `Environment.WorkingSet`: measured 0.209 µs/call (100k reads) — direct `GetProcessMemoryInfo` Win32 call, no handle caching needed. `Process.WorkingSet64` on a **cached, unrefreshed** `Process` object: 0.014 µs/call but stale (see caveat). Calling `proc.Refresh()` first costs ~3.72 ms/call (syscall). | `Process.WorkingSet64` is cached at the time the `Process` object was constructed or last `Refresh()`d — verified in probe: allocating 200 MB changed nothing until `Refresh()` was called. `Environment.WorkingSet` always reads live, no caching trap, and is the cheaper/safer choice. On Linux it also works (backed by `/proc`). | Live-eligible; use `Environment.WorkingSet`, not a cached `Process` field. |
| Private memory | `Process.PrivateMemorySize64` (needs a `Refresh()`'d or freshly-obtained `Process` handle) | gauge; bytes | Same caching trap as above — a syscall (~3.7 ms) if you call `Refresh()`, effectively free if you read a stale cached value. | No `Environment.*` shortcut exists for private bytes. On Linux, sourced from `/proc/<pid>/status`, generally supported. | Live-eligible, but only if you `Refresh()` once per second on a *shared, long-lived* `Process` instance — never call `Process.GetCurrentProcess()` fresh every read (measured 4.0 ms/call, i.e. more expensive than the whole 1-second broadcast budget if done per-metric). |
| Virtual memory | `Process.VirtualMemorySize64` | gauge; bytes | Same as PrivateMemorySize64 — bundled into the same underlying refresh syscall. | Same Refresh() caveat. Meaning differs a lot cross-platform (reserved address space vs. Linux VSZ from `/proc/<pid>/statm`) — treat as diagnostic-only, not comparable across OSes. | Live-eligible with the shared-`Process`+once-per-second-`Refresh()` pattern. |
| Peak working set | `Process.PeakWorkingSet64` | monotonic high-water-mark gauge; bytes | Same refresh-bundled cost as above. | On Linux this is `/proc/<pid>/status` `VmHWM` — supported. | Live-eligible (same caching rule), but it only ever grows, so 1 Hz sampling is overkill; snapshot every few seconds is enough. |
| CPU usage % | **Not directly available.** Derive from `Process.TotalProcessorTime` deltas: `(cpuTimeDelta / wallClockDelta / Environment.ProcessorCount) * 100`. Also exposed cumulatively (seconds, not %) via Meter `System.Runtime` / `dotnet.process.cpu.time` (`ObservableCounter<double>`, tagged by "user"/"system" mode), confirmed present in the probe. | derived rate; % (the raw API gives a cumulative counter, seconds of CPU time consumed since process start) | `TotalProcessorTime` costs the same Refresh() syscall as working set/private memory — it's bundled in the same OS call, so reading it alongside working set is free once you've already paid for the refresh. | Not available on Browser/WASI/iOS/tvOS platforms per runtime docs (irrelevant for Basil's server deployment). CPU% must be computed by the caller from two time-stamped samples — there is no built-in instantaneous CPU percentage anywhere in the BCL. | Live-eligible only as a *derived* rate: keep the previous `(cpuTime, wallClock)` pair and diff each tick. Do not report the raw cumulative seconds as if it were a percentage. |
| Thread count | `Process.Threads.Count` (bundled in Refresh()) — **not** `ThreadPool.ThreadCount`, which only counts pool worker threads, not the process's OS thread total | gauge; integer count of OS threads in the process | Bundled into the process refresh syscall; `Threads` itself materializes a `ProcessThreadCollection` (allocates) each time it's accessed even without an explicit `Refresh()`. | Cross-platform, but the collection allocates on every access — don't call `.Threads.Count` more than once per broadcast tick. | Live-eligible (once per second via the shared-Process pattern). |
| Handle count | `Process.HandleCount` | gauge; integer | Bundled into the process refresh syscall on Windows. | **Linux difference:** `HandleCount` is supported on Linux but has different semantics there — it counts open file descriptors by iterating `/proc/<pid>/fd`, which is directory-enumeration work, not a cheap kernel counter read like on Windows. On Windows it's a handle-table count from the process refresh block. Numerically these are not the same "thing" across platforms even though the property name is identical. | Live-eligible on Windows (bundled cost); on Linux it costs an extra directory scan beyond the normal refresh — treat as more expensive there and consider a lower sampling rate for that platform. |

**Process refresh pattern (applies to every "process" row that needs a syscall):** keep exactly one `Process.GetCurrentProcess()` instance alive for the process lifetime, call `proc.Refresh()` once per broadcast tick (measured ~3.7 ms on Windows, well inside a 1-second budget), then read every field off that single refreshed snapshot. Never call `Process.GetCurrentProcess()` per-metric (measured ~4.0 ms *per call*, i.e. it would alone eat the entire per-second budget if invoked more than once).

---

## memory

| Metric | How obtained | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Managed (GC heap) memory in use | `GC.GetTotalMemory(false)` | gauge; bytes, best-effort estimate of managed heap bytes currently allocated (not forced to collect first) | Probe: 0.116 µs/100k = negligible. `false` avoids forcing a blocking collection; `true` forces a full GC first (do not use `true` on a 1 Hz broadcast — it would itself cause GC pressure). | None significant. Cross-platform, cheap. | Live-eligible. |
| GC heap size (all generations) | `GC.GetGCMemoryInfo().HeapSizeBytes` | gauge; bytes, total heap size after the *last* completed GC | Probe: `GC.GetGCMemoryInfo()` averaged 0.052 µs/call — it just returns a cached struct populated at the end of the last GC, no lock, no syscall. | **Verified caveat, not obvious from docs:** in a process that hasn't yet had a garbage collection, every field on `GCMemoryInfo` (`HeapSizeBytes`, `FragmentedBytes`, `MemoryLoadBytes`, `TotalCommittedBytes`, `PromotedBytes`, generation sizes) reads back as `0`, and `Index`/`Generation` read `0` too — because the struct reflects the *last* GC, and there hasn't been one. This is a real snapshot API, not a live heap walk; it does not update between GCs. | Live-eligible in the "cheap to poll" sense, but semantically it only changes when a GC actually runs, so between GCs the value is flat — do not mistake "flat reading" for "no memory activity." |
| Fragmented bytes | `GC.GetGCMemoryInfo().FragmentedBytes` | gauge; bytes, fragmentation across generations as of the last GC | Same as above (bundled into the same struct read). | Same "post-last-GC snapshot, zero before first GC" caveat. | Live-eligible, updates once-per-GC not once-per-second. |
| Memory load | `GC.GetGCMemoryInfo().MemoryLoadBytes` | gauge; bytes, total physical/container memory in use system-wide (not just this process) as of the last GC | Same struct read, free. | Same "last GC" caveat. Also: this is *system-wide* memory pressure (or container-wide under cgroup limits), not this process's memory — don't present it under a process-scoped label. | Live-eligible, updates once-per-GC. |
| Available memory | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` | gauge; bytes, the memory limit the GC is budgeting against | Same struct read, free. | **Container-aware on both platforms**, confirmed via runtime source: on Linux under cgroup v1/v2 this reflects the cgroup memory limit (not host RAM); on Windows inside a Job Object it reflects the job's memory limit. On bare Windows (Basil's dev/probe environment) it showed the full host RAM (16.9 GB) in the probe. In Basil's Docker deployment this number will correctly track the container's memory cap — safe to expose as-is. | Snapshot-mostly-static (changes only if the container limit or host RAM configuration changes at runtime, which is rare) — do not broadcast at 1 Hz as if it moves; a slow poll (e.g. every 10–60 s) is sufficient. |
| High memory load threshold | `GC.GetGCMemoryInfo().HighMemoryLoadThresholdBytes` | gauge; bytes, the `MemoryLoadBytes` level above which the GC becomes more aggressive | Same struct read, free. | Same container-awareness as `TotalAvailableMemoryBytes` (it's derived from it, ~90% by default per `GCHighMemPercent` in `GetConfigurationVariables()`, confirmed probe value `90`). | Snapshot-only — this is effectively a configuration-derived constant for the process lifetime. |
| Working set | See `process` category (`Environment.WorkingSet`). Also listed here because "memory" and "process" categories both plausibly claim it — pick one canonical owner to avoid duplicate metric names in two categories. | gauge; bytes | See above. | See above. | Live-eligible. |
| Private memory | See `process` category (`Process.PrivateMemorySize64`). Same duplication note. | gauge; bytes | See above (Refresh-bundled syscall). | See above. | Live-eligible via the shared-Process-refresh pattern. |

---

## gc

| Metric | How obtained | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Gen0/1/2 collection counts | `GC.CollectionCount(0)`, `GC.CollectionCount(1)`, `GC.CollectionCount(2)` **or** Meter `System.Runtime`/`dotnet.gc.collections` (probe confirms `ObservableCounter<long>`, tagged by generation, 3 separate measurements: 1, 0, 10 in one sample — one value per generation) | **cumulative counter** (monotonically increasing since process start), not a gauge | Probe: `GC.CollectionCount(0)` 0.023 µs/100k — trivial field read. | None significant; cross-platform. | Live-eligible as a counter; the API's consumer should diff between ticks to get a "collections per second" rate rather than displaying the raw cumulative total as if it resets. |
| Generation sizes (gen0/1/2 heap size) | `GC.GetGCMemoryInfo().GenerationInfo[n].SizeAfterBytes` (also `SizeBeforeBytes`, `FragmentationBeforeBytes`, `FragmentationAfterBytes` per generation) **or** Meter `dotnet.gc.last_collection.heap.size` (probe confirms `ObservableUpDownCounter<long>`, one measurement per generation — probe saw 5 measurements: 560, 68096, 0, 0, 8184 for gen0..gen4-ish/LOH/POH slices) | gauge; bytes, per generation, as of last GC | Free (bundled into the same `GCMemoryInfo` struct read as heap size above). | `GenerationInfo.Length` was `5` in the probe (gen0, gen1, gen2, LOH, POH — confirms LOH/POH are exposed as generation-info slots, not separate top-level properties). Same "zero before first GC" caveat as heap size. | Live-eligible, updates once-per-GC. |
| LOH size | `GC.GetGCMemoryInfo().GenerationInfo[3]` (index 3 = LOH in the 5-slot `GenerationInfo` array observed in the probe) — there is no separate "LOH size" property; it's a `GenerationInfo` slot | gauge; bytes | Free, same struct. | Index mapping (0=gen0,1=gen1,2=gen2,3=LOH,4=POH) is implementation-observed from the probe's `GenerationInfo.Length == 5`, not a documented enum — verify against `GCGenerationInfo` docs/`GC.MaxGeneration` before hard-coding indices in production code; safer to identify slots relative to `GC.MaxGeneration` rather than a literal `3`. | Live-eligible, updates once-per-GC. |
| POH size | `GC.GetGCMemoryInfo().GenerationInfo[4]` (Pinned Object Heap, .NET 5+) | gauge; bytes | Free, same struct. | Same indexing caveat as LOH. | Live-eligible, updates once-per-GC. |
| Fragmentation | `GC.GetGCMemoryInfo().FragmentedBytes` (total) or per-generation via `GenerationInfo[n].FragmentationAfterBytes` | gauge; bytes | Free. | Same "last GC" snapshot caveat. | Live-eligible, updates once-per-GC. |
| GC mode (Workstation vs Server) | `GCSettings.IsServerGC` | gauge; boolean | Field read, effectively free. | **This is fixed at process startup** (set via `runtimeconfig.json`/`.csproj` `<ServerGarbageCollection>`) and cannot change while the process runs. | **Snapshot-only.** Read once at startup; never re-poll or broadcast every second. |
| Concurrent (background) GC enabled | `GCSettings.LatencyMode` is *not* the concurrent-GC flag — use `GC.GetConfigurationVariables()["ConcurrentGC"]` (probe confirmed key `ConcurrentGC = True`, `Boolean`) to read the configured concurrent-GC setting, or `GCMemoryInfo.Concurrent` for whether the *most recent* GC specifically ran concurrently | Two different things: the **configured** setting is a fixed boolean for the process; the **per-GC** `Concurrent` flag on `GCMemoryInfo` reflects only the last GC. | `GetConfigurationVariables()` **allocates** a `Dictionary<string, object>` boxing ~30 config values every call — do not call this every second; call it once at startup and cache the result. `GCMemoryInfo.Concurrent` is free (bundled struct read). | Confirmed via probe: `GC.GetConfigurationVariables()` returns 30 keys including `ServerGC`, `ConcurrentGC`, `RetainVM`, `GCHeapHardLimit*`, `GCRegionSize`, `GCDynamicAdaptationMode`, etc. — useful as a one-time config dump, not a per-tick metric. | Configured value: **snapshot-only**. Per-GC `Concurrent` flag: live-eligible but changes only once-per-GC. |
| Latency mode | `GCSettings.LatencyMode` (enum: `Batch`, `Interactive`, `LowLatency`, `SustainedLowLatency`, `NoGCRegion`) | gauge; enum, can change at runtime if application code calls `GCSettings.LatencyMode = ...` | Field read, free. | Probe showed `Interactive` (the default) for an unconfigured console app. | Effectively snapshot-only for Basil unless the app deliberately toggles latency modes at runtime (check the codebase — if nothing sets this, treat as fixed and poll rarely). |
| Time-in-GC / pause time | **Two independently-verified sources, both cumulative, neither is "% time in GC":** (1) `GC.GetTotalPauseDuration()` — a direct static method, returns cumulative `TimeSpan` paused since process start; confirmed present and callable in .NET 10 (probe returned `00:00:00` early in a process's life, i.e. correctly near-zero before much GC activity). (2) Meter `System.Runtime`/`dotnet.gc.pause.time` — confirmed `ObservableCounter<double>`, unit seconds, probe observed `0.001781` — same cumulative quantity, exposed as an OTel-shaped instrument. There is **no** built-in "percent time in GC" gauge; `GCMemoryInfo.PauseTimePercentage` exists and *is* a percentage but it is scoped to the **last GC only** (confirmed by probe: `PauseTimePercentage = 0` before any GC), not a rolling window. | `GC.GetTotalPauseDuration()`: cumulative counter (seconds paused, all-time). `GCMemoryInfo.PauseTimePercentage`: gauge, % of wall-clock spent paused **during the most recent GC only**, not a global/rolling rate. | Both are cheap field reads (no allocation, no lock) — same cost class as `GetTotalMemory`. | This was explicitly flagged by the task as "must not be assumed" — confirmed: it is *not* a live "time in GC right now" percentage. To get a genuine "% time in GC over the last second" you must diff `GetTotalPauseDuration()` between two 1-second-apart samples and divide by the wall-clock delta yourself. | `GetTotalPauseDuration()`: live-eligible as a cumulative counter, diff between ticks. `PauseTimePercentage`: live-eligible but only updates once-per-GC and represents that one GC's pause ratio, not a smoothed rate — labeling it as an ongoing "% time in GC" would be misleading without saying "as of the last GC." |
| Allocation rate | **Not directly available** — derive from `GC.GetTotalAllocatedBytes(false)` deltas between ticks (bytes/sec), or from Meter `dotnet.gc.heap.total_allocated` (`ObservableCounter<long>`, confirmed in probe, cumulative bytes allocated since start) | derived rate; bytes/sec, computed by the caller from a cumulative counter | Probe: `GC.GetTotalAllocatedBytes(false)` 0.021 µs/100k — trivial. `GetTotalAllocatedBytes(true)` forces a GC-precise total (more expensive, do not use on a 1 Hz path). | None significant for the `false` overload. | Live-eligible as a diffed rate; never expose the raw cumulative total as "the allocation rate." |

---

## threadpool

| Metric | How obtained | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Thread count | `ThreadPool.ThreadCount` **or** Meter `dotnet.thread_pool.thread.count` | gauge in meaning (current pool thread count, can go up or down), **but probe shows it is implemented as an `ObservableCounter<long>`, not an `ObservableUpDownCounter`, contradicting the OTel-semantic-convention description found in documentation** — trust the probe: the runtime source (`RuntimeMetrics`) registers it as `ObservableCounter`. Report it as a gauge in your API regardless of the .NET-side instrument class, since its actual values do go down (confirmed conceptually, not exercised in this idle-pool probe). | Probe: `ThreadPool.ThreadCount` 0.008 µs/100k — a plain field read, no lock. | None significant. | Live-eligible. |
| Worker threads: min / max / available | `ThreadPool.GetMinThreads(out worker, out _)`, `GetMaxThreads(out worker, out _)`, `GetAvailableThreads(out worker, out _)` | Min/Max: **configuration snapshot** (integers, can be changed at runtime via `SetMinThreads`/`SetMaxThreads` but rarely are). Available: gauge = Max − currently-busy. | Field reads, effectively free (no measurable cost distinct from `ThreadPool.ThreadCount` in the probe's timing loop). | Probe values: min worker = 8 (≈ `Environment.ProcessorCount`), max worker = 32767 (the .NET default ceiling), available worker = 32767 (idle pool, nothing running). | Min/Max: snapshot-only (poll rarely, they almost never change). Available worker count: live-eligible. |
| Completion port threads (min/max/available) | Same three `ThreadPool.Get*Threads` calls, `iocp`/completionPortThreads out-parameter | **Confirmed vestigial on the modern (portable) thread pool, which is what .NET 10 uses on both Windows and Linux by default.** Probe values: min iocp = 1, max iocp = 1000, available iocp = 1000 — fixed legacy compatibility numbers (`_legacy_minIOCompletionThreads` = 1, `_legacy_maxIOCompletionThreads` = 1000 in the runtime source), **not a real, actively-managed I/O completion pool** on the portable thread pool. | Reading them costs nothing extra (same call as worker threads), but the *values themselves carry no operational signal*. | **This is exactly the metric the task flagged as "must not be assumed."** Verified: it is not a live measure of I/O thread pressure; it is a hard-coded legacy value maintained only so old code calling these APIs doesn't break. Recommend excluding it (see Recommended exclusions). | N/A — do not expose as a live metric; it never meaningfully changes. |
| Thread pool queue length | `ThreadPool.PendingWorkItemCount` **or** Meter `dotnet.thread_pool.queue.length` | gauge; count of items currently queued, not yet started. **Probe again shows this is implemented as `ObservableCounter<long>`, not `ObservableUpDownCounter`** despite the semantic being a fluctuating gauge — a second confirmed doc/implementation mismatch, trust the probe. | Probe: `ThreadPool.PendingWorkItemCount` 0.022 µs/100k — plain field read, no lock. **`ThreadPool.PendingWorkItemCount` is directly available as a static property in .NET 10** — it is *not* EventCounter-only on this runtime (it historically was EventCounter-only in early .NET Core; this has changed). | None significant. Confirmed present and cheap; no `MeterListener`/`EventListener` needed at all if you use the static property directly. | Live-eligible, cheap enough for 1 Hz. |
| Completed work items | `ThreadPool.CompletedWorkItemCount` **or** Meter `dotnet.thread_pool.work_item.count` | **cumulative counter** since process start, not a rate | Probe: 0.018 µs/100k — plain field read. | None significant. | Live-eligible as a counter; diff between ticks for a "work items/sec" rate — do not present the raw cumulative value as a rate. |
| Lock contention count | `Monitor.LockContentionCount` **or** Meter `dotnet.monitor.lock_contentions` | **cumulative counter** since process start, not a rate. **Directly available as a static property — confirmed working, no EventCounter/Meter needed.** | Probe: 0.045 µs/100k reads — plain field read. Probe also forced real contention (two threads racing a lock) and confirmed the counter incremented by exactly 1. | This was flagged by the task as "must not be assumed" — confirmed it *is* directly readable via `Monitor.LockContentionCount`, no listener required, and it is cumulative (diff between ticks for a rate). The Meter instrument (`dotnet.monitor.lock_contentions`, `ObservableCounter<long>`) mirrors the same underlying counter — either source is fine, but the static property is simpler and needs no listener plumbing. | Live-eligible as a diffed rate. |

---

## runtime

| Metric | How obtained | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Runtime version | `RuntimeInformation.FrameworkDescription` (e.g. `.NET 10.0.11`, confirmed in probe) or `Environment.Version` (`10.0.11`) | gauge; string/version, fixed for process lifetime | Field read, free. | `Environment.Version` gives the CLR version number; `FrameworkDescription` gives the human-readable framework name+version — pick one, they're redundant. | **Snapshot-only** — never changes while running; read once at startup. |
| Server GC (yes/no) | `GCSettings.IsServerGC` — duplicate of the `gc` category row | Same as above. | Field read, free. | Fixed at process start. | Snapshot-only. |
| Concurrent GC (yes/no) | `GC.GetConfigurationVariables()["ConcurrentGC"]` — duplicate of the `gc` category row | Same as above. | Allocates a dictionary if called live; cache the one-time read instead. | Fixed at process start. | Snapshot-only. |
| Processor count | `Environment.ProcessorCount` | gauge; integer, logical CPU count available to the process | Field read, free. Probe: `8` on the dev machine. | Can theoretically change at runtime if the container's CPU affinity/quota changes (rare, and .NET doesn't always immediately reflect an external cgroup CPU quota change without a restart) — but for practical purposes treat as fixed. | Snapshot-only (poll rarely, e.g. on demand or once per minute at most). |
| Architecture | `RuntimeInformation.ProcessArchitecture` and `RuntimeInformation.OSArchitecture` (both confirmed `X64` in probe) | gauge; enum, fixed | Field read, free. | Distinct concepts: process architecture (bitness of *this* process, relevant if running x86 on x64 OS) vs. OS architecture (the host's native architecture) — don't conflate them into one field. | Snapshot-only. |
| OS | `RuntimeInformation.OSDescription` (confirmed `Microsoft Windows 10.0.26200` in probe) | gauge; string, fixed | Field read, free. | On Linux this returns a distro/kernel string (e.g. `Linux 5.x...`) via `uname` — different format, same API, no code change needed. | Snapshot-only. |

---

## exceptions

| Metric | How obtained | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Total thrown count | Meter `System.Runtime` / `dotnet.exceptions` **only** — confirmed via probe's `InstrumentPublished` callback that this instrument exists and is registered as a synchronous `Counter<long>` (push-based, **not** `ObservableCounter`) tagged by `error.type` (the exception type name). **There is no static BCL property anywhere for a raw exception count** — this is the metric the task most explicitly said "must not be assumed," and the probe confirms there is genuinely no direct field/property; a Meter listener is the only path. | **Cumulative counter, push-based** — the runtime increments it at the moment an exception is thrown (inside the CLR's exception-dispatch path), it does not accumulate lazily and is not observable/pull-based like the GC gauges. | Cost is paid by the runtime at throw time (a few extra instructions per `throw`), which is negligible relative to the cost of throwing itself; reading the *accumulated total* from your own code has no extra cost, but only if a listener has been continuously attached. | **This is the critical caveat the task called out**: because it is push-based, **you cannot point-sample it** — if no `MeterListener` (or an OTel `MeterProvider`) has been running continuously since some baseline, you have no way to retroactively know how many exceptions were thrown in the interval. The diagnostic API's background service must attach a `MeterListener` once at startup and keep it running for the process lifetime, accumulating a running total (or per-`error.type` breakdown) in memory that the 1 Hz broadcast then reads. Reading the accumulator itself is a cheap field read once you own it; standing the listener up is a one-time cost, not a per-tick cost. | Live-eligible **only if** the diagnostic service owns a long-lived `MeterListener`; the count itself, once accumulated, is a cumulative counter — diff between ticks to get "exceptions/sec." |
| Unhandled count | **Not available via any Meter or BCL API.** `dotnet.exceptions` counts *every* thrown exception (including caught-and-handled ones), not specifically unhandled ones. To count unhandled exceptions you must subscribe to `AppDomain.CurrentDomain.UnhandledException` (fires just before the process would terminate — only fires for exceptions that truly escape all handlers on a non-thread-pool thread; on a thread-pool thread or `Task` an unobserved exception instead surfaces via `TaskScheduler.UnobservedTaskException`, which is a materially different and narrower signal). Not verified further by probe (would require deliberately crashing the probe process, which is out of scope for a metrics inventory). | Would be an app-level cumulative counter you maintain yourself in the handler, not something the runtime exposes as a pre-built metric. | Handler registration is free; the handler itself only fires on the (rare, fatal) unhandled path, so it contributes no steady-state cost. | Genuinely distinct from "total exceptions" — do not derive "unhandled count" from `dotnet.exceptions`; that instrument cannot tell you whether an exception was ultimately caught. `AppDomain.UnhandledException` firing usually means the process is about to crash — by the time you'd report this metric over the diagnostic API, the process may already be going down, so its practical value as a *live, polled* metric is low (more useful as a one-shot crash-log event than a broadcast gauge). | Effectively an event, not a poll-able metric — do not model it as a 1 Hz gauge. |
| Exception rate | Derived: diff the `dotnet.exceptions` accumulator (see "Total thrown count") between ticks, divide by elapsed seconds. | derived rate; exceptions/sec | Same cost profile as "Total thrown count." | Same push-based/listener-required caveat. | Live-eligible once the listener-backed accumulator exists. |

---

## http

All rows in this category require actual ASP.NET Core hosting (`Microsoft.AspNetCore.Hosting` / `Microsoft.AspNetCore.Server.Kestrel` Meters) — **confirmed by probe**: in a plain console process (no Kestrel host started), attaching a `MeterListener` for the entire process lifetime discovered **zero** instruments from either meter name; only the `System.Runtime` meter's 19 instruments appeared. Per `dotnet/aspnetcore` source (via deepwiki), `HostingMetrics`/`KestrelMetrics` are only constructed when the hosting/Kestrel pipeline is actually built with an `IMeterFactory` injected, and their `ObservableUpDownCounter`/`Histogram` instruments only emit non-zero values once real requests/connections flow through — they are not "always on" the way the `System.Runtime` meter is.

| Metric | How obtained (meter/instrument) | Semantic | Sampling cost | Availability caveats | Live or snapshot-only |
|---|---|---|---|---|---|
| Active requests | Meter `Microsoft.AspNetCore.Hosting` / `http.server.active_requests` (`UpDownCounter<long>`) | gauge; incremented at request start, decremented at request end | Requires an already-running `MeterListener`/OTel `MeterProvider` attached to a live `WebApplication` host — cannot be point-sampled without one; cost of reading an accumulated value from your own listener is a field read. | Needs an `IMeterFactory` wired into hosting (this is automatic in a standard `WebApplicationBuilder` in .NET 10 — `AddMetrics()` is on by default via the hosting pipeline — but you must still attach a listener to *consume* the values; ASP.NET Core does not expose a "current value" property anywhere outside the Meter API). Zero traffic ⇒ the instrument may not have emitted any measurement yet at all, not just "zero." | Live-eligible once a listener owns the running total. |
| Request rate | Derived: count `http.server.request.duration` `Histogram` measurements (each recorded measurement = one completed request) per second via a listener-side counter you maintain. | derived rate; requests/sec | Same "requires running listener" cost profile. | The histogram itself gives you durations, not a count directly, but every recorded measurement corresponds to exactly one completed request, so counting measurements gives you the rate. | Live-eligible via listener-maintained counter. |
| Failed request rate | Derived from `http.server.request.duration` measurements whose `error.type` tag is set (non-null tag = failed: either the exception type name for unhandled exceptions, or the numeric status code string for a 5xx with no exception) | derived rate; failures/sec | Same listener-based cost profile. | Confirmed via deepwiki/source: `error.type` is only present on failed requests, absent (or a defined "success" indicator) otherwise — filter on tag presence, not on a separate instrument. | Live-eligible via listener-maintained counter. |
| Request duration | Meter `Microsoft.AspNetCore.Hosting` / `http.server.request.duration` (`Histogram<double>`, seconds) | Distribution, not a single gauge — recorded once per completed request. | Listener-based; histograms require bucket-aggregation logic in the listener callback (sum/count/percentiles), more CPU than a plain gauge read, but still bounded by number of requests, not by the 1 Hz tick. | Do not try to expose "the" request duration as a single number without deciding what aggregation (p50/p95/avg over the last second) the API will report — a histogram is not naturally a single value. | Live-eligible as an aggregate over the last broadcast interval, not as a raw single reading. |
| Active connections | Meter `Microsoft.AspNetCore.Server.Kestrel` / `kestrel.active_connections` (`UpDownCounter<long>`) | gauge; incremented on connection start, decremented on stop | Listener-based, same profile as active requests. | Distinct from `http.server.active_requests` — a single HTTP/2 or HTTP/3 connection can multiplex many concurrent requests, so these two numbers are not interchangeable and both are worth exposing separately if you care about multiplexing behavior. | Live-eligible via listener. |
| Connection rate | Derived from `kestrel.connection.duration` (`Histogram<double>`) measurement count per second, same pattern as request rate. | derived rate; connections/sec | Listener-based. | Also consider `kestrel.rejected_connections` (`Counter<long>`, e.g. `MaxConcurrentConnections` exceeded) and `kestrel.queued_connections`/`kestrel.queued_requests` (`UpDownCounter<long>`) as related, not-explicitly-requested-but-relevant Kestrel-pressure signals — cheap to add once the listener infrastructure exists for this meter. | Live-eligible via listener. |

**Implementation note for the whole `http` category:** because these Meters are entirely absent until ASP.NET Core hosting actually constructs them, the diagnostic background service must attach its `MeterListener` (or register with the existing OTel `MeterProvider` if Basil already has one) once at application startup — after `WebApplication.Build()`/`Run()` has wired up hosting — and keep it running for the app's lifetime, exactly as with the `dotnet.exceptions` counter. A per-tick "create a listener, sample, dispose" approach will not work for these instruments: the histogram/gauge state lives in the listener's accumulated callbacks, not in any queryable static property.

---

## Recommended exclusions

| Candidate | Reason for exclusion |
|---|---|
| Completion port thread min/max/available (`ThreadPool.Get*Threads` `iocp` out-param) | **Confirmed vestigial** on the portable thread pool that .NET 10 uses by default on both Windows and Linux (probe: fixed values 1/1000/1000, unaffected by any actual I/O load). Exposing it invites operators to draw conclusions from a number that carries no operational signal — actively misleading. |
| `Process.VirtualMemorySize64` | Its meaning differs so much cross-platform (Windows reserved address-space bytes vs. Linux `statm` VSZ) that a single "virtual memory" number in a dashboard shared between Windows dev and Linux Docker prod is more confusing than useful; keep it out of the always-on set and reserve it for a one-off diagnostic dump if ever needed. |
| Unhandled exception count as a polled/broadcast metric | It's an event (fires once, usually as the process is dying), not a steady-state value — a 1 Hz gauge implies "how many right now," which doesn't apply. If wanted at all, it belongs in a log/alert path (fire an event when `AppDomain.UnhandledException` triggers), not the periodic snapshot broadcast. |
| Raw `Process.VirtualMemorySize64`/`PrivateMemorySize64` **per-metric** `Process.GetCurrentProcess()` calls | Not a metric-identity issue but a *pattern* to explicitly forbid in the implementation: measured at ~4.0 ms per call for a fresh handle. At 1 Hz with even 2–3 metrics doing this independently, you'd blow well past the budget and add real load. Must share one cached, `Refresh()`'d `Process` instance. |
| `GC.GetConfigurationVariables()` polled every tick | Allocates a ~30-entry dictionary on every call. It's genuinely useful as a one-time "runtime config" snapshot (server GC on/off, heap hard limits, region size, dynamic adaptation mode, etc.) but should be read once at startup and cached, never included in the 1 Hz path. |
| `GC.GetTotalAllocatedBytes(true)` (the "precise" overload) | The `true` overload forces the runtime to walk allocation contexts to get an exact number instead of an approximation — meaningfully more expensive than `false`, and on a 1 Hz diagnostic loop the approximation is more than adequate. Use `false`. |
| `GCMemoryInfo.PauseTimePercentage` presented as a live "% time in GC" | It's scoped to the *last individual GC*, not a rolling window — showing it as a continuously-updating "time in GC" percentage misrepresents what it measures (confirmed by probe: it reads `0` before any GC has happened, then jumps to whatever the *one* most recent GC's ratio was, and stays flat between GCs). If a smoothed rate is wanted, compute it from `GetTotalPauseDuration()` deltas instead. |
| High-cardinality `error.type` tag values from `dotnet.exceptions` / `http.server.request.duration` broadcast as separate time series per distinct exception/status value | The tag can carry arbitrary exception type full names — if user code throws many distinct exception types, this is unbounded cardinality for a dashboard/time-series backend. Aggregate to a single "total" number (and optionally a small fixed set of buckets, e.g. "5xx" vs "exception") rather than exposing per-`error.type` breakdowns as first-class metrics. |
| `Process.Threads.Count` polled at high frequency for its own sake | Confirmed the `Threads` property materializes a `ProcessThreadCollection` (an allocation) on every access, distinct from the free "field read" pattern of most other process counters — fine at 1 Hz, but don't reach for it in any tighter loop or per-request code path. |
| `TotalAvailableMemoryBytes` / `HighMemoryLoadThresholdBytes` at 1 Hz | These only change when the container/host memory configuration itself changes (essentially never at runtime). Broadcasting them every second is wasted bandwidth for a value that is realistically snapshot-only; poll on a much slower cadence (e.g. once per minute, or once at connect-time to the diagnostic stream) instead. |

---

## Appendix: raw probe output

```
===== Environment / RuntimeInformation =====
Environment.ProcessorCount = 8
Environment.OSVersion = Microsoft Windows NT 10.0.26200.0
Environment.Is64BitProcess = True
Environment.WorkingSet = 21336064
Environment.TickCount64 = 96816312
Environment.Version = 10.0.11
RuntimeInformation.FrameworkDescription = .NET 10.0.11
RuntimeInformation.RuntimeIdentifier = win-x64
RuntimeInformation.OSDescription = Microsoft Windows 10.0.26200
RuntimeInformation.OSArchitecture = X64
RuntimeInformation.ProcessArchitecture = X64

===== GCSettings =====
GCSettings.IsServerGC = False
GCSettings.LatencyMode = Interactive
GCSettings.LargeObjectHeapCompactionMode = Default

===== GC static methods =====
GC.GetTotalMemory(false) = 60384
GC.GetTotalAllocatedBytes(false) = 60288
GC.GetTotalAllocatedBytes(true) = 59048
GC.CollectionCount(0) = 0
GC.CollectionCount(1) = 0
GC.CollectionCount(2) = 0
GC.MaxGeneration = 2
GC.GetTotalPauseDuration() = 00:00:00

===== GC.GetGCMemoryInfo() - default kind =====
Index = 0
Generation = 0
Compacted = False
Concurrent = False
HeapSizeBytes = 0
FragmentedBytes = 0
MemoryLoadBytes = 0
HighMemoryLoadThresholdBytes = 15200781926
TotalAvailableMemoryBytes = 16889757696
TotalCommittedBytes = 0
PromotedBytes = 0
PinnedObjectsCount = 0
FinalizationPendingCount = 0
PauseTimePercentage = 0
PauseDurations = [00:00:00, 00:00:00]
GenerationInfo.Length = 5
  Gen[0] SizeBeforeBytes=0 SizeAfterBytes=0 FragmentationBeforeBytes=0 FragmentationAfterBytes=0
  Gen[1] SizeBeforeBytes=0 SizeAfterBytes=0 FragmentationBeforeBytes=0 FragmentationAfterBytes=0
  Gen[2] SizeBeforeBytes=0 SizeAfterBytes=0 FragmentationBeforeBytes=0 FragmentationAfterBytes=0
  Gen[3] SizeBeforeBytes=0 SizeAfterBytes=0 FragmentationBeforeBytes=0 FragmentationAfterBytes=0
  Gen[4] SizeBeforeBytes=0 SizeAfterBytes=0 FragmentationBeforeBytes=0 FragmentationAfterBytes=0

===== GC.GetGCMemoryInfo(GCKind.Any) =====
(identical to above in this pre-first-GC state)

===== GC.GetConfigurationVariables() =====
  ServerGC = False (Boolean)
  ConcurrentGC = True (Boolean)
  RetainVM = False (Boolean)
  NoAffinitize = False (Boolean)
  GCCpuGroup = False (Boolean)
  GCLargePages = False (Boolean)
  LOHThreshold = 85000 (Int64)
  HeapCount = 1 (Int64)
  MaxHeapCount = 0 (Int64)
  GCHeapAffinitizeMask = 0 (Int64)
  GCHeapAffinitizeRanges =  (String)
  GCHighMemPercent = 90 (Int64)
  GCGen0MaxBudget = 6291456 (Int64)
  GCHeapHardLimit = 0 (Int64)
  GCHeapHardLimitPercent = 0 (Int64)
  GCRegionRange = 33779515392 (Int64)
  GCRegionSize = 0 (Int64)
  UOHWaitBGCSizeIncPercent = 200 (Int64)
  GCHeapHardLimitSOH = 0 (Int64)
  GCHeapHardLimitLOH = 0 (Int64)
  GCHeapHardLimitPOH = 0 (Int64)
  GCHeapHardLimitSOHPercent = 0 (Int64)
  GCHeapHardLimitLOHPercent = 0 (Int64)
  GCHeapHardLimitPOHPercent = 0 (Int64)
  GCConserveMem = 0 (Int64)
  GCName =  (String)
  GCPath =  (String)
  GCDynamicAdaptationMode = 1 (Int64)
  GCDTargetTCP = 0 (Int64)
  GCDGen0GrowthPercent = 0 (Int64)
  GCDGen0GrowthMinFactor = 0 (Int64)
  GCDGen0GrowthMaxFactor = 0 (Int64)

===== GC.RefreshMemoryLimit() =====
GC.RefreshMemoryLimit() succeeded (void return).

===== ThreadPool =====
ThreadPool.ThreadCount = 0
ThreadPool.PendingWorkItemCount = 0
ThreadPool.CompletedWorkItemCount = 0
ThreadPool.GetMinThreads -> worker=8, iocp=1
ThreadPool.GetMaxThreads -> worker=32767, iocp=1000
ThreadPool.GetAvailableThreads -> worker=32767, iocp=1000

===== Monitor =====
Monitor.LockContentionCount = 0
Monitor.LockContentionCount after forced contention = 1 (delta=1)

===== JitInfo =====
JitInfo.GetCompiledILBytes(false) = 9750
JitInfo.GetCompiledMethodCount(false) = 18
JitInfo.GetCompilationTime(false) = 00:00:00.0131209

===== Process.GetCurrentProcess() =====
Id = 12444
ProcessName = metricprobe
StartTime = 09/07/2026 21:49:53
Uptime (now - StartTime) = 00:00:00.3890257
WorkingSet64 = 25395200
PrivateMemorySize64 = 8200192
VirtualMemorySize64 = 2237565259776
PeakWorkingSet64 = 25395200
HandleCount = 221
Threads.Count = 8
TotalProcessorTime = 00:00:00.0781250
UserProcessorTime = 00:00:00.0625000
PrivilegedProcessorTime = 00:00:00.0156250

-- Testing Process property caching --
WorkingSet64 before alloc = 25395200
WorkingSet64 after 200MB alloc, NO Refresh() = 25395200 (same as before: True)
WorkingSet64 after proc.Refresh() = 25210880

===== Timing: 100k reads of cheap APIs (Stopwatch, ticks -> ms) =====
GC.GetTotalMemory(false) x100000: total=11.60ms, per-call=0.1160us
GC.GetTotalAllocatedBytes(false) x100000: total=2.11ms, per-call=0.0211us
GC.CollectionCount(0) x100000: total=2.31ms, per-call=0.0231us
GC.GetGCMemoryInfo() x100000: total=5.20ms, per-call=0.0520us
ThreadPool.PendingWorkItemCount x100000: total=2.20ms, per-call=0.0220us
ThreadPool.ThreadCount x100000: total=0.82ms, per-call=0.0082us
ThreadPool.CompletedWorkItemCount x100000: total=1.79ms, per-call=0.0179us
Monitor.LockContentionCount x100000: total=4.46ms, per-call=0.0446us
Environment.WorkingSet x100000: total=20.89ms, per-call=0.2089us
Environment.TickCount64 x100000: total=0.72ms, per-call=0.0072us
cached Process.WorkingSet64 (no Refresh, field read) x100000: total=1.43ms, per-call=0.0143us
Process.GetCurrentProcess() + WorkingSet64 x2000: total=8015.39ms, per-call=4007.695us
proc.Refresh() + WorkingSet64 x2000: total=7444.51ms, per-call=3722.255us

===== MeterListener probe: which meters/instruments actually publish =====
Instruments discovered so far (before RecordObservableInstruments):
  System.Runtime :: dotnet.assembly.count (ObservableUpDownCounter`1) unit={assembly}
  System.Runtime :: dotnet.exceptions (Counter`1) unit={exception}
  System.Runtime :: dotnet.gc.collections (ObservableCounter`1) unit={collection}
  System.Runtime :: dotnet.gc.heap.total_allocated (ObservableCounter`1) unit=By
  System.Runtime :: dotnet.gc.last_collection.heap.fragmentation.size (ObservableUpDownCounter`1) unit=By
  System.Runtime :: dotnet.gc.last_collection.heap.size (ObservableUpDownCounter`1) unit=By
  System.Runtime :: dotnet.gc.last_collection.memory.committed_size (ObservableUpDownCounter`1) unit=By
  System.Runtime :: dotnet.gc.pause.time (ObservableCounter`1) unit=s
  System.Runtime :: dotnet.jit.compilation.time (ObservableCounter`1) unit=s
  System.Runtime :: dotnet.jit.compiled_il.size (ObservableCounter`1) unit=By
  System.Runtime :: dotnet.jit.compiled_methods (ObservableCounter`1) unit={method}
  System.Runtime :: dotnet.monitor.lock_contentions (ObservableCounter`1) unit={contention}
  System.Runtime :: dotnet.process.cpu.count (ObservableUpDownCounter`1) unit={cpu}
  System.Runtime :: dotnet.process.cpu.time (ObservableCounter`1) unit=s
  System.Runtime :: dotnet.process.memory.working_set (ObservableUpDownCounter`1) unit=By
  System.Runtime :: dotnet.thread_pool.queue.length (ObservableCounter`1) unit={work_item}
  System.Runtime :: dotnet.thread_pool.thread.count (ObservableCounter`1) unit={thread}
  System.Runtime :: dotnet.thread_pool.work_item.count (ObservableCounter`1) unit={work_item}
  System.Runtime :: dotnet.timer.count (ObservableUpDownCounter`1) unit={timer}

Forcing observable instrument sampling via listener.RecordObservableInstruments():
  [long] System.Runtime/dotnet.gc.collections = 1
  [long] System.Runtime/dotnet.gc.collections = 0
  [long] System.Runtime/dotnet.gc.collections = 10
  [long] System.Runtime/dotnet.process.memory.working_set = 35241984
  [long] System.Runtime/dotnet.gc.heap.total_allocated = 268266880
  [long] System.Runtime/dotnet.gc.last_collection.memory.committed_size = 6443008
  [long] System.Runtime/dotnet.gc.last_collection.heap.size = 560
  [long] System.Runtime/dotnet.gc.last_collection.heap.size = 68096
  [long] System.Runtime/dotnet.gc.last_collection.heap.size = 0
  [long] System.Runtime/dotnet.gc.last_collection.heap.size = 0
  [long] System.Runtime/dotnet.gc.last_collection.heap.size = 8184
  [long] System.Runtime/dotnet.gc.last_collection.heap.fragmentation.size = 456
  [long] System.Runtime/dotnet.gc.last_collection.heap.fragmentation.size = 672
  [long] System.Runtime/dotnet.gc.last_collection.heap.fragmentation.size = 0
  [long] System.Runtime/dotnet.gc.last_collection.heap.fragmentation.size = 0
  [long] System.Runtime/dotnet.gc.last_collection.heap.fragmentation.size = 0
  [double] System.Runtime/dotnet.gc.pause.time = 0.001781
  [long] System.Runtime/dotnet.jit.compiled_il.size = 24346
  [long] System.Runtime/dotnet.jit.compiled_methods = 182
  [double] System.Runtime/dotnet.jit.compilation.time = 0.0689739
  [long] System.Runtime/dotnet.monitor.lock_contentions = 1
  [long] System.Runtime/dotnet.thread_pool.thread.count = 0
  [long] System.Runtime/dotnet.thread_pool.work_item.count = 0
  [long] System.Runtime/dotnet.thread_pool.queue.length = 0
  [long] System.Runtime/dotnet.timer.count = 0
  [long] System.Runtime/dotnet.assembly.count = 19
  [long] System.Runtime/dotnet.process.cpu.count = 8
  [double] System.Runtime/dotnet.process.cpu.time = 0.296875
  [double] System.Runtime/dotnet.process.cpu.time = 15.203125

Total distinct instruments published across ALL meters in this console process: 19
(Microsoft.AspNetCore.Hosting / Kestrel / System.Net.Http meters never appeared: 0 instruments,
confirming they require the hosting/HttpClient subsystems to actually be constructed with an
IMeterFactory and receive traffic — a bare console app that never hosts Kestrel or issues an
HttpClient request creates none of those Meter instances.)
```

### Notes on documentation-vs-probe discrepancies

Two disagreements between what documentation/deepwiki described and what the probe's `InstrumentPublished` callback actually reported (probe wins per task instructions):

1. **`dotnet.thread_pool.thread.count`** — documentation/OTel semantic-convention description characterizes this as a gauge (`ObservableUpDownCounter`); the probe shows it registered as `ObservableCounter<long>` in this .NET 10 build.
2. **`dotnet.thread_pool.queue.length`** — same discrepancy: documented as an up/down gauge, probe shows `ObservableCounter<long>`.

Both are still reported by consuming code as instantaneous gauges in this document's tables (that's what they measure), but note the actual CLR instrument type mismatch in case downstream OTel/Prometheus exporters treat `ObservableCounter` as strictly monotonic and clip or reject decreasing values.
