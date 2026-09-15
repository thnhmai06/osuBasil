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
| 2026-09-10 | `a936c343` | C4 moved `MpCommandService`/`MpReplies` into Multiplayer; `Bot -> Irc` row kept (see below) | 43 features-only, 50 solution-wide, as recorded by the C3 worker's re-measurement below | 1 of 10 |
| 2026-09-10 | `a6e12458` | C3 inverted logout; script numbers unchanged, `Shared -> Features` pinned list 13 to 12 | unchanged | 1 of 10 |
| 2026-09-14 | `e286cc26` | re-measured at the start of the reconciliation session: `python plans/execution/measure-slice-graph.py`; `SliceAdjacency` 44 rows, pinned list 12, `DomainAdjacency` 6 | 43 features-only, 50 solution-wide | 1 of 10 |
| 2026-09-15 | `32aaed40` | **C5, after C1a closed** (pinned list 21 → 3 across five steps, none of them a slice-boundary edge): `SliceAdjacency` 44 rows, `Shared -> Features` pinned list 12, `DomainAdjacency` 6 — all three unchanged since `e286cc26`; script re-run, unchanged too. See "C5" below. | 43 features-only, 50 solution-wide | 1 of 10 |
| 2026-09-15 | `573f3c0c` | **C5, after C1b's per-feature table resolved** (Units 1-9: `Shared -> Features` pinned list 12 → 10, two rows dropped in Units 2 and 4; `DomainAdjacency` 6 → 14, eight edges added as real business logic moved in; `SliceAdjacency` unchanged at 44, C1b never touches slice-to-slice edges directly): `python plans/execution/measure-slice-graph.py` re-run. See "C5, run after C1b" below. | 42 features-only, 50 solution-wide | 1 of 10 |

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
* **solution-wide** must fall with it. It was 53 at the stage C entry baseline and is **50** at
  `e286cc26`, after C4 and C3.

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

### Three instruments, three populations

Task C3 added the third case. It inverted `PlayerLogoutService`'s dependency on five slices and
**every number stayed the same**: `SliceAdjacency` 44, features-only 43, solution-wide 50. The win was
real and it showed up in a fourth place — the `Shared_Should_Not_Reference_Features` pinned list, 13
entries down to 12.

The script saw nothing because `Shared/Sessions/PlayerLogoutService.cs` was never in its population:
it counts edges **sourced from** a slice-named directory, and `Shared/` is not one. So:

| Instrument | Population | Enforced by |
|---|---|---|
| `SliceAdjacency` | `Features/<Slice>` → `Features/<Slice>` | NetArchTest, every build |
| `Shared_Should_Not_Reference_Features` pinned list | `Shared/` → `Features/` | NetArchTest, exact set equality |
| `measure-slice-graph.py` | files owned by a slice-named directory, in `Features/` or `Domain/` | nothing; it is a report |

Nothing measures `Host/` → anything. Inside `Basil.Domain`, C6 added `DomainAdjacency`.

A fifth instrument landed on 2026-09-14, and it is the one C1 is measured by:

| Instrument | Population | Enforced by |
|---|---|---|
| `TransportSeamTests` pinned list | `Features/` types outside `.Packets` and outside `Irc` → `Basil.Protocol` | NetArchTest, exact set equality |

It starts at **21 entries**. None of the four instruments above has this dependency in its
population, so cutting it moves none of their numbers — the C3 shape again. The finding that
produced it is in `c1-transport-seam-decision.md`.

**The practical rule: name the currency before claiming a win.** C3's currency is the pinned list. C4's
was `SliceAdjacency` rows. C2's will be the pinned list again, because what it removes is
`Shared/Sessions/GameSession.cs` naming Multiplayer and Spectating types. C5 must state which
instrument each claim rests on, or a task that moved nothing will read as progress on whichever number
happened to drift.

### A fourth blind spot, found by C6

`measure-slice-graph.py` derives its slice names from the directories under
`src/Basil.Server/Features`. `Basil.Domain` has namespaces that are not slices — `Channels`, `Login`
and `Social` — so the script never looked at them.

Task C6 measured the Domain graph from the assembly instead, and found **six edges where this
project's own record said three**. All three extras run through exactly those namespaces:

| Edge | Carried by | Why it is a real relationship |
|---|---|---|
| `Scores -> Login` | `Submission.cs` | `ValidateClientDetails` checks the submission against the osu! version captured at login |
| `Channels -> Users` | `Channel.cs` | a channel gates read and write on `UserPrivileges` |
| `Users -> Login` | `User.cs` | a user carries the `Country` resolved at login |

None is implausible, so all six were declared rather than treated as a design flaw. The point is not
the three edges — it is that the script's population is derived from a directory listing that has no
authority over `Basil.Domain`, and C1 is about to move ninety-six files in there.

C6's rule discovers its population from the assembly at run time rather than from a hardcoded list,
which is why it found this and why it will cover C1's arrivals without being edited.

### The pinned list has been quoted one short since before C3

`SliceBoundaryTests`' comment said "12 types" while the array held 13, so C3's removal of
`PlayerLogoutService` was recorded here as 12 down to 11 when the true movement was **13 down to 12**.
The removal is real; only the absolute number was wrong, taken from the comment rather than counted
from the array.

The count is now out of the comment entirely. A number restated in prose beside the list it counts
has nothing checking it, and this one drifted for at least two tasks before anyone counted.

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

## C5, run after C1a — reported rather than concluded

C1a closed the `TransportSeamTests` pinned list from 21 to 3, and every one of the four instruments
that watch slice boundaries — `SliceAdjacency` (44 rows), the `Shared -> Features` pinned list (12),
`DomainAdjacency` (6), and `measure-slice-graph.py` (43 features-only / 50 solution-wide) — read
**identical** to `e286cc26`, before C1a started. That is not a failure to move; it is what
`c1-transport-seam-decision.md` predicted before C1a began: none of those four instruments has
`Basil.Protocol` in its population, so a task that removes business code's dependency on the
protocol is invisible to all of them by construction, the same shape C3 already demonstrated for
the `Shared -> Features` list. C1a's currency was always the pinned list, and that moved by 18 rows.

**What this means for the "stop and report" instruction.** The plan's C5 prediction — features-only
falling to roughly 17, with Auth, Beatmaps, Content, Users and Spectating standing free — was made
for the *original*, unsplit C1: the ~96-file move of business code into `Basil.Domain`. That move is
**C1b**, and C1b has not run; `c1-transport-seam-decision.md` split it off explicitly because the
services could not have entered `Basil.Domain` with their protocol references still attached. So the
graph "not moving as predicted" here is not a broken measurement or a missed step — it is C5 being
run against a prediction whose precondition (C1b) is still an open decision, not a completed task.

**Reported, not decided:** whether C1b still happens. `architecture-target-20260908.md`'s target
layout and invariant 9 (`Basil.Domain` may not reference `Basil.Protocol`) are unchanged and satisfied
by C1a's tree as it stands today — every business service that used to reach for
`ServerPacketWriter`/`IrcMessageWriter` no longer does. Two paths from here, both legitimate,
neither an agent's call to make alone:

* **Run C1b** — move the ~96 files into `Basil.Domain`, now that C1a has cleared the blocker that
  stopped it. This is what would actually produce the predicted features-only ≈17 / solution-wide
  drop, and it is the only way to get `DomainAdjacency` (not `SliceAdjacency`) watching the moved
  code, as C6 was built to do.
* **Skip C1b** — decide the namespace-level separation C1a already achieved (`Features/<Slice>` code
  no longer touches the protocol; `Basil.Domain` already holds the value-object layer) is enough for
  Stage D's purposes, and that the project-boundary move is not worth its cost. Stage D would then
  split `Basil.Server` into host projects with the business services still living in
  `Basil.Server.Features`, not `Basil.Domain` — survivable, but a different target shape than
  `architecture-target-20260908.md` describes today, and that document would need a matching update.

Either way, **Stage D should not start silently assuming one answer.** This is the decision point the
original C5 instruction was trying to catch, just for a different reason than the text anticipated.

## C5, run after C1b — the ≈17 prediction did not land, and why that is expected

C1b ran (the user's decision, `c1b-project-move-decision.md` §"Status"), through nine small verified
units rather than the ~96-file move at once. Re-measured at `573f3c0c`, C1b's last commit:

| Instrument | Post-C1a (`32aaed40`) | Post-C1b (`573f3c0c`) | Moved because |
|---|---:|---:|---|
| `SliceAdjacency` declared rows | 44 | 44 | C1b moves files *into* `Basil.Domain`; it does not touch `Basil.Server.Features`-to-`Features` edges |
| `Shared -> Features` pinned list | 12 | 10 | Units 2 and 4: `FileSystemReplayStorage` and `OsuWebRoutes` stopped referencing `Features` once the interfaces they used moved to `Basil.Domain` |
| `DomainAdjacency` declared rows | 6 | 14 | Units 1, 3, 4, 7, 8 declared 8 new edges as real cross-namespace business logic arrived inside `Basil.Domain` |
| `measure-slice-graph.py`, features-only | 43 | 42 | one edge fewer; see below |
| `measure-slice-graph.py`, solution-wide | 50 | 50 | unchanged |

**The plan's features-only ≈17 prediction was written for a different move than the one that ran.**
It assumed C1's original, undivided scope: every business service *and* every transport adapter —
the 24 Multiplayer packet handlers, `MpCommandService`, `MpReplies`, the rest of Bot's dispatcher —
moving into `Basil.Domain` together. `c1b-project-move-decision.md`'s Unit 9 found, by direct
measurement rather than by re-deriving the plan's classifier, that none of those transport-shaped
files were ever real candidates: they mutate live session state, call `MatchSession.BeginMutationAsync`,
or read through a `Basil.Server.Shared` type, the same category of blocker C1a's own `TransportSeamTests`
was built to catch for the protocol specifically. What C1b actually moved — repository/store
contracts, response-encoder-adjacent business services, `ChannelSession`, `MatchRoomState` — mostly
lived *inside* a slice's own business logic, not across the `Features/<Slice>`-to-`Features/<Slice>`
boundary `SliceAdjacency`/`measure-slice-graph.py` watch. That is why `SliceAdjacency` and the
solution-wide script count sit still: the edges C1b removed were never between two slices in the
first place, they were between a slice and the protocol/session layer underneath it, which these two
instruments were never built to see (the same blind spot the post-C1a section above already names).

**`DomainAdjacency` is the instrument built to watch exactly this, and it moved as expected.** Its
row count roughly doubling (6 → 14) is the direct, visible record of business logic actually landing
inside `Basil.Domain` and forming real cross-namespace relationships there — this is what C6 was
built for, and it is doing its job.

**Not a miss.** The ≈17 number would only be reachable by also moving the 24 packet handlers and the
`!mp` command surface — Stage D's `Basil.Hosts.Bancho` work, gated on host projects that do not exist
yet. Attempting it now would, per `architecture-target-20260908.md` §8's own migration-order note,
move business logic into a host-project shape before that shape exists, then require moving it again
once Stage D actually creates `Basil.Hosts.Bancho`. C1b is complete at the scope it was actually run
at; the ≈17 prediction is Stage D's number to reach, not C1b's.
