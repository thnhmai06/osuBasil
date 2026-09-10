# C2 is deferred into C1 and Stage D, not performed on its own

**Decided 2026-09-10**, after a worker investigated C2, measured it, proved the blocker empirically,
and stopped rather than doing the work. The finding is in `plans/execution/phase-stage-c.md` under
"C2 — investigated, not started"; this document is the decision taken on it.

## What C2 said it would prove

> Verify: the `Shared_Should_Not_Reference_Features` pinned list loses its `Shared.Sessions.*`
> entries. The test asserts exact set equality, so the list must be edited deliberately — that edit
> is the proof.

Three entries are pinned: `GameSession`, `UserSession`, `GhostDisconnectService`. C2 cannot remove
any of them.

## Why not — verified, not argued

`Shared/Sessions/GameSession.cs` names exactly two feature namespaces: `Features.Irc` and
`Features.Multiplayer`. It does **not** name `Features.Spectating` at all — `Spectating` and
`Spectators` are typed `GameSession` and a `GameSession` collection, so they were never a boundary
crossing. `UserSession`'s only feature reference is the `IIrcConnection` property it declares.
`MpScopeMatchId` is an `int?` and never was a violation.

So each entry needs something C2 does not do:

| Entry | Held by | Removed by |
|---|---|---|
| `UserSession` | `IIrcConnection` | **Stage D** — the IRC transport becomes its own project |
| `GameSession` | `IIrcConnection` **and** `new BanchoIrcBridgeConnection(this)` **and** `MatchSession` | **Stage D and C1 together** — neither alone is enough |
| `GhostDisconnectService` | its own imports, not session state | not C2's |

The pinned list is asserted per type, for exact set equality. A type with two offending references
loses its entry when the second one goes, not the first — so even finishing all four field moves
leaves `GameSession` pinned by `Features.Irc`, which C2's own destination table defers to Stage D.

A worker confirmed the shape rather than inferring it: deleting only the `UserSession` line from
`knownOffenders` and running `Basil.ArchitectureTests` failed, naming `UserSession` as an offender
from `Irc` alone. The line was reverted and the suite is back to 6 of 6.

## What the other instruments would do

Nothing. Carrying out all four field moves leaves `SliceAdjacency` at 44 and
`measure-slice-graph.py` at 43 features-only / 50 solution-wide. A reader of `session.Match` already
depended on Multiplayer through the field's type; a lookup through the owning slice is the same edge
with more code.

## The decision

**`.Match` is absorbed into C1.** `MatchSession` is still one 584-line class holding both the match's
business state and its SSE projection machinery — nine `StateStream<T>` fields, `SseSubscriberRegistry`,
`SequenceGate`, sixteen references in all. C1 splits exactly that. Once the business half lives in
`Basil.Domain`, `GameSession.Match` points at a Domain type, and `Shared -> Features.Multiplayer`
disappears as a side effect of C1 rather than as forty-five read sites rewritten under C2's name.

**`IrcConnection` stays with Stage D**, which is where C2's own table already put it.

**`.InLobby` and `.MpScopeMatchId` are not moved.** They move no instrument, and moving them would
*create* two cleanup obligations that do not exist today: both currently die with the session object,
and a player-id-keyed map has to be cleared explicitly at logout. Trading zero measured benefit for
two new invariants to maintain is the opposite of `CLAUDE.md` rule 2.

## The part of the original finding that still stands, and the part that does not

The 2026-09-08 assessment called `GameSession` "a shared mutable element with no owner", and C2 was
the answer to it. Half of that has since been measured false: **every field already has exactly one
writing slice** — `.Match` Multiplayer, `.Spectating` Spectating, `.InLobby` Chat, `.MpScopeMatchId`
Multiplayer. Ownership of writes is not the problem, and `MatchMutationScope` settled the
synchronisation question in Stage B.

What remains true is narrower: **reads are unconstrained.** Any slice can read `session.Match`
whether or not it owns anything about matches. No instrument measures that, and no rule currently
expresses it. It is worth considering in Stage E2, where the new architecture rules are written, as a
rule about which slices may *read* another slice's state — but it is not worth forty-five rewrites
and two new invariants ahead of C1, when C1 changes what `.Match` is anyway.

## Consequence for the stage order

**C4 → C3 → C6 → C1 → C5.** C2 comes off the path. After C1, re-read this document and decide whether
anything is left of it; the honest expectation is that C1 and Stage D between them consume all of it.

## Related code

* `src/Basil.Server/Shared/Sessions/GameSession.cs`, `UserSession.cs`
* `src/Basil.Server/Features/Multiplayer/MatchSession.cs` — the 584 lines C1 splits
* `tests/Basil.ArchitectureTests/SliceBoundaryTests.cs` — the pinned list
