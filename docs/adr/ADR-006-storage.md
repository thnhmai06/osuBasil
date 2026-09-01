# ADR-006 — Storage

> Status: **Accepted (partial).** Retry contract for `PutAsync` decided 2026-09-01 (see Decision).
> Not a decision gate. Phase 5 of the perf-investigation plan is
> explicitly secondary: "storage rewrite is not a prerequisite for core concurrency scalability
> unless profiling shows it contributes meaningfully to the capacity envelope" — that profiling
> has not been run this session (Phase 6 is where re-profiling happens, and even there only for
> items the profile actually implicates). This ADR records one concrete, low-risk fix landed this
> session and the larger open items deferred pending either a design decision or evidence that
> they matter for the capacity envelope.

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
  code, not a quick patch — deferred.
- **`ReconcileAllAsync` runs inside a request.** `POST /beatmapsets` (`HandleCreate`) calls
  `BeatmapIngestionService.ReconcileAllAsync`, a full-disk reconciliation pass, synchronously as
  part of handling a single upload, and returns its result (`{ ingested }`) as the response body.
  Moving this off the request path is not just a threading change: the response contract
  currently depends on the full reconciliation's count. A redesign needs to decide either an
  async/202-style response (matching `PUT`/`DELETE /beatmapsets/{id}`'s existing async pattern)
  or scoping the reconciliation to just the newly-uploaded archive instead of a full-disk sweep.
  Deferred pending that decision — not a same-session fix.
- **ffmpeg thumbnail generation**: an N-miss workload spawns N processes with no stampede guard.
  Same category of fix as the `.osz` stampede guard above; deferred alongside it since both need
  the same single-flight mechanism and are natural to design together.
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
