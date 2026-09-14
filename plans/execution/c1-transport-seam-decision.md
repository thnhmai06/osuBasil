# C1 is blocked on a seam the plan never measured: business code encodes the wire formats

> Status: **Investigated 2026-09-14 at `e286cc26`, decided, instrument landed.** C1 as written
> cannot run. What replaces it is at the end of this document, and
> `plans/basil-plan-20260909.md` Stage C is updated to match.

## The finding

Invariant 9 of `plans/architecture-target-20260908.md` says `Basil.Domain` may not reference
`Basil.Protocol`. Task C1 moves "about ninety-six files" of services, live state and contracts
into `Basil.Domain`, and sizes the move with a table that checks each candidate for `ILogger<T>`,
`IOptions<T>`, `IMemoryCache`, `HttpClient` and `IHostedService`, concluding that Domain's package
list "ends up as the two abstraction packages and nothing else".

**That table never checked `Basil.Protocol`.** Measured from the compiled assembly with NetArchTest
(`tests/Basil.ArchitectureTests/TransportSeamTests.cs`, the rule this investigation left behind),
73 types under `Features/` depend on `Basil.Protocol`. 46 are packet handlers and 6 are the `Irc`
slice — adapters, where the dependency belongs. The other **21** are the business services C1
exists to move, plus two API route types:

| Type | What it takes from `Basil.Protocol` |
|---|---|
| `Auth.LoginService` | `ServerPacketWriter` ×24 sites, `PacketBuilders`, `LoginFailureReason`, `BanchoPrivileges` |
| `Auth.ClientIntegrityService` | `IrcMessageWriter` |
| `Chat.ChannelMembershipService` (+ nested sinks) | `ServerPacketWriter` ×3, `IrcMessageWriter` ×13, `IrcMessage`, `IrcNumeric` |
| `Chat.ChatDispatchService` (+ nested sinks) | `ServerPacketWriter` ×3, `IrcMessageWriter` ×11 |
| `Multiplayer.MatchControlService` | `ServerPacketWriter` ×5 |
| `Multiplayer.MatchMembership` | `ServerPacketWriter` ×6 |
| `Multiplayer.MatchLifecycle` | `ServerPacketWriter` ×3, `MatchState` |
| `Multiplayer.MatchBroadcast` | `ServerPacketWriter` ×2, `IrcMessageWriter` ×3 |
| `Multiplayer.MpCommandService` (+ nested sink) | `IrcMessageWriter` ×3, `MatchState` |
| `Multiplayer.Handlers.Lifecycle.AbortHandler` | `ServerPacketWriter` |
| `Multiplayer.IMatchRegistry`, `InMemoryMatchRegistry`, `MatchLiveSnapshotBuilder` | `MatchState` (a wire record used as the registry's read model) |
| `Multiplayer.MatchPacketDataMapper` | `MatchPacket`, `ServerPacketWriter.WriteMatch` — this one is an adapter by design |
| `Spectating.SpectatorService`, `SpectateFramesEvent` | `ServerPacketWriter` ×5, `SpectateFrameBundle` |
| `Content.AnnounceRoutes`, `Multiplayer.Endpoints.MatchListEndpoints` | the API host describing bancho structures — already named as defects by Task D3 |

The enums the services also use — `Mods`, `SlotStatus`, `GameMode`, `MatchTeamType`,
`MatchWinCondition` — are **already `Basil.Domain` types**, so they cost nothing. The cost is
concentrated in one shape: a service decides an outcome and, in the same method, encodes the packet
or IRC line that announces it, then writes the bytes into `GameSession.Enqueue` or
`IIrcConnection.Send`. Across the nine services that is **27 distinct `ServerPacketWriter`
operations** at about 52 sites and **8 distinct `IrcMessageWriter` operations** at about 32 sites,
with `session is IrcSession` branches choosing between the two transports inline.

The same services take `GameSession` — a `Shared/Sessions` type that carries `IIrcConnection`
(`Features.Irc`) and `MatchSession` — as their parameter type throughout. Domain cannot hold that
type either, so a Domain-side player abstraction is part of the same seam.

`plans/architecture-assessment-20260908.md` §2 measured "business code does not reach the web
framework anywhere" and "does not reach the database at all", and both are still true. It did not
measure business code reaching the **transport protocol**, and it does almost everywhere.
`architecture-target-20260908.md` §3.5 found the pattern in exactly one file, `AnnounceRoutes`, and
prescribed the right fix — Domain publishes, the bancho host encodes — for that one file. C1 is that
fix applied twenty-one times, and the plan sized it as a file move.

## Why this changes the order, not just the size

`stage-c-order-decision.md` put C1 last because a half-moved project is a tree that does not compile
and has no resume point. Moving a service that still calls `ServerPacketWriter` into `Basil.Domain`
is exactly that: the first feature moved fails `Domain_Should_Not_HaveDependencyOn_Frameworks`'s
sibling rule the moment it is added, and there is no intermediate green.

So the seam has to be cut **in place, inside `Basil.Server`, before anything crosses the project
boundary** — the same reasoning that already ordered C4 and C3 ahead of C1. Each step is one
service's encoder calls replaced by a notification contract that the service owns and the transport
implements; the tree compiles after every step; and the instrument below says whether the step did
anything.

## The instrument

`TransportSeamTests.Business_And_Api_Types_Should_Not_Reference_Protocol` pins the 21 types above by
exact set equality, in the same shape as `Shared_Should_Not_Reference_Features`. Its population is
every type under `Features/` outside a `.Packets` namespace and outside the `Irc` slice. It fails
when a new business type reaches for the protocol **and** when an entry is removed without deleting
its row — verified by deleting `SpectatorService`'s row and watching it fail before trusting the
green run.

**This is C1's currency.** A seam step counts when a row can be deleted and the suite stays green.
The `SliceAdjacency` allowlist, the `Shared -> Features` pinned list and `measure-slice-graph.py`
all have populations that do not include this dependency, so none of them will move when it is cut —
the same lesson as C3, where the win showed up only in the pinned list.

## What was considered

**Relax invariant 9 and let Domain reference `Basil.Protocol`.** Protocol has no framework
reference, so the project would still be framework-free. Rejected: the services would move with their
encoders intact, so "transport adapters translate; they do not decide" would be false in the very
project that exists to make it true, and `GameSession` still cannot follow them. It saves the seam
work by making the move meaningless.

**Skip the move; keep the services in `Basil.Server` and only cut the seam.** Not rejected — it is
what happens if the seam is cut and the move is then judged not worth its cost. That judgement belongs
after the seam, with a pinned list that has actually shrunk, not before it. The target's
three-host layout and invariant 9 are user decisions and stay as written; the only change here is
that the step the plan skipped is put back in front of them.

## Decision

C1 is split in two, and only the first half is scheduled:

* **C1a — cut the transport seam, in place.** One service per commit, ordered smallest first:
  `SpectatorService` (5 sites, one transport), `AbortHandler`, `MatchMembership`,
  `MatchControlService`, `MatchLifecycle`, `MatchBroadcast`, then the dual-transport ones
  (`ChatDispatchService`, `ChannelMembershipService`, `MpCommandService`, `ClientIntegrityService`),
  `LoginService` last because the login reply is intrinsically a packet sequence and may end up
  classified as an adapter. `MatchState`'s three users get a Domain read model or the record moves
  to Domain, whichever `MatchLiveSnapshotBuilder`'s JSON contract allows. Each commit deletes its row
  from the pinned list. The contract each service gets is decided at that service, from its call
  sites, not designed up front — the shape the plan already accepted for logout handlers.
* **C1b — the project move,** only after C1a has emptied the list, and only if the move still buys
  something the namespace rules do not. That is a decision for then, with numbers.

C5 gates on C1a's pinned list as well as on the allowlist. Stage D is unchanged in text and still
depends on C5.
