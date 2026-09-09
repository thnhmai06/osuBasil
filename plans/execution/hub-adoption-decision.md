# How the multiplayer slice adopts the event hub

**Decided 2026-09-09. This is Task B5's design question, settled before a worker starts.**

## What the hazard was thought to be

Task 0.10's advisor review flagged a race. `LiveSubscription.OnPublish` has a branch that runs when
the subscription has no snapshot yet:

```csharp
if (!_hasSnapshot)
{
    _snapshot = payload;
    _version = version;
    _hasSnapshot = true;
    return;
}
```

The payload it adopts comes from `StateStream.Publish`, which returns a **merge-patch delta** — it
only returns a full state on the very first publish, when there is nothing to diff against. So a
subscriber that opened while the stream was stale, and had a publish land before its seed arrived,
would take a delta as its first item. The SSE contract says the first item is a full snapshot.

Two remedies were on the table: hand the hub both a full snapshot and a delta on every publish, or
stop `OnPublish` adopting a delta and let the seeder retry against `StateStream.Latest`.

## What the code actually says

**`SeedIfNotSuperseded` has no callers.** Neither does the stale flag reach any production path.
The whole seed handshake — `MarkStale`, `IsStale`, `SeedIfNotSuperseded`, `_hasSnapshot` — was built
in Task 0.10 and wired to nothing, because no slice has adopted the hub yet. It exists only in
`LiveEventHubTests`.

That changes the question. It is not "how do we repair the race", it is "how should adoption be
written so the race has nowhere to live". And because nothing depends on the handshake yet, the
answer is not constrained by it.

## The decision

**The hub carries deltas only. It never carries a snapshot, and the seed handshake goes.**

A subscriber gets its full state from `StateStream` at open time, not from the hub:

* `StateStream` gains one method returning `(T? latest, long version)` **read together under its own
  lock**, which it already holds for `Publish`. That pair is consistent by construction: no publish
  can interleave between reading the state and reading the version it belongs to.
* The subscriber serializes that state as its first item and remembers the version.
* Everything the hub delivers afterwards is a delta with a version, and anything at or below the
  remembered version is dropped.

Why this beats both earlier options:

* **Handing the hub both payloads** would serialize the full state on every publish whether or not
  anyone needed it — the unconditional-snapshot-build cost the performance investigation identified
  in the first place. Full serialization now happens once per subscriber, at the moment one arrives.
* **Retrying the seed** keeps a handshake whose only purpose is to lose a race that this design does
  not have. A retry loop is a smaller bug surface than a race, but no loop is smaller still.
* The hub goes back to being what the design asked for and the user insisted on: a loudspeaker. It
  moves bytes and a version. It has no opinion about what a snapshot is.

## What this deletes

`MarkStale`, `IsStale`, `SeedIfNotSuperseded` and `_hasSnapshot`, along with the two
`LiveEventHubTests` cases that pin the seed race. Deleting a test is normally a warning sign, so it
is worth saying plainly why these go: they pin the behaviour of a mechanism that is being removed
because it was never needed, and keeping them would mean keeping the mechanism to satisfy them.

The ordering guarantee they were protecting does not go anywhere — it moves to where it can be
stated as an invariant rather than defended by a handshake: **a subscriber's first item is the state
at version N, and every later item has a version strictly greater than N.** That is testable
directly, and it should be tested directly.

## What stays

The zero-subscriber guard stays and is the point of the whole exercise. A publish still checks
`HasSubscribers` before building anything:

```csharp
if (!hub.HasSubscribers(key)) return;
```

`HasSubscribers` is already used by test doubles, so the seam exists. What the guard now skips is
cheaper to describe: with no snapshot to build for the hub, the only work avoided is the delta,
which is the work that was always the point.
