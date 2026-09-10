# Architecture progress — the numbers that say whether this is working

The migration in `plans/basil-plan-20260909.md` is judged by two measurements, not by how many
tasks are ticked. Both come from the assessment and are re-derived the same way each time.

## How to re-measure

Attribute every `using Basil.Server.Features.X` to the slice the importing file lives in, keep only
the edges where a type declared in the target namespace actually appears in the importing file's
**code** (not its comments), then run Tarjan over the result. The scripts used to produce the
baseline are in the assessment's method section; the two traps that produced wrong numbers the
first time are worth repeating:

* A bare `\.Match\b` search counts `Regex.Match`. Anchor it to a session variable.
* A naive type-name extractor picks English words out of XML comments (`record the ...` yields a
  "type" named `the`). Require an access modifier, an uppercase initial, and skip comment lines.

## Baseline and target

| Measurement | 2026-09-08 baseline | After stage C | Target |
|---|---:|---:|---:|
| Live cross-slice edges | 44 (unchanged at stage C entry) | ~17 predicted | as few as the domain really needs |
| Strongly connected components | 1 of 10 | 5 free + a knot of 5 | no cycle that is not declared |
| Mutual (two-cycle) slice pairs | 15, or 13 by the script's definition | — | 3, each declared with a reason |
| Declared allowlist edges | 38, of which 9 were invisible | 45, all visible | shrinking |
| `Shared -> Features` pinned offenders | 12, of which 1 was invisible | 13 | 0 |
| Dead / documentation-only imports | 5 / 7 | 0 / 0 | 0 |

## Log

| Date | Commit | Change | Edges | Components |
|---|---|---|---:|---|
| 2026-09-08 | `e7a1bf7` | assessment measured the baseline | 44 | 1 of 10 |
| 2026-09-09 | `ed48716` | stage A made constant-mediated coupling visible; 7 edges declared, 1 `Shared` offender pinned | 44 | 1 of 10 |
| 2026-09-10 | `4d669be8` | re-measured with `measure-slice-graph.py` | 44 features-only, 52 solution-wide | 1 of 10 |
| 2026-09-10 | `ef96b008` | **stage C entry baseline**, after stage B closed and stage F merged | 45 features-only, 53 solution-wide | 1 of 10 |

Stage A changed no coupling. It changed what the rule can see, which is why the edge count is
unchanged while the declared count rose from 38 to 45.

### The stage C entry baseline

The C5 gate compares against a number, so the number has to be current. Everything the migration
has landed since the assessment — three types out of `MatchMembershipService`, the slot and
countdown handlers out of `MatchControlService`, the `const` to `static readonly` change — was
re-measured on `4d669be8`, from source, by `plans/execution/measure-slice-graph.py`:

```text
slices: 11 (Auth, Beatmaps, Bot, Chat, Content, Diagnostics, Irc, Multiplayer, Scores, Spectating, Users)

== features-only, comparable to the assessment (src\Basil.Server\Features)
live edges: 45
mutual pairs: 13
documentation-only edges: 1
cycle of 10: Auth, Beatmaps, Bot, Chat, Content, Irc, Multiplayer, Scores, Spectating, Users

== solution-wide, the number C5 gates on (src)
live edges: 53
mutual pairs: 17
cycle of 10: Auth, Beatmaps, Bot, Chat, Content, Irc, Multiplayer, Scores, Spectating, Users
```

Measured at `ef96b008`, the commit that closes Stage B and carries the merged Stage F. The numbers
moved by exactly one edge from the 2026-09-09 reading, and the one edge is `Diagnostics -> Auth`.

**There are now eleven slices and the cycle still has ten.** Diagnostics is the first slice to sit
outside the knot: one outgoing edge, declared, and nothing depends on it. That is the
published-gauges decision in `plans/execution/diagnostics-boundary-decision.md` showing up in the
measurement — four edges into Multiplayer, Chat, Irc and Users were refused in favour of each slice
publishing its own counts as gauges, and the alternative would have put Diagnostics straight into the
component with everything else.

It is also the only evidence so far that a boundary decision on this migration changes the graph in
the predicted direction, which is worth holding onto going into C5.

Four things this says, none of which were safe to assume:

* **Still one component of ten, and the edge count moved only by the one authorised edge.** Nothing the
  migration has landed so far has moved a cross-slice edge, and the measurement is now a script
  rather than a transcript, so C5 re-runs the same definition instead of a similar one.
* **The honest number is 53, not 45.** The assessment matched references written
  `Basil.Server.Features.<Slice>`, so it never counted the eight edges that run through
  `Basil.Domain.<Slice>` namespaces. Those are real dependencies between slices; they were simply
  outside the frame.
* **Stage B moved nothing, and was never meant to.** Splitting a 1,483-line service into handlers
  inside the same slice cannot change a cross-slice edge. Recording that explicitly is what keeps a
  green Stage B from reading, later, as evidence the coupling problem is shrinking on its own.
* **The mutual-pair count is 13, and it is not comparable to the assessment's 15.** The two numbers
  come from different definitions, not from movement in the code: the script's first run, before it
  stripped comments, also reported 13. Nothing in Stage B changed a mutual pair. Thirteen is the
  entry baseline because it is what the script measures, not because two pairs were removed.

### Why C5 must gate on the solution-wide number

The assessment's predicted payoff — 44 edges down to roughly 17, four slices leaving the knot --
came from a *simulated platform extraction*: it took shared types out of `Features/` and re-counted.
A measurement scoped to `Features/` therefore falls when files leave that directory, whether or not
any dependency was actually broken. Stage C moves roughly ninety-six files out of `Features/`. Gate
C5 on that number alone and it passes for free, on the very step it exists to check.

So C5 reads both:

* **features-only** must fall to roughly 17. That is the prediction, in the frame it was made in.
* **solution-wide** must fall with it. It starts at 52.

If features-only drops to 17 while solution-wide stays near 52, the coupling did not go anywhere --
it moved into `Basil.Domain` and out of the old measurement's view. That is the precise failure this
gate exists to catch, and it is invisible to either number on its own.

`Basil.Protocol` is excluded from the solution-wide pattern. `Basil.Protocol.Irc` and
`Basil.Protocol.Multiplayer` are wire-format libraries that share a name with a slice, reference no
feature, and are something every transport is entitled to depend on. Counting them reported eight
edges that do not exist.

The same caution applies to the documentation-only count, which is 1 here where the assessment
reported 7: the assessment counted a `using` whose only consumer was a doc comment, and the script
counts a slice named nowhere but in a comment. Only the features-only live-edge number is derived the
same way in both, which is why it is the only figure in this file that may be compared with the
assessment's directly. Every other row that pairs an assessment figure with a script figure is a
comparison between two definitions until proven otherwise.

## The two instruments have opposite blind spots

Found 2026-09-10 during Task C4, by a worker acting on the stop-and-report instruction rather than
declaring a convenient answer. It changes what C5 is allowed to gate on.

`stage-c-order-decision.md` predicted that moving `MpCommandService` out of `Bot` would remove
`Bot -> Irc`, because `measure-slice-graph.py` attributed that edge to `MpCommandService` alone. The
edge did read as gone afterwards. Deleting `("Bot", "Irc")` from the allowlist then **failed**
`SliceBoundaryTests`, naming `CommandDispatcher.ScopedDmReplySink`.

The dependency is real and it stays behind: `ScopedDmReplySink.Reply()` calls
`sender.IrcConnection.Send(...)`, and `IrcConnection` is typed `IIrcConnection`, a
`Features.Irc` type. But that name is **never spelled** in `CommandDispatcher.cs` — no `using`, no
fully qualified reference, just an inferred property type. A source-text scan cannot see it. The
compiler emits a reference, so NetArchTest can.

So the two instruments fail in opposite directions:

| | Sees | Blind to |
|---|---|---|
| `SliceAdjacency` + NetArchTest, reading IL | inferred types, every reference the compiler emits | `const` values, which C# inlines at the use site |
| `measure-slice-graph.py`, reading source text | anything whose namespace is spelled, `const` included | inferred types, where the namespace is never written |

**ADR-008 already closed the IL side's blind spot**, by requiring values that cross a boundary to be
`static readonly` rather than `const`. Nothing has closed the text side's, and nothing cheaply can:
seeing an inferred type's namespace means resolving symbols, which is the full Roslyn analysis
ADR-008 deferred and which the available Roslyn tooling does not deliver here — its
`get_dependency_graph` errors on this solution and its `detect_circular_dependencies` reports zero
cycles where there are ten slices in one.

That makes the allowlist the stronger instrument today, and the script the weaker one. It is a search
tool, not a gate.

### What C5 gates on, restated

**An edge counts as removed only when its `SliceAdjacency` row can be deleted and the architecture
suite stays green.**

The script's falling numbers say where to try. The allowlist says whether it worked. C5's prediction
is therefore stated in allowlist rows, which are enforced on every build, rather than in script edges,
which can fall for a reason that is not true.

Both numbers still get recorded, because the pair is more informative than either: a script edge that
disappears while its allowlist row cannot be deleted is precisely the `Bot -> Irc` case, and knowing
that shape exists is what stops the next worker from reading a falling count as progress.

## The rule for stage C

`plans/basil-plan-20260909.md` task C5 says to stop and report if the graph does not move as
predicted. That is not a formality: stage D splits the code into projects along these boundaries,
and a project split made against a graph that is still one component produces projects that cannot
compile without each other.
