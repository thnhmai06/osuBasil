# ADR-004 — SSE subsystem design contract

> Status: **Proposed — pending review.** No production code in this ADR's scope has been
> changed. This is the design contract required before Phase 3 (SSE rebuild) starts, per the
> perf-investigation plan's decision-gate rule. Written from direct reading of the current
> implementation (`IMatchLiveEvents`, `MatchLiveEvents`, `SnapshotChannel<T>`, `JsonMergePatch`,
> `LiveSseRoutes`, `MatchMembershipService`'s publish methods, `TeardownMatch`), not from the
> investigation's earlier secondhand notes — several details below correct or sharpen those
> notes against the actual code.

## Problem

osuBasil exposes 9 live SSE streams per match (`main`, `settings`, `hosts`, `refs`, `ban`,
`timer`, `slots`, per-slot `slot`/`score`/`input` combined, `chat`) plus a per-player `input`
stream. The current implementation has four independent defects that a straight "add more
streams" extension would only compound:

1. **No teardown signal.** A subscriber's lifetime is tied only to its own HTTP request's
   `CancellationToken` (client disconnect). `TeardownMatch` (`MatchMembershipService.cs`) cancels
   the match's timers and removes it from the registry, but never touches any subscriber's
   channel or event-handler subscription. A client still connected when its match closes keeps
   its event handler attached to the global, per-server `MainPublished`/`SettingsPublished`/etc.
   multicast delegates indefinitely, and — for `HandleLiveSlot` specifically, whose closures
   capture the `MatchSession` by reference — keeps that whole `MatchSession` object graph
   reachable from GC roots until the client eventually disconnects.
2. **Global publish, not per-match.** `MatchLiveEvents` (`Basil.Infrastructure/Sessions`) backs
   each event type with one C# multicast delegate shared by every match on the server; every
   subscriber for every match's `main` channel sits on the same `MainPublished` event, filtering
   by match id inside its own closure. Publishing one match's state change therefore invokes
   every subscriber of that event type across every currently-open match, not just this match's
   subscribers — an O(total global subscriber count) scan per publish, and every publish call
   site in `MatchMembershipService` runs this while the match's own `MatchSession.Lock` is held
   (via `EnqueueStateAsync` and friends), so one match's lock hold time is inflated by unrelated
   matches' spectator counts.
3. **`{}` spam.** `JsonMergePatch.Diff`/`DiffObjects` always returns a `JsonObject` (empty when
   nothing changed) for object-shaped payloads — it never returns `null` — so `SnapshotChannel<T>.
   Publish` always emits a patch, even a no-op `{}`, on every call. Every `EnqueueStateAsync` call
   (run after every packet mutation) republishes `main`/`settings`/every slot regardless of
   whether that particular channel's payload actually changed.
4. **`slots` stream is silent for packet-driven mutations.** `PublishSlotsAsync` (the whole-
   arrangement `SlotsSnapshot` channel behind `/matches/{id}/slots/live`) has exactly one call
   site, in `MatchControlService.cs:1193` (an HTTP PUT/PATCH path). None of the ~20 bancho packet
   handlers that mutate slots (`MatchChangeSlotHandler`, `MatchLockHandler`, `MatchReadyHandler`,
   and so on) call it — only the single per-slot `PublishSlot` inside `EnqueueStateAsync` does.
   A client watching `/slots/live` never sees a client-driven slot change.

This ADR is the design contract Phase 3 must satisfy before any of the above is touched, per the
plan's own rule that a rebuild needs its invariants settled first.

## Evidence

Everything in the Problem section was confirmed by direct code reading this session, not
inherited from the earlier investigation notes:
- `TeardownMatch`: `MatchMembershipService.cs` (`matchRegistry.Remove`, timer cancellation,
  lobby broadcast — no channel/subscriber touch).
- Global multicast: `Basil.Infrastructure/Sessions/MatchLiveEvents.cs` (`PublishMain` etc. simply
  invoke a single server-wide event); `LiveSseRoutes.cs` (every handler closure filters
  `if (id == matchId)` after being invoked for every match).
- `{}` spam: `Basil.Application/Services/Multiplayer/JsonMergePatch.cs` `DiffObjects` always
  returns a `JsonObject` (`new JsonObject()` at minimum); `Diff`'s top branch routes every
  object-shaped pair through it, never reaching the `null`-returning scalar branch.
- Silent `slots` stream: `grep PublishSlotsAsync` → exactly 1 call site
  (`MatchControlService.cs:1193`); `EnqueueStateAsync` calls only `PublishSlot` (singular, per
  index), never `PublishSlots`.
- Channel boundedness: `LiveSseRoutes.Subscribe` (chat, input) uses
  `Channel.CreateBounded<T>(32, DropOldest)`; `SubscribeWithSnapshot`/`SubscribeMultiWithSnapshot`
  (main, settings, hosts, refs, ban, timer, slots, the combined per-slot stream) use
  `Channel.CreateUnbounded<T>` — unbounded for every snapshot-backed stream.
- Diff scope: `SnapshotChannel<T>` holds one `_latest` per channel *per match* (each
  `MatchSession` owns its own `MainSnapshot`/`SettingsSnapshot`/etc. instances) — so the diff
  itself is already correctly scoped per match, per channel. What is *not* per-connection is that
  every subscriber to the same match's same channel receives the exact same patch computed
  against the single shared `_latest`; a client that just connected gets the fresh full
  snapshot (`readLatestSnapshot()` in `LiveSseRoutes`), but two already-connected clients cannot
  each be at a different point in the delta stream — there is one publish, one patch, broadcast
  to all.
- Design inputs already researched (this investigation, `docs/adr` sibling reference, not
  re-derived here): osu!api v2 match-event/game-object shapes (`ppy/osu-web`) as the structural
  reference for match event types and game objects — camelCase kept per Basil's existing
  envelope contract, only the *structure* borrowed.

## Constraints

- No silent behavior change to any *client-observed contract*: existing event types, snapshot
  shapes, and the "snapshot first, then patches" sequence must keep working for any client
  already built against them, unless a change is called out explicitly as a breaking change in
  the Decision below.
- `sse.md` currently documents per-connection diffing; the implementation does not do this
  today. This ADR must either bring the code to match the doc, or correct the doc — it must not
  leave them silently disagreeing (`docs-guideline.md`).
- Publish must never perform I/O and must never block on a slow consumer — true today via
  `TryWrite`/`DropOldest` where those are used; must stay true wherever the rebuild touches this.
- Publish must never run under `MatchSession.Lock` once this ADR's mechanism replaces the current
  one, restoring the "no I/O or unrelated-cost work inside the lock" discipline this
  investigation has been restoring everywhere else (RC1/RC4, ADR-003).

## Design contract

### Ownership

- Each `MatchSession` owns its own set of live channels (already true structurally via the
  per-match `SnapshotChannel<T>` instances and `SlotSnapshots` list). This ADR keeps that
  ownership model: a hub of channels lives on the match, not on a separate global registry keyed
  by match id.
- `TeardownMatch` becomes the single place that ends every subscriber of that match: it must
  complete (not just leave dangling) every channel the match owns, so every open
  `IAsyncEnumerable<SseItem<string>>` for that match observes end-of-stream and the HTTP response
  completes on its own, instead of waiting for the client to disconnect first.
- A subscriber that disconnects (its own request `CancellationToken` fires) removes only its own
  subscription — never affects other subscribers of the same match.

### Slow consumer / backpressure

- Every live channel becomes a bounded `Channel<T>` (matching what `chat`/`input` already do,
  extended to every other stream). `DropOldest` for state-oriented streams (see classification
  below) is correct — dropping an intermediate state is exactly what coalescing means. For
  event-oriented streams (chat, match events), dropping silently is not acceptable per the
  Constraints above (an audit-relevant message must not vanish without a trace); those channels
  either get a larger bound sized to realistic burst volume, or drop-oldest paired with an
  explicit "gap" marker item so the client can tell a message was lost instead of silently
  missing it.
- A specific bound (channel capacity, per stream type) is left to the implementation plan, not
  fixed here — it should be informed by the dedicated SSE benchmark this ADR requires before sign
  -off on the implementation (see Measurements).

### Event stream vs. state stream, and coalescing

| Stream | Classification | Coalesce? |
|---|---|---|
| `main` | State | Yes — latest only |
| `settings` | State | Yes |
| `hosts` | State | Yes |
| `refs` | State | Yes |
| `ban` | State | Yes |
| `timer` | State | Yes |
| `slots` (whole arrangement) | State | Yes |
| per-slot combined `slot` sub-event | State | Yes |
| `chat` | Event | No — ordered, no drop without a gap marker |
| per-slot `score` sub-event | Event | No |
| per-slot `input` sub-event | Event (high-frequency) | No drop *of the frame boundary*, but this is the stream most likely to need its own bound/rate discussion in the implementation plan given its volume |

A state-oriented stream only needs the client to converge on the current truth; an
intermediate state a slow client never saw is not a defect once the next state supersedes it —
this is exactly what `DropOldest` on a bounded channel gives for free, and it is the key to this
subsystem scaling with match count rather than with mutation rate. An event-oriented stream's
individual items are each meaningful on their own (a chat line, a score submission) and must
either all arrive, in order, or the gap must be visible to the client — never silently absorbed
into "the latest state."

### Snapshot authority

- The existing "subscribe, discard anything queued before the fresh read, then yield the fresh
  full snapshot, then forward the channel" sequence (`SubscribeWithSnapshot`/
  `SubscribeMultiWithSnapshot`) is correct and is kept: the snapshot is authoritative for
  everything up to the moment it was read, and only mutations after that moment arrive as
  patches.
- Ordering guarantee kept as-is: within one stream, a client never receives a patch that
  contradicts a snapshot or patch it already received (each `SnapshotChannel<T>.Publish` call is
  a single atomic snapshot-then-patch step against that one channel's own state).

### Per-connection state vs. global diff

`sse.md` (as it exists before this ADR) documents per-connection diffing; the actual code diffs
once per publish against one shared `_latest`, broadcasting the identical patch to every
subscriber of that match's that channel. This ADR's recommendation is to **keep the shared-diff
model, not build per-connection diffing**, for a concrete reason: every subscriber to the same
channel of the same match is, by construction, watching the same authoritative state — there is
exactly one `main` truth for a match, not one per viewer. Per-connection diffing would only earn
its complexity if different subscribers could legitimately observe different views of the same
channel (e.g. a privileged vs. a public view), which none of these 9 streams currently do. `sse.md`
should be corrected to describe the shared-diff model actually in use, rather than the code being
changed to match a doc that was never accurate. (If the actual implementation plan disagrees with
this recommendation, that disagreement should be resolved here, before code, not discovered again
during the rebuild.)

### Publish never under the match lock

Every `PublishXxx` call currently runs while `MatchSession.Lock` is held, and — per the Problem
section — that lock hold time is inflated by the O(global subscribers) scan inherent in the
current global-multicast design. Moving to per-match-owned channels (Ownership, above) already
shrinks a publish's cost to O(this match's subscribers) regardless of what else this ADR
decides; on top of that, the implementation plan must call publish *after* releasing the lock,
not inside the locked section, matching the same "no I/O or unrelated-cost work under the lock"
rule already applied elsewhere this investigation (RC1, RC4, ADR-003 draft). The state being
published must be captured (or its `SnapshotChannel<T>.Publish` call made) before the lock is
released if the freshly-mutated data is only safely readable under the lock; the network/queue
write itself must not require holding it.

### `slots` stream completeness

Every slot-mutating call path (currently: `EnqueueStateAsync`'s per-index `PublishSlot`, plus the
single `PublishSlotsAsync` call in the PUT/PATCH path) must also drive the whole-arrangement
`slots` channel. The implementation plan should route all slot mutation, packet-driven and
HTTP-driven alike, through the same call sequence, so there is exactly one place that decides
"the slot arrangement changed, publish both `slot` and `slots`" instead of two independently
maintained code paths that can silently diverge (as they already have).

### `{}` spam

`JsonMergePatch.Diff` must return `null` (and `SnapshotChannel<T>.Publish` must skip writing to
the channel entirely) when nothing in the diffed object actually changed. This is a small,
independent, low-risk fix (a null-check in `DiffObjects`'s return, and a null-check before
`channel.Writer.TryWrite` in `SnapshotChannel<T>.Publish`) that does not depend on any other
decision in this ADR — it can land as its own commit ahead of, or independent from, the rest of
Phase 3, since it changes only when a patch is sent, never what it contains when one is.

### DTO alignment

No change recommended to the wire shapes themselves in this ADR — the existing
`MatchLiveSnapshot`/`MatchSettingsView`/`MatchSlotView`/etc. records stay as they are. The
osu!api v2 structural reference gathered earlier in this investigation (event types, game
object, score match-context) remains available for Phase 4's DTO/OpenAPI audit, which is the
correct place for a naming/shape audit — this ADR is scoped to subsystem mechanics, not payload
shape.

## Alternatives considered

**Per-match, per-connection hub with per-viewer diff state.** Rejected for the reason given in
"Per-connection state vs. global diff" above: no current stream has viewer-dependent content, so
the added per-connection state (memory + bookkeeping proportional to subscriber count) buys
nothing over the shared-diff model, which already scales the diff cost with mutation rate, not
subscriber count.

**Drop `SnapshotChannel<T>` and republish full state on every mutation.** Rejected: this is the
"before" state the patch model already improved on (see `SnapshotChannel<T>`'s own doc comment),
and would multiply bandwidth for large `main` payloads (which include slot occupant details)
proportional to subscriber count and match size, with no compensating benefit.

## Decision

**Not yet made** — submitted for review. The author's recommendations, to be confirmed or
redirected before implementation:
1. Per-match owned channel hub; `TeardownMatch` completes every owned channel.
2. Bounded channels everywhere, `DropOldest` for state streams, an explicit gap marker (not
   silent drop) for event streams if their bound is ever exceeded.
3. Keep the shared-diff model; correct `sse.md` to describe it accurately instead of rebuilding
   the code to match an inaccurate doc.
4. Publish outside the match lock once channels are per-match (which alone removes the
   O(global subscribers) cost currently inflating lock hold time).
5. Unify slot-arrangement publishing so `slots` and per-slot `slot` always fire together,
   packet-driven or HTTP-driven.
6. Fix `{}` spam independently (low-risk, can land first).

## Trade-offs

Per-match channel ownership adds one more thing `TeardownMatch` must get right (completing every
channel, not just cancelling timers) — a new invariant to test, but a small, contained one.
Bounded channels with `DropOldest` for state streams mean a very slow client can miss
intermediate states entirely (already true today for the channels that are unbounded, in the
opposite failure mode: an unbounded channel to a dead-but-not-yet-disconnected client grows
without limit instead). Gap markers for event streams add one more item shape clients must
handle, a small addition to the wire contract that should be documented alongside whatever the
final implementation plan settles on for its exact shape.

## Measurements

Required before Phase 3 is considered done, per the plan's Definition of Done: a dedicated SSE
benchmark, not folded into the general multiplayer load scenario — 10/50/100 matches × N SSE
clients each, measuring event throughput, CPU, allocation rate, RSS, match-lock wait time, and
channel backlog depth (already instrumented: `BasilMetrics.SseBacklogDepth`/
`SseActiveSubscribers`). Success = correctness (every stream delivers every mutation exactly
once per its classification's ordering rule) *and* no regression in these resource metrics
versus the Phase 0 baseline, plus a soak run exercising match open/close churn with SSE clients
remaining connected through a close, verifying RSS stays flat (the leak this ADR's Ownership
section targets) and connections observably end when their match closes.
