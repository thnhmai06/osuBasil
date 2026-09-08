# osuBasil — Target Architecture

**Date:** 2026-09-08
**Evidence:** `plans/architecture-assessment-20260908.md` — read that first; this document does
not repeat its measurements.
**Status:** proposal. Three structural decisions are settled by the user (§1.2); everything
else is derived and open to revision.

---

## 1. Inputs

### 1.1 The layering rule

Applied system-wide:

```
API / Presentation
        ↓
Business
        ↓
External
```

Business must not depend on the web framework, the HTTP framework, an ORM, a database
library or implementation, a transport framework, or an external service implementation.

This is a rule about dependency direction, not an instruction to build textbook Clean
Architecture. How far to layer, and where a boundary becomes a project rather than a
folder, is decided per subsystem from evidence.

### 1.2 Settled decisions

| Decision | Choice |
|---|---|
| Where business lives | **`Basil.Domain` expands into the business layer** |
| Host boundaries | **Three host projects** — `Basil.Hosts.Bancho` (osu! client only), `Basil.Hosts.Irc` (IRC only), `Basil.Hosts.Api` (the rest), grouped under a solution folder, with the entry point and its wiring in `Basil.Host` |
| Host thickness | **Thick** — endpoints and packet handlers move into the host projects |
| Data access | **Keep hand-written repositories over Dapper**; fix the four services that reach the database directly |

The thick-host choice was made with its measured cost stated: eight of ten features will
span four or five projects. §4 records why that is survivable here and what keeps it so.

---

## 2. What the measurements already say about layering

The layering rule is close to satisfied today, by convention rather than by enforcement.

| Role | Files | Touch HTTP | Touch DB |
|---|---:|---:|---:|
| Packet handlers | 47 | 0 | 0 |
| Services | 31 | 0 | 4 |
| Routes | 19 | 19 | 0 |
| Persistence | 32 | 0 | 14 |
| Contracts | 13 | 0 | 0 |

Business code does not reach the web framework anywhere, and reaches the database in four
files. Routes never touch the database. Zero feature files import Serilog — logging already
goes through `ILogger<T>`.

**The layering is not the problem.** The problems the assessment measured are horizontal:
one strongly connected component of ten features, an enforcement rule blind to constants,
and per-player state with no owner. The layering rule matters here because it decides
*where* things go when those problems are fixed — not because it is being violated.

Six files violate it and are named in §6.

---

## 3. Target structure

### 3.1 Projects and the dependency rule

```
Basil.Domain          business. No framework references at all.
Basil.Protocol        bancho wire format. No project references.

Basil.Infrastructure  external: persistence adapters, storage, media, external services.
                      -> Domain

Basil.Hosts.Bancho    osu! client transports.   -> Domain, Protocol, Infrastructure
Basil.Hosts.Irc       IRC transport.            -> Domain, Protocol, Infrastructure
Basil.Hosts.Api       the Basil API surface.    -> Domain, Infrastructure

Basil.Host            entry point and composition. -> everything
```

The rule the compiler enforces, with no exception list:

> **`Basil.Domain` references no host, no infrastructure, and no framework.**
> **No host references another host.**

`Basil.Infrastructure` is the successor to today's `Basil.Server`. The name change is not
cosmetic: it states what the project is allowed to contain. Anything in it that is not an
adapter to something external belongs in `Domain` or a host.

### 3.2 Feature is the folder axis inside every project

Every project is organized by feature, so navigation stays feature-first even though a
feature spans projects:

```
Basil.Domain/Multiplayer/          MatchSession, match services, contracts
Basil.Hosts.Bancho/Multiplayer/    the 24 multiplayer packet handlers
Basil.Hosts.Api/Multiplayer/       match routes and views
Basil.Infrastructure/Multiplayer/  SqliteMatchRepository, replay storage
```

A feature's name appears in the same place in every project. This is the main thing that
makes a four-project feature navigable rather than scattered.

### 3.3 Host assignment

| Host project | Hosts served | Why |
|---|---|---|
| `Basil.Hosts.Bancho` | `bancho.` (packet exchange), `osu.` (`/web/*.php`, `/d/{set}`), `b.` (beatmap assets), `a.` (avatars) | every one of these is spoken only by the osu! stable client |
| `Basil.Hosts.Irc` | the TCP IRC listener | a different transport with a different lifecycle: long-lived sockets, not request/response |
| `Basil.Hosts.Api` | `api.`, `assets.` | the Basil API and the assets its consumers use (`cover`, `card`, `list`, `slimcover` variants are UI sizes, not client assets) |

**`Irc` stops being a feature.** All eleven of its files — TCP listener, connection,
session, authentication, query service, replies — are transport. It has been a host wearing
a feature's name.

Open item, low consequence: `a.` (avatars) and `assets.` both straddle the line. Avatars are
read by the osu! client and by any UI; the assets host serves osu-web-style crop variants.
The assignment above is a judgement, easily revised, and is called out here rather than
buried.

---

## 4. The cost of thick hosts, and what makes it survivable

Measured distribution of today's 191 feature files under this structure:

| Feature | Domain | Hosts.Bancho | Hosts.Api | Infrastructure | Projects |
|---|---:|---:|---:|---:|---:|
| Multiplayer | 16 | 24 | 3 | 2 | 5 |
| Users | 9 | 11 | 2 | 5 | 5 |
| Beatmaps | 15 | — | 3 | 5 | 4 |
| Content | 8 | — | 8 | 2 | 4 |
| Chat | 6 | 7 | — | 1 | 4 |
| Auth | 10 | — | 1 | 2 | 4 |
| Scores | 10 | — | 1 | 2 | 4 |
| Spectating | 7 | 4 | 1 | — | 4 |
| Irc | (becomes a host) | | | | |
| Bot | 6 | — | — | — | 2 |

This is the shape that made the previous Clean Architecture attempt slow. Three things make
it different here, and all three are measured rather than hoped for:

**The adapters are thin.** The 46 packet handlers have a median of 26 code lines and a mean
of 30; only two exceed 60. A behaviour change lands in `Domain` and usually does not touch
the handler at all. The previous attempt's pain came from changes that had to be threaded
through every layer; a 26-line adapter that translates a packet into a service call does not
need threading.

**Only two files today mix transport with business**, `MatchSubResourceRoutes` (1,424 lines)
and `MatchRoutes` (627). Both are already scheduled for decomposition. They are the only
places where a move would drag business logic into a host project, and they must be split
before they move, not after.

**There are four projects, not four layers per feature.** A feature does not pass a request
down a chain: a handler calls a service, a service calls a repository contract. Two hops,
the same as today.

The failure mode to watch for is adapters growing logic. §5 states it as an invariant with a
number attached.

---

## 5. Invariants

1. **`Basil.Domain` has no framework reference and no host reference.** Enforced by the
   absence of the project reference, so it cannot be violated by editing a test.
2. **No host project references another host project.** Same enforcement.
3. **Every piece of per-player runtime state has exactly one owning feature.** Only that
   feature writes it; others read through the owner's contract.
4. **A feature reaches another feature only through a published contract owned by the
   callee.** Internals stay internal.
5. **Cross-feature reactions to lifecycle events go through events**, not direct calls.
6. **Transport adapters translate; they do not decide.** A packet handler or route that
   grows past roughly 60 code lines, or that mutates domain state directly, is a signal
   that logic has leaked out of `Domain`. Today two files fail this and both are known.
7. **The feature graph inside `Basil.Domain` has no cycles** except those explicitly
   declared with a recorded reason. Target: at most the three of §7.
8. **An abstraction exists only where it carries a boundary**, a real implementation swap,
   a lifecycle, or an external integration. One implementation and no boundary means no
   interface.

### The enforcement gap must be closed first

Invariants 3–7 are namespace rules, and the current mechanism cannot see constant-mediated
references — the assessment verified that nine live edges are invisible because C# inlines
`const` values. Fixing that is a prerequisite, not a step: otherwise every later step
reports a success it has not earned.

Invariants 1 and 2 do not depend on it, which is exactly why they are worth spending
projects on.

---

## 6. How the settled decisions resolve the assessment's findings

**F3, the session god object, dissolves along the new boundaries rather than by decree.**
`GameSession` is not one thing; the layering makes that visible:

| Member | Belongs to | Because |
|---|---|---|
| `Enqueue(byte[])`, `Dequeue()` | `Basil.Hosts.Bancho` | a bancho packet queue is that transport's outbox |
| `IrcConnection` | `Basil.Hosts.Irc` | an IRC socket is that transport's |
| `Id`, `Name`, `Privilege`, `LoginTime`, `Country`, channels | `Basil.Domain` | player identity, no feature owns it exclusively |
| `Match` | `Basil.Domain/Multiplayer` | already written only by Multiplayer |
| `Spectating`, `Spectators` | `Basil.Domain/Spectating` | already written only by Spectating |
| `ModeStats`, `Status` | `Basil.Domain/Users` | already written only by Users |
| `InLobby` | `Basil.Domain/Chat` | already written only by Chat |
| `MpScopeMatchId` | `Basil.Domain/Multiplayer` | it is match scope, currently held by Bot |

Every row is a placement the existing write-ownership already implies. The object was a god
object because there was nowhere else to put its parts; now there is.

**`MatchSession` also splits, and the rule is what forces it.** It currently holds eight
`StateStream<T>` snapshot channels, an `SseSubscriberRegistry` and a `SequenceGate` — live
projection machinery that serializes JSON merge patches. Under invariant 1 that cannot live
in `Basil.Domain`. The business object keeps slots, host, settings and progress; the
projection moves out to the API host. This is the direction Tasks 1.3 and 1.4 of the paused
plan were already heading; the layering rule makes it obligatory rather than optional.

**The four layering violations to fix** (§2): `Auth/LoginService`,
`Beatmaps/BeatmapsetGarbageCollectorService`, `Beatmaps/BeatmapsetMigrationService`,
`Beatmaps/BeatmapWatcherService` reach the database directly. Each gets a contract in
`Domain` and an adapter in `Infrastructure`. **The two transport/business mixes** —
`MatchSubResourceRoutes`, `MatchRoutes` — are decomposed before they move.

**F4, `Bot`.** `MpCommandService` (1,422 lines) and `MpReplies` (467) are multiplayer
behaviour and move to `Basil.Domain/Multiplayer`. What remains of `Bot` — the dispatcher and
the reply sink, about 730 lines — is a command surface over chat text, and belongs in
`Basil.Domain/Bot` with its transport bindings in the hosts that carry chat.

**F1, the single component.** The assessment simulated the fix: extracting shared contracts,
identity and constants takes the graph from 44 edges to 20 and frees four features; making
logout an event leaves 17 and frees a fifth. Under this structure those extractions are not
optional cleanups — they are what `Basil.Domain` being framework-free and cycle-checked
requires.

---

## 7. The three relationships that survive

After every mechanical fix, three mutual dependencies remain, and they are genuine domain
relationships rather than accidents:

* **match ↔ chat channel** — a match owns a channel; the channel routes commands back.
* **match ↔ score** — a match records scores; a score reports on its match.
* **command dispatch ↔ match control** — `!mp` executes match operations; match state
  produces replies.

Each gets an explicit direction and a recorded reason rather than a bidirectional reference.
The first two are read-model problems: the reader takes a projection, not the live object.
The third stops being a cycle once `MpCommandService` sits inside Multiplayer.

---

## 8. Migration order

Each step leaves the build green and is measurable. The edge count from the assessment is
the progress metric.

| # | Step | Why here |
|---|---|---|
| 0 | Fix dependency enforcement so constant-mediated references are visible | every later step's verification depends on it |
| 1 | Delete the 5 dead and 7 doc-only imports | cheap; removes noise from every later measurement |
| 2 | Split `MatchSubResourceRoutes` and `MatchRoutes` | they are the only files that would carry business logic into a host project |
| 3 | Fix the four services that reach the database | small, and required before `Domain` can be framework-free |
| 4 | Create `Basil.Domain` as the business layer: move services, live state, contracts, constants, `UserBrief` | the project reference is what makes invariant 1 real; expect the feature graph to drop from 44 edges to ~20 |
| 5 | Split `GameSession` per §6 | removes the last `Shared → Features` couplings and the 71-file blast radius |
| 6 | Logout and login become events | inverts the five-feature dependency; frees Auth from the knot |
| 7 | Move `MpCommandService`/`MpReplies` into Multiplayer | collapses Bot's 19-type edge |
| 8 | Create `Basil.Hosts.Bancho`, `.Irc`, `.Api`; rename `Basil.Server` to `Basil.Infrastructure`; `Basil.Host` keeps the entry point | done last, because moving files into host projects is only safe once steps 2–7 have removed the business logic from them |
| 9 | Declare the three surviving relationships with reasons | closes invariant 7 |

Steps 4 and 5 are the ones that pay. Step 8 is the most visible and the least urgent —
doing it earlier would move business logic into host projects and then have to move it out
again.

### What survives from the paused migration plan

Still valid and unaffected: the god-file decompositions (plan Tasks 1.5–1.7, which are step 2
here), the localization work, the logging taxonomy, the Diagnostic API (Phase 5) and the
load harness (Phase 6).

Superseded: Phase 1's Task 1.4 (`MatchSession` encapsulation) is absorbed by steps 5 and the
projection split; Phases 2–4 assumed the current ten slices are the target shape and must be
re-sequenced after step 8.

Phase 0 is not wasted. Its per-slice DI, routing, metrics and locale seams are the mechanism
this migration uses to move things, and the `LiveEventHub` is what step 6 needs.

---

## 9. ADRs to write

1. **The layering rule and where it is enforced by projects versus tests.** Records that
   `Basil.Domain` and the host projects carry compiler-enforced boundaries, and everything
   else is namespace rules.
2. **Dependency enforcement must see constants**, or shared constants become
   `static readonly`. Records the const-inlining blind spot and the remedy chosen.
3. **Per-player runtime state is owned by one feature.** Records the `GameSession` split
   table and the read path for other features.
4. **Three host projects, and what each owns.** Records the thick-host choice, its measured
   cost, and the thin-adapter invariant that keeps it workable.
5. **`Irc` is a host, not a feature.**
6. **Cross-feature lifecycle reactions use events.** Names the events and why logout in
   particular is not a method call.
7. **Data access stays hand-written over Dapper.** Records that repositories are not
   mandatory, the criteria for using one, and why no ORM is being introduced.
8. **The three permitted mutual relationships**, each with its reason.
9. **Abstractions require a stated justification.**

---

## 10. Open items

* Host assignment for `a.` (avatars) and `assets.` — argued in §3.3, low consequence.
* Whether `Basil.Domain` keeps its current name once it holds live state and services, or
  becomes something that says so more plainly.
* Whether `Basil.Protocol` counts as a transport framework under the layering rule. It is
  the project's own wire format with no external dependencies, and packet handlers live in
  a host, so the question is only whether `Basil.Domain` may reference it at all. Current
  answer: it should not need to, and the migration should discover whether that holds.
