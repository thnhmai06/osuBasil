# ADR-006 — Storage

> Status: **Accepted (partial).** Not a decision gate. Phase 5 of the perf-investigation plan is
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
leftover temp files; true concurrent-read atomicity relies on the OS's rename guarantee rather
than something a unit test can meaningfully simulate.
