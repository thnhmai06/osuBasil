# ADR-006 — Storage

> Status: **Partially accepted, partially proposed.** Retry contract for `PutAsync` decided and
> implemented 2026-09-01 (see "Decision (implemented this session)" below) — that part is not a
> decision gate. The `.osz` direct-storage design (see "`.osz` direct storage — design [GATE]"
> further down, added 2026-09-03) **is** a `[GATE]` item: a real architecture flip of the beatmap
> storage subsystem, proposed here but **not yet approved and not implemented**. Per plan rule 4,
> no code should be written against it until its Decision section is approved.

> Phase 5 of the perf-investigation plan is explicitly secondary: "storage rewrite is not a
> prerequisite for core concurrency scalability unless profiling shows it contributes meaningfully
> to the capacity envelope" — that profiling has not been run this session (Phase 6 is where
> re-profiling happens, and even there only for items the profile actually implicates). This ADR
> records one concrete, low-risk fix landed 2026-09-01, a full design for the largest deferred item
> (`.osz` direct storage) written 2026-09-03 at the user's request, and the remaining open items
> still deferred pending either their own design decision or evidence that they matter for the
> capacity envelope.

## Decision (implemented this session)

**Atomic cache writes.** `FileSystemResponseCache.PutAsync` wrote directly to an entry's final
path; two concurrent regenerations of the same not-yet-cached entry could race, and a reader
could observe a partially-written file mid-write. It now writes to a sibling temp file and
renames it into place — a rename is atomic on the same volume, so a concurrent `GetAsync` for
the same key always observes either the previous complete file or the new complete one, never a
torn one.

> **Platform caveat found on review:** this guarantee is POSIX rename semantics. On Windows
> (this project's primary dev/deploy target), `File.Move(tempPath, path, overwrite: true)`
> replacing a destination that another handle currently has open can throw `IOException`
> (sharing violation) instead of silently succeeding, depending on the reader's share mode and
> the exact instant the two operations overlap — `GetAsync`'s `File.ReadAllBytesAsync` opens and
> closes quickly, keeping the window narrow, but narrow is not zero. The failure mode this
> produces is a **throw on the writer**, never a torn read on the reader — `GetAsync` itself
> either fully succeeds against the pre- or post-rename file or isn't affected — so the atomicity
> claim for readers holds; what's uncovered is that a concurrent writer can occasionally fail
> with an exception rather than always succeeding silently. `PutAsync`'s caller should treat that
> exception as retryable-or-logged, not assume it can't happen on this platform.

> **Gap found on review — atomic ≠ single-writer:** the rename guarantee this fix provides is
> only "a reader never sees a torn file." It says nothing about two concurrent writers. Two
> `PutAsync` calls for the same key racing each other both write their own temp file and both
> rename successfully (no contention between two *renames* the way there can be between a rename
> and a concurrent *read*) — whichever rename lands last silently wins, with no ordering
> guarantee between the two callers' content. Today nothing in this codebase requires
> single-writer semantics for a cache regeneration (last-writer-wins is an acceptable outcome for
> a response cache, not a durability violation), so this is not treated as a bug — but it is an
> explicit gap, not an implied guarantee of this fix, and should be called out if any future
> caller needs deduplication/single-flight around concurrent regeneration of the same key (the
> same category of stampede guard already flagged as needed for `.osz`/ffmpeg below — that would
> be a separate decision, not a corollary of the atomic-write fix).

> **Gap found on review, not fixed in this pass — orphaned temp file on write failure:**
> `PutAsync` writes `tempPath`, then calls `File.Move(tempPath, path, true)` with no
> try/catch around the move. If the move throws (the Windows sharing-violation case above, or
> any other I/O failure), `tempPath` is left on disk — nothing deletes it. This is a real,
> uncorrected gap: the invariant "a failed write leaves no temp file behind" (asked for on
> review) does **not** hold today. Separately, the invariant "a failed write leaves the previous
> entry at `path` valid and untouched" **does** already hold as a structural consequence of using
> `File.Move` (a failed move never partially applies — `path` is either the pre-existing file,
> untouched, or the fully-renamed new one; there is no partial state), but this has no test
> pinning it either. Both invariants are correctness requirements this ADR should have stated
> explicitly rather than leaving implicit in the mechanism: **(a)** old entry survives a failed
> `Put` unchanged, **(b)** a failed `Put` never leaves a `*.tmp` file behind. (a) holds by
> construction; (b) does not and needs a fix (a try/catch around the `Move` call that deletes
> `tempPath` on failure before rethrowing) plus a regression test — left for the implementation
> pass that acts on this review, since this ADR review is documentation-only.

> **Note found on review — mechanism vs. requirement:** this ADR's actual architectural
> requirement is "replace an entry atomically, using whatever OS-level guarantee a rename
> provides." `File.Move(..., overwrite: true)` is the specific API chosen to satisfy it, not
> itself the requirement — a future implementation swapping it for `File.Replace` or another
> atomic-rename primitive (e.g. if the orphaned-temp-file gap above is fixed by changing
> mechanism rather than adding a catch block) is still satisfying this ADR's Decision, not
> deviating from it. If a future change wants to lock in a specific API rather than "atomic
> replacement" as the requirement, that should be raised as its own decision, not inferred from
> this wording.

> **Decided — retry contract for the writer-side exception:** `PutAsync` retries internally, a
> small bounded number of attempts with a short backoff, before letting the exception propagate.
> This covers the narrow Windows sharing-violation race without pushing a retry decision onto
> every caller — the cache is a speed-up, not a source of truth, so absorbing a transient
> collision internally is appropriate; a failure that persists past the retry budget still
> propagates rather than being silently swallowed, so a real, non-transient problem (disk full,
> permissions) is still observable to the caller.

## Open items (confirmed by code reading, not implemented this session)

- **`.osz` zip-on-demand.** Beatmap downloads decompress the archive into a `MemoryStream` and
  call `.ToArray()` per request rather than storing/streaming extracted assets — real allocation
  cost (up to ~200MB LOH for a 100MB set) with no cache or stampede guard on the extraction
  itself. Issue #4 asks for storing `.osz` content directly instead. This is a genuine storage
  architecture decision (what gets stored, what gets extracted on demand vs. cached, single-
  flight around a concurrent stampede on a cold cache) that deserves its own design pass before
  code, not a quick patch — **design written 2026-09-03, see "`.osz` direct storage — design
  [GATE]" below; still not implemented, awaiting approval.**
- **`ReconcileAllAsync` runs inside a request.** `POST /beatmapsets` (`HandleCreate`) calls
  `BeatmapIngestionService.ReconcileAllAsync`, a full-disk reconciliation pass, synchronously as
  part of handling a single upload, and returns its result (`{ ingested }`) as the response body.
  Moving this off the request path is not just a threading change: the response contract
  currently depends on the full reconciliation's count. A redesign needs to decide either an
  async/202-style response (matching `PUT`/`DELETE /beatmapsets/{id}`'s existing async pattern)
  or scoping the reconciliation to just the newly-uploaded archive instead of a full-disk sweep.
  Deferred pending that decision — not a same-session fix.
- ~~**ffmpeg audio-preview generation**: an N-miss workload spawns N processes with no stampede
  guard.~~ **Fixed 2026-09-04** (per-beatmapset single-flight lock around
  `BanchoHostGroups.BuildAudioPreviewAsync`'s `extractor.ExtractAsync` call, matching
  `ScoreSubmissionService`'s existing `ChecksumLocks` compare-and-remove pattern). This is the
  method's only production `IResponseCache` get-miss-build-put caller, so it also closes the
  general "`FileSystemResponseCache` has no stampede guard" gap noted above for the one case that
  actually exists today — no other caller regenerates cache content on a miss, so no generic
  cache-level guard was added on top (would be unused abstraction, CLAUDE.md rule 2). Regression
  test `AudioPreviewSingleFlightTests.Preview_ConcurrentRequests_ExtractsOnlyOnce`, verified by
  temporarily reverting the fix (5 concurrent requests → 5 extractions instead of 1).
- **`noVideo` variant, osu!direct search improvements**: feature work from Issue #4, not a
  correctness or performance fix — out of this investigation's scope (see
  `working-scopes.md`-style triage: these are product features, not bugs).

## Why these are deferred rather than fixed now

Per the plan's own priority ordering, this phase is secondary specifically because none of the
confirmed root causes (RC1: SQLite write path; the ThreadPool-saturation failure mode) trace
back to storage. The items above are real code-quality/robustness gaps, worth fixing, but
fixing them under this session's "do phase 2 through 6" instruction without re-profiling first
would risk exactly what the plan warns against: assuming a fix matters for capacity without the
evidence to back it. The atomic-write fix was made an exception because it is a correctness bug
(a torn read is observably wrong, not just slow) with a self-contained, near-zero-risk fix — the
others are either bigger design decisions (`.osz`, `ReconcileAllAsync`) or pure feature work.

## Measurements

Not applicable to the deferred items (no code changed). The atomic-write fix has direct test
coverage (`FileSystemResponseCacheTests`) verifying behavior preservation and the absence of
leftover temp files, but only sequentially — no test exercises a `GetAsync` racing a `PutAsync`
against the same key. True concurrent-access behavior relies on the OS's rename guarantee rather
than something a unit test can meaningfully simulate, and on Windows specifically that guarantee
is a possible writer-side sharing-violation exception under a tight race (see the caveat in
Decision above), not a torn read — untested, not unsafe.

**Test gap found on review, not yet written:** an integration/stress test that runs `GetAsync`
and `PutAsync` concurrently against the same key (many iterations, tight loop, on the actual
filesystem rather than a mock) to observe real behavior under contention — specifically whether
a `GetAsync` ever throws or returns anything other than a complete previous/new value, and
whether `PutAsync` ever throws the Windows sharing-violation case in practice on this project's
target filesystem. This is what would turn the sharing-violation caveat and the "readers never
see a torn file" claim from code-reasoning into observed behavior. Left for the implementation
pass, not written as part of this documentation-only review.

---

## `.osz` direct storage — design [GATE]

> Status: **Proposed 2026-09-03, not approved, not implemented.** Written at the user's explicit
> request to design this item ("Thiết kế storage .osz trực tiếp"), continuing from the "Open
> items" entry above. This is a `[GATE]` decision per plan rule 4 — no code against it until the
> Decision section is approved.

### Problem

Issue #4 (Storage): *"Store beatmapsets directly as `.osz` files instead of zipping on demand.
Cache audio/image assets when reading the archive. Automatically invalidate caches when
beatmaps/beatmapsets are deleted. Populate/refresh caches when beatmaps/beatmapsets are added or
replaced."*

Today, ingestion (`BeatmapIngestionService.ReconcileOszAsync`) extracts every entry of an
uploaded/dropped `.osz` into a per-set folder (`"{Id} {Artist} - {Title}"`, holding every `.osu`,
image, audio, and video file as-is) and **deletes the original `.osz`** — the archive itself is
never kept. Every other read path (background/cover images, thumbnails, audio previews, `.osu`
file serving, difficulty analysis, video lookup) reads directly from that extracted folder via a
real file path (`BeatmapIngestionService.BackgroundFilePath`/`AudioFilePath`/`OsuFilePath`/
`VideoFilePath`, all `StorageOptions`-relative path builders). Downloading the set back out as a
`.osz` (`GET .../download`, both the `assets.` host route and the in-game `osu-search.php`
mirror-fallback path) does the reverse on **every single request**:
`BanchoHostGroups.BuildBeatmapsetArchiveAsync` walks the extracted folder, `ZipArchive`-writes
every file into an in-memory `MemoryStream`, and returns `zipStream.ToArray()` — no cache, no
stampede guard, no streaming. For a 100MB set this is real allocation cost (the copied file bytes
plus the `MemoryStream`'s internal doubling buffer, up to roughly 200MB, large enough to land on
the LOH) repeated for every download of every set, including N concurrent downloads of the same
popular set during a tournament.

### Evidence

- `BanchoHostGroups.BuildBeatmapsetArchiveAsync` (`src/Basil.Web/Routing/Bancho/
  BanchoHostGroups.cs`): confirmed by direct reading — full in-memory zip build per request, no
  `IResponseCache` involvement, no single-flight guard. Called from both
  `BeatmapsetAssetRoutes.cs` (`assets.` host) and `OsuWebRoutes.cs` (in-game client).
- `BeatmapIngestionService.ReconcileOszAsync`: `ExtractOszIntoFolderAsync(oszPath, targetFolder,
  ...)` followed immediately by `File.Delete(oszPath)` — the archive is never persisted after
  ingestion, confirmed by reading the method body.
- Every asset-serving integration point reads a real file path, not a byte stream or archive
  entry, and this is a property of the third-party libraries involved, not an incidental
  implementation choice:
  - `BeatmapsetBackgroundProvider`/`BeatmapThumbnailProvider` (ImageSharp.Web `IImageProvider`s)
    return a `PhysicalImageResolver(new FileInfo(path))` — ImageSharp.Web's resolver contract in
    this codebase is file-path-based; there is no byte-array/stream resolver already in use here.
  - `FfmpegAudioExtractor.ExtractAsync(string audioFilePath, ...)` shells out to an external
    `ffmpeg` process via `FFMpegArguments.FromFileInput(audioFilePath, ...)` — a real file path on
    disk, not a stream Basil controls in-process.
  - `IOsuCalculator.Analyze(string beatmapFilePath, ...)` (backed by `ppy.osu.Game`'s own beatmap
    decoder) is also file-path-based, and is called from **two** places with different lifetimes:
    `BeatmapIngestionService.TryAnalyze` at ingestion time, and `BeatmapsetRoutes.HandleBeatmapDifficulty`
    (`GET /beatmapsets/{id}/{beatmapId}/difficulty`) at arbitrary request time, recomputing with
    different mods against the same `.osu` file. A design that only makes `.osu` content
    available during ingestion does not cover the second call site.
  - **Conclusion from this evidence:** switching the canonical store to `.osz` cannot avoid an
    extraction step somewhere — every one of these integrations needs a real file on disk for the
    specific asset it wants. The "cache" Issue #4 asks for is not an optional speed-up on top of
    the storage change; it is the mechanism that keeps these integrations working at all once raw
    per-file extraction on ingest goes away.
- `BeatmapWatcherService` currently watches the whole `BeatmapsetsPath` tree recursively
  (`IncludeSubdirectories = true`, `NotifyFilters.FileName | DirectoryName | LastWrite`) because a
  live beatmapset today *is* a folder of many individual files, each of whose changes must be
  noticed. It already has a working pathway for a **loose `.osz` file at the storage root**
  (`ReconcileOszAsync`, reached via `Settle`'s `looksLikeOsz` branch) — today used only
  transiently, for a file about to be extracted and deleted. This existing pathway is the natural
  seed for the new steady-state: a beatmapset becomes *permanently* a loose `.osz` file rather than
  a transient one.
- `BeatmapsetGarbageCollectorService`/`BeatmapsetRoutes.HandleDelete`'s rename-then-async-delete
  pattern (`Directory.Move(folder, folder + DeletedFolderInfix + guid)`, reclaimed on a 10-minute
  cycle) exists because deleting a folder can leave a locked file blocking the whole operation,
  requiring retry. A single `.osz` file has no such partial-failure mode — a file delete either
  fully succeeds or fully fails, atomically, unlike a directory tree.
- ADR-006's already-accepted atomic-write mechanism (temp file + rename, `FileSystemResponseCache
  .PutAsync`, top of this document) is a directly reusable building block for this design: the new
  cache's own writes need exactly the same "a concurrent reader never observes a torn file"
  guarantee, and there is no reason to invent a second mechanism for the same requirement.

### Constraints

- No silent behavior change to any currently-working download/asset-serving contract (plan rule
  2): `noVideo` downloads, background/cover/thumbnail resizing, audio previews, and difficulty
  recalculation must all keep returning the same bytes for the same input they do today.
- **Existing deployments already have data in the old shape** (extracted folders, no persisted
  `.osz`) — this design needs a real migration story, not just new-data-only behavior. This is
  qualitatively different from ADR-006's already-implemented atomic-write fix, which changed no
  on-disk shape at all.
- Every current asset consumer (`BeatmapsetBackgroundProvider`, `BeatmapThumbnailProvider`,
  `FfmpegAudioExtractor`'s caller, `IOsuCalculator.Analyze`'s two call sites, the `.osu`/video/
  audio direct-download routes) must still receive a real file path — this design cannot make any
  of them archive-aware without also reworking their third-party integration, which is out of
  scope here (see Evidence).
- The cache is disk space, not just CPU/memory savings: a naive "eagerly extract every entry of
  every set" policy roughly doubles beatmapset storage footprint (the compact `.osz` plus a full
  duplicate extracted copy), which works against the "lightweight" goal from `CLAUDE.md`'s own
  project description and the `README.md` overview just as much as the current zip-on-demand
  waste does — the design must bound this, not just move the waste from CPU to disk.
- Single-flight/stampede-guard requirement (Issue #4's implicit ask, "cache ... when reading the
  archive"): N concurrent requests for the same cold cache entry (e.g., a set that just went viral
  mid-tournament) must extract it once, not N times.

### Alternatives

**A. Store `.osz` only; extract every asset lazily on first request, cache indefinitely until
invalidated.**
Simplest mechanism — no eager-population step at all. Rejected as the sole mechanism because it
contradicts Issue #4's explicit "populate/refresh caches when beatmaps/beatmapsets are added or
replaced" — under this alternative, the *first* real request after every ingest/replace pays the
full extraction cost, exactly the stampede scenario the cache exists to prevent for a
just-updated, immediately-popular set (a tournament host replacing a map right before a round).

**B. Store `.osz` only; eagerly extract and cache *every* entry (images, every difficulty's
audio, every video) right after ingest, refresh on replace, delete-cache-directory on delete.**
Fully satisfies "populate/refresh on add/replace" literally, and is the simplest mental model (the
cache is always either "this set's complete assets" or "purged"). Rejected as the primary policy
because it reintroduces almost the same disk footprint as today's always-extracted design (video
files in particular are large and, outside the full-archive download, essentially never served
individually — `GET .../video` exists but is not a hot path) — it just relocates where the
duplication lives, without bounding it.

**C. Store `.osz` only; eagerly extract and cache only what the live read paths can actually
serve outside a full-archive download (every `.osu` file, the beatmapset's one recorded preview
background image, the beatmap(s) actually used for a preview/background/audio-preview
computation), lazily extract anything else (a specific difficulty's own background/audio when
directly requested, video) on first access, single-flight-guarded, cached until invalidated.**
Satisfies "populate/refresh on add/replace" for the assets that are actually likely to be
requested immediately after an add/replace (the set's own preview background/audio, used by the
listing/search UI right away), while bounding eager disk cost to roughly "one background image
plus one audio clip's source plus every `.osu` file" per set — small relative to a full duplicate,
and every `.osu` file needs extracting at ingest time anyway (Evidence: analysis already reads
them). Anything colder (a specific non-preview difficulty's own unique background, a video) is
extracted on the first real request and kept cached from then on, same as B from that point
forward.

**D. Keep today's "always fully extracted" folder as the canonical store; only fix the
zip-on-demand download path by caching the built archive.**
Smallest possible change — cache the `.osz` `BuildBeatmapsetArchiveAsync` already knows how to
build, keyed by set id (+ `noVideo` variant), invalidated the same way thumb/preview already are.
Rejected as the primary decision because it does not do what Issue #4 asks (*"store beatmapsets
directly as `.osz` files"* — the archive would still be a derived cache artifact, not the
canonical store) and keeps today's full-duplication disk footprint permanently, with none of
Alternative C's bound. Recorded here because its narrow mechanism (cache the built archive,
`noVideo` variant included) is still exactly what this ADR's Decision needs for the download path
itself — it is folded into the Decision below, not discarded.

### Decision

1. **Canonical store: a beatmapset is a single loose `.osz` file directly under
   `StorageOptions.BeatmapsetsPath`**, named by the existing convention
   (`BeatmapIngestionService.BeatmapsetFolderName`'s pattern, `.osz` extension instead of a
   directory) — the same shape `ReconcileOszAsync`'s already-working "loose `.osz` at the root"
   pathway handles today, just permanent instead of transient. Ingestion no longer deletes the
   archive after processing it; a replace (`PUT /beatmapsets/{id}`) overwrites this one file
   (atomically — see item 4) instead of re-extracting into a folder.
2. **A new extracted-asset cache, keyed by `(beatmapsetId, entryName)`, resolves to a real file
   path on disk**, physically rooted under `StorageOptions.CachePath` (sibling to the existing
   ImageSharp.Web resize cache and audio-preview cache, all already under `Data/Cache/` — cache in
   general is already documented as "safe to delete while Basil is stopped," and this new cache
   inherits that property). This is a distinct abstraction from the existing `IResponseCache`: that
   interface stores *processed output* (already-resized thumbnail bytes, already-transcoded
   preview bytes) behind a byte-array `Get`/`Put` contract; this new cache stores *extracted
   source* bytes and must hand callers a real path (Constraints, Evidence), so a byte-array
   contract does not fit its consumers. It reuses ADR-006's already-accepted atomic-write
   mechanism (temp file + rename) for every entry it writes, and a per-`(beatmapsetId, entryName)`
   keyed lock (matching this codebase's existing dedup-lock pattern, e.g. `ChecksumLocks`) around
   the extract-on-miss path, so concurrent requests for the same cold entry extract once.
3. **Population policy is Alternative C**: at ingest/replace, eagerly extract and cache every
   `.osu` file (needed for analysis regardless) plus the beatmapset's own recorded preview
   background and preview audio source. Every other entry (a specific difficulty's own unique
   background/audio, video, and the full-archive `noVideo` variant — see item 5) is extracted
   lazily on first real request, through the same single-flight-guarded cache, and kept from then
   on.
4. **Every current read path is rewired to resolve through this cache instead of a
   `BeatmapsetFolderPath`-relative file path**: `BackgroundFilePath`/`AudioFilePath`/`OsuFilePath`/
   `VideoFilePath`'s callers (the two ImageSharp.Web providers, `FfmpegAudioExtractor`'s caller,
   `IOsuCalculator.Analyze`'s two call sites, the `.osu`/video/audio direct-download routes) change
   to "resolve through the cache, extracting on miss" instead of "build a path directly under the
   beatmapset folder" — the resolved path is still a real `FileInfo`/file path, so none of the
   third-party integrations themselves need to change.
5. **Download becomes a cache-backed read, not a per-request build.** The plain `.osz` download is
   the canonical file itself — `File.OpenRead`/`Results.File` on the stored path directly, no
   `ZipArchive`/`MemoryStream` involved at all. The `noVideo` variant is Alternative D's narrow
   mechanism: build it once with the existing `BuildBeatmapsetArchiveAsync` logic, write it through
   the same extracted-asset cache (keyed as a set-level entry, e.g. `(beatmapsetId,
   "novideo.osz")`), and serve every subsequent `noVideo` request straight from that cached file.
6. **Cache invalidation is directory-scoped, not per-key.** Because every cache entry for a set
   lives under one `{beatmapsetId}`-keyed subdirectory, a delete or replace invalidates the whole
   set in one `Directory.Delete(setCacheDir, recursive: true)` — simpler than today's per-key
   `cache.DeleteAsync` calls (thumb small/large, preview) in
   `BeatmapIngestionService.DeleteBeatmapsetAsync`, which this replaces for beatmapset-scoped
   entries. A replace additionally re-runs the eager-population step (item 3) against the new
   archive content immediately after invalidating the old cache directory, so "refresh on replace"
   (Issue #4's wording) holds without a window where the set has no warm cache at all.
7. **`BeatmapWatcherService` watches `.osz` files at the storage root instead of a recursive
   folder tree.** `IncludeSubdirectories` becomes unnecessary (no more per-asset-file change
   events to track inside a set's folder — there is no such folder anymore); `NotifyFilters`
   narrows to file create/change/rename/delete on `*.osz` only. This is a real simplification of
   the watcher's job, a direct consequence of the storage shape change, not a separate decision.
8. **Migration for existing deployments**: on first startup after this change, a one-time pass
   over every *existing* extracted beatmapset folder (a) builds its `.osz` using the exact same
   zip-write logic `BuildBeatmapsetArchiveAsync` already has (run once and persisted, instead of
   per-request and discarded) at the new canonical location, then (b) moves the *existing*
   extracted folder's contents directly into the new asset-cache directory for that set id instead
   of re-extracting them — the folder's content is already exactly what the eager-population step
   (item 3) would produce, so migration doubles as a free cache pre-warm rather than a second
   extraction pass. This pass is additive/idempotent (skips a set that already has both a `.osz`
   and a populated cache directory), so it is safe to also run as an ordinary part of every
   startup rather than needing a separate one-shot migration flag.

### Trade-offs

- **Disk usage**: bounded, not eliminated. Alternative C's selective eager population plus lazy
  fill-on-demand means a fully "hot" set (every difficulty's assets requested at least once)
  converges to roughly the same total footprint as today's always-extracted model, just spread
  across a compact canonical `.osz` plus a cache that can, in principle, be wiped and rebuilt at
  any time (unlike today's extracted folder, which *is* the source of truth). A "cold" set (never
  requested since ingest) is smaller than today, since only `.osu`/preview assets are ever
  extracted for it.
- **New moving part**: a second on-disk representation (compact archive + derived cache) instead
  of one, with a real invariant to hold — "the cache is always either absent or a faithful
  extraction of the current `.osz`'s content" — that a bug could violate (e.g., a cache entry
  surviving a replace that should have invalidated it). Directory-scoped invalidation (Decision
  item 6) is deliberately chosen to make this invariant easy to reason about and hard to violate
  partially (no per-key list to keep in sync, unlike today's four explicit `cache.DeleteAsync`
  calls).
- **Complexity moves into `BeatmapIngestionService` and the watcher**, both of which already
  contain the ingest/reconcile logic and are the natural place for it, rather than introducing a
  new top-level service — but this still measurably grows those files' responsibility.
  `ReconcileFolderAsync`'s current per-`.osu`-file loop over `Directory.EnumerateFiles` becomes a
  per-archive-entry loop over `ZipArchive.Entries` instead; the shape of the reconciliation logic
  (resolve beatmapset, upsert each beatmap, drop ones no longer present) does not otherwise change.
- **Migration risk**: item 8 touches every existing beatmapset on a production server's first
  startup after upgrade. It needs its own test coverage (a fixture with a pre-existing extracted
  folder, verifying both the produced `.osz`'s content and the migrated cache directory) before
  being trusted against real deployment data — explicitly called out here so the implementation
  pass does not skip it.

### Measurements

**"Before" allocation/latency measured 2026-09-04** (`microbenchmarking` skill, BenchmarkDotNet,
`[MemoryDiagnoser]`, standalone throwaway project — `BuildBeatmapsetArchiveAsync` is `internal`
with no `InternalsVisibleTo` for a benchmark project, so its `MemoryStream` + `ZipArchive.Create` +
per-entry `CopyToAsync` + `.ToArray()` shape was reproduced standalone rather than referenced
directly; this measures that pattern's allocation shape, not literally the shipped method). Input:
synthetic incompressible data (real `.osz` contents — mp3/jpg/png/mp4 — are already compressed, so
random bytes approximate their DEFLATE behavior reasonably well), split across 6 files to exercise
the same per-entry loop as a real set, seeded RNG for reproducibility.

| Set size | Mean latency | Allocated | Gen2 collects/op |
|---|---|---|---|
| 10 MB | 226 ms | 42.2 MB (~4.2x) | 1.67 |
| 100 MB | 2.28 s | 357.4 MB (~3.6x) | 3.0 |

This **exceeds** the ADR's original ~2x/~200MB estimate — `MemoryStream`'s internal doubling
growth plus the `ZipArchive`'s own buffering adds more than the naive "one copy of the raw bytes,
one copy of the zipped bytes" reasoning accounted for. Gen2 collecting on every single call at
both sizes (not just the 100MB one) confirms this is genuine large-object-heap pressure on the
current per-request path, not a theoretical concern — this raises RC7's allocation-cost claim from
`NEEDS EXPERIMENT` to **CONFIRMED** for the *allocation/latency* half specifically. This benchmark
was development-feedback/throwaway per the skill's use-case guidance and was not committed to the
repo.

**Still not run** — before implementing the full Decision, per plan rule 6/the Definition of Done,
this still needs:

- Disk-usage measurement across a realistic mixed-popularity beatmapset library (many sets never
  requested outside ingest, a few "hot" ones) to confirm Alternative C's bound actually holds in
  practice rather than only in the worst-case reasoning above.
- Migration-pass timing against a large existing library (hundreds to low thousands of sets, matching
  a real tournament server's scale), since it runs on every startup until every set has both
  artifacts.
- The stampede-guard's actual effect under concurrent load (N simultaneous requests for the same
  cold cache entry) for whatever mechanism the eventual `.osz`/extracted-asset cache uses — the
  narrower ffmpeg-preview stampede case in this same ADR's "Open items" is now fixed and has this
  test (`AudioPreviewSingleFlightTests`), but that guard is scoped to that one caller, not the
  `.osz` cache this Decision proposes.

One measurement closing does not unpark this Decision by itself — the disk-usage and migration-
timing items above are still open, and the rewrite (canonical-store flip + deployment-data
migration) stays parked until they are too.
