# Should the Diagnostics slice reach into the features it reports on?

**Decided 2026-09-09. Blocker for Task 5.6.**

A worker building the Diagnostic API stopped rather than edit `SliceAdjacency`, and proved the
constraint empirically instead of arguing it: a throwaway probe file referencing each candidate
type, run against `Slices_Should_Only_Reference_Declared_Slices`, deleted before committing. The
result:

| Type it wanted | Lives in | Rule says |
| --- | --- | --- |
| `AdminKeyDefaults.Policy` | `Features.Auth` | needs `Diagnostics -> Auth` |
| `IMatchRegistry` | `Features.Multiplayer` | needs `Diagnostics -> Multiplayer` |
| `IChannelRegistry` | `Features.Chat` | needs `Diagnostics -> Chat` |
| `ISessionRegistry<IrcSession>` | `Features.Irc` | needs `Diagnostics -> Irc` |
| `ISessionRegistry<GameSession>` | `Shared.Sessions` | passes, no edge |

## The decision

**`Diagnostics -> Auth`: added.** Every admin-gated slice already has this edge — `Beatmaps -> Auth`,
`Content -> Auth`, `Multiplayer -> Auth`, `Users -> Auth` — and all of them exist for the same
reason: `AdminKeyDefaults` is a constant. Stage C moves those constants to `Basil.Domain`, which
deletes all five edges at once. Blocking the Diagnostic API on an edge that is already the norm and
already scheduled for removal would be pedantry, and the alternative the worker considered and
rejected — hardcoding the literal `"Admin"` to dodge the compile-time reference — is strictly worse:
the same coupling with no compiler check.

**`Diagnostics -> Multiplayer`, `-> Chat`, `-> Irc`: refused.**

The worker had already demonstrated the alternative without noticing it was one. It read
`basil.sse.subscribers` and `basil.match.publish.stale_dropped` through the meter listener, from a
slice it has **no edge to at all**, because those are published as metrics by whoever owns them.

Live counts work the same way. The slice that owns a registry publishes an observable gauge over
it; Diagnostics reads the meter and never learns who published. No edge, and no need for one.

Three reasons this is better than adding the edges rather than merely equivalent:

* **Observability that reaches into everything is how an observer becomes a coupling centre.**
  Diagnostics would have gone from zero outbound edges to four in one step, in a graph the migration
  exists to make acyclic. The next thing worth reporting on would add a fifth.
* **The owning slice is the only one that knows what "active" means for its own registry.** A match
  in teardown, a channel with no members, an IRC session mid-handshake — Diagnostics counting rows
  in someone else's dictionary would encode an answer it has no standing to give.
* It matches the invariant the target architecture already states: a feature reaches another only
  through a contract the callee owns. A published metric is exactly that contract.

## What this requires

No `CreateObservableGauge` exists anywhere in the server today, so this is a new pattern rather than
an existing one being reused. Each owning slice adds one:

| Slice | Gauge | Over |
| --- | --- | --- |
| Multiplayer | `basil.matches.active` | `IMatchRegistry` |
| Multiplayer | `basil.match.timers.active` | matches with a pending countdown |
| Chat | `basil.channels.active` | `IChannelRegistry` |
| Irc | `basil.irc.sessions.active` | `ISessionRegistry<IrcSession>` |

`Chat` and `Irc` have no metrics file yet and get one, following `MultiplayerMetrics`.

## A correction to the inventory, found by the same worker

`plans/diagnostic-metric-inventory-20260907.md` has **no `application` section**. It covers
`process`, `memory`, `gc`, `threadpool`, `runtime`, `exceptions` and `http`, every one of them
probe-verified against this runtime.

Task 5.5's field list — active users, active sessions, active matches, active SSE subscribers per
stream, active timers, eventing counters — is a plan author's sketch that never went through that
probe gate. The inventory is authoritative input; that list is not, and the gauges above are the
decision about what `application` actually reports.
