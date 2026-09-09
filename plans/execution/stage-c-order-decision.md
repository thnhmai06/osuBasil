# Stage C runs in reverse: C4, C3, C2, then C1

> Status: **Decided 2026-09-10.** Supersedes the C1..C5 ordering in
> `plans/basil-plan-20260909.md`, which is updated to match.

## The problem with the written order

Stage C as planned starts with C1, which moves roughly ninety-six files out of
`Basil.Server/Features` into `Basil.Domain`, and only then splits `GameSession` (C2), turns logout
into an event (C3), and moves `MpCommandService` into Multiplayer (C4).

Two things are wrong with that.

**It moves the same code twice.** `MpCommandService` is 1,889 lines that C4 moves from `Bot` to
`Multiplayer`. Under the written order C1 moves it into `Basil.Domain/Bot` first, and C4 then moves
it again. The same is true of everything C2 and C3 restructure.

**It is the wrong failure mode for this project.** Four workers have now been killed mid-task by a
session limit, and one of them left the tree unable to compile. A half-finished C3 or C4 is a
compiling tree with a measurable edge count — the work simply stops where it stopped. A
half-finished C1 is ninety-six files stranded across a project boundary, with no green suite and no
obvious point to resume from. Cost of redoing work is the smaller argument; survivability is the
real one.

## The order

**C4 → C3 → C2 → C1 → C5.**

Every restructuring step happens inside `Basil.Server`, where the compiler checks each move, Rider's
refactorings work on the reference index, and the suite is runnable at every intermediate point. The
project boundary is crossed once, at the end, on code that has stopped moving.

## What changes in C2

C2's destination table names `Basil.Domain` for six of its eight member groups. Running C2 before C1
would mean doing part of C1 under C2's name, which is exactly the ambiguity that lets a worker
improvise.

C2 is therefore restated as a split **in place**: each feature gets its own map from player id to
its own state, and those maps live in `Features/<Slice>` like everything else. `Basil.Domain` is
C1's business and C1's alone.

This costs nothing, because C2's actual verification does not mention `Basil.Domain` at all: the
`Shared_Should_Not_Reference_Features` pinned list loses its `Shared.Sessions.*` entries. That test
asserts exact set equality, so the deletion has to be deliberate, and the deliberate deletion is the
proof. It is achievable entirely inside `Basil.Server`.

## What C4 is measured to do

Checked against the live graph before committing to the order, because "move a file and the coupling
improves" is the assumption this whole stage is supposed to stop trusting.

`Bot` has seven outgoing edges. `MpCommandService.cs` is the only file carrying three of them:

| Edge | Files carrying it | After C4 |
|---|---|---|
| `Bot -> Beatmaps` | `MpCommandService` | gone |
| `Bot -> Irc` | `MpCommandService` | gone |
| `Bot -> Scores` | `MpCommandService` | gone |
| `Bot -> Chat` | `BotBootstrapService`, `CommandDispatcher`, `ICommandDispatcher`, `ICommandReplySink`, `MpCommandService` | survives |
| `Bot -> Multiplayer` | `CommandDispatcher`, `ICommandDispatcher`, `MpCommandService` | survives, and is what C4 reduces to one contract |
| `Bot -> Users` | `BotBootstrapService`, `CommandDispatcher`, `MpCommandService` | survives |
| `Bot -> Content` | `CommandDispatcher` | survives, and stays in `Bot` |

**C4 creates no new outgoing edge for Multiplayer.** Every slice `MpCommandService` reaches —
Beatmaps, Chat, Irc, Scores, Users — is one Multiplayer already depends on. The one edge that could
have been new, `Multiplayer -> Content`, is not: `Bot -> Content` lives in `CommandDispatcher`,
which stays behind.

Net effect: three edges removed, none added.

## Related code

* `plans/execution/measure-slice-graph.py` — the measurement above is reproducible from it
* `plans/execution/architecture-progress.md` — the Stage C entry baseline and the C5 gate
