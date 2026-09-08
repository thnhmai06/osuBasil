# osuBasil — Architecture Assessment

**Date:** 2026-09-08
**Scope:** `Basil.Domain`, `Basil.Protocol`, `Basil.Server` at commit `06621f2`
**Status:** assessment and proposal. Nothing here has been implemented.

This document was written by measuring the code. Where a plan, a design document or a
comment disagrees with what the compiler sees, the code wins and the disagreement is
recorded as a finding.

---

## 0. Method, and what it can and cannot see

Every number below comes from one of four measurements:

1. **Import graph.** Every `using Basil.Server.Features.X` / `Basil.Server.Shared.X` in
   every `.cs` file, attributed to the slice or segment the file lives in.
2. **Liveness filter.** For each import, whether any type declared in the target
   namespace actually appears in the importing file's code, in its comments only, or
   nowhere. This separates real edges from dead and documentation-only imports.
3. **Strongly connected components** over the resulting directed graph (Tarjan).
4. **Targeted reads** of the types the graph pointed at, to confirm what an edge is
   actually made of.

Two corrections were made during the analysis and are worth stating, because they
changed conclusions:

* A naive `\.Match\b` search attributed dozens of `Regex.Match` calls to the multiplayer
  session field of the same name. The corrected count is roughly a third of the naive one.
* A naive type-name extractor picked up English words out of prose in XML comments
  (`record the …` yielding a "type" named `the`), which made five dead imports look live.
  The extractor now requires an access modifier and an uppercase initial, and skips
  comment lines.

**What this method cannot see:** references that never produce an import, either because
the types share a namespace or because they are fully qualified. It also cannot see
runtime coupling that flows through a shared mutable object without a type reference.
Both would make the numbers below *understatements*, never overstatements.

---

## 1. What the architecture is today

### Projects

```
Basil.Domain      no project references
Basil.Protocol    no project references
Basil.Server      -> Domain, Protocol
```

`Basil.Domain` holds the domain model. `Basil.Protocol` is a standalone bancho packet
layer. Everything else lives in `Basil.Server`.

### Inside `Basil.Server`

Three areas: `Features/` (10 slices), `Shared/` (9 segments), `Host/` (14 composition
files).

| Slice | Files | Lines | | Shared segment | Files | Lines |
|---|---:|---:|---|---|---:|---:|
| Multiplayer | 46 | 8,667 | | Http | 33 | 3,465 |
| Beatmaps | 24 | 4,939 | | Eventing | 11 | 943 |
| Bot | 7 | 2,643 | | Media | 10 | 652 |
| Users | 28 | 2,258 | | Sessions | 6 | 637 |
| Content | 19 | 2,045 | | Storage | 4 | 318 |
| Scores | 14 | 1,570 | | Persistence | 5 | 177 |
| Chat | 15 | 1,397 | | Configuration | 6 | 154 |
| Irc | 11 | 1,317 | | Localization | 3 | 116 |
| Auth | 14 | 1,171 | | Logging | 2 | 91 |
| Spectating | 13 | 583 | | | | |

Features total roughly 26,600 lines; `Shared` roughly 6,550, about a fifth of the server.
`Shared/Http` alone is larger than every slice except Multiplayer and Beatmaps.

### Hosting

Six host groups — `bancho`, `osu`, `b` (beatmap assets), `a` (avatar), `api`, `assets` —
are all constructed in one file, `Shared/Http/BanchoHostGroups.cs`, as a record of
`RouteGroupBuilder`s handed to `Host/SliceRegistration.MapAll`.

Route ownership is split. The `api` host's routes are contributed by the slices that own
them. The `bancho`, `osu` and `assets` hosts have their routes mapped from `Shared/Http`.

### Runtime state

Four in-memory registries hold the server's live state:

| Registry | Owner | Holds |
|---|---|---|
| `InMemoryMatchRegistry` | Multiplayer | matches |
| `InMemoryChannelRegistry` | Chat | chat channels |
| `IrcSessionRegistry` | Irc | IRC sessions |
| `GameSessionRegistry` | **Shared** | connected players |

Three of the four registries are owned by the feature whose state they hold. The fourth
is not, and its *element* type is where the trouble is.

### Enforcement

`Basil.ArchitectureTests` contains six tests. Two govern the inside of `Basil.Server`:

* `Slices_Should_Only_Reference_Declared_Slices` — a slice may reference another only via
  an edge declared in `SliceAdjacency`, which declares 38.
* `Shared_Should_Not_Reference_Features` — asserted as exact set equality against a pinned
  list of 12 existing violations, so the list can only change deliberately.

---

## 2. Findings, ranked

### F1 — There is no dependency direction between features at all *(critical)*

The ten slices form **one strongly connected component**. Every slice can reach every
other slice by following real code references.

Measured: 44 live cross-slice import edges out of 90 possible ordered pairs. Removing the
5 dead imports and 7 documentation-only imports does not break the component; it survives
on code references alone.

Fifteen of those edges are mutual — `Auth ↔ Users`, `Bot ↔ Chat`, `Bot ↔ Multiplayer`,
`Chat ↔ Multiplayer`, `Chat ↔ Irc`, `Multiplayer ↔ Scores`, `Multiplayer ↔ Spectating`,
`Multiplayer ↔ Users`, and seven more.

This is the finding everything else serves. A dependency graph with one component has no
layering, no direction and no substitutable parts: any change can propagate anywhere, and
no slice can be reasoned about, tested or replaced without the other nine. The `Features/`
folder tree suggests ten independent units; the compiler sees one.

### F2 — The rule meant to prevent F1 cannot see a whole class of coupling *(critical)*

`SliceAdjacency` declares 38 edges. The code contains 44 live ones. Nine edges exist in
the source that the allowlist does not declare, **and the architecture test still passes.**

The reason is verified, not guessed: `NetArchTest` reads IL, and the C# compiler inlines
`const` values at the use site. A slice that consumes only constants from another slice
leaves no assembly reference behind.

Two constant-bearing types account for seven of the nine invisible edges:

| Type | Owner | Invisible edges it creates |
|---|---|---|
| `AdminKeyDefaults` (`Scheme`, `Policy`, `Role` — all `public const string`) | Auth | Beatmaps→Auth, Content→Auth, Multiplayer→Auth |
| `BotBootstrapService.BotId` (`public const int`) | Bot | Auth→Bot, Content→Bot, Multiplayer→Bot, Users→Bot |

The remaining two are an unused import and a documentation-only reference.

This matters more than the nine edges themselves. Policy names, scheme names, system user
ids and route-documentation strings are exactly the things a codebase expresses as
constants, so the blind spot sits precisely where cross-cutting coupling accumulates. The
one mechanism claiming to enforce slice boundaries is structurally unable to see it, which
means a green architecture suite is currently weaker evidence than it appears.

### F3 — Session state: ownership exists in practice but is unexpressed in types *(critical)*

`GameSession` lives in `Shared/Sessions`. It is referenced by **71 files across 9 of the
10 slices**, and it imports `Features.Multiplayer` and `Features.Irc` itself. Its sibling
`PlayerLogoutService`, also in `Shared`, imports five feature slices.

The object carries state belonging to at least four features:

```
GameSession
├── Match          -> Multiplayer
├── Spectating, Spectators, AddSpectator, RemoveSpectator  -> Spectating
├── ModeStats, CurrentStats, Status  -> Users
├── IrcConnection  -> Irc
├── InLobby        -> Chat
└── MpScopeMatchId (on UserSession)  -> Bot
```

The important nuance, which changes the cost of fixing it: **writes are already
well-owned.** `.Match` is written only by Multiplayer, `.Spectating` only by Spectating,
`.InLobby` only by Chat, `.Privilege` only by Auth. Nobody is scribbling on anyone else's
field today.

That is good news about the current code and bad news about the current architecture. The
ownership is real but exists only as a convention. The type system grants all 71 files
compile-time access to every feature's session state, so nothing prevents the next change
from breaking the convention, and nothing tells a reader it exists.

This is not theoretical. The bug found and fixed during Task 1.1 this session —
`LeaveAsync` parting a chat channel between clearing a slot and reassigning the host,
leaving the match naming an unseated host if an IRC send threw — is exactly this shape: a
single function reaching across Multiplayer, Chat and Irc through one session object, with
a fallible broadcast in the middle. Potential coupling is what made that reachable.

### F4 — `Bot` is a transport that holds another feature's domain logic *(high)*

`Bot` is 7 files and 2,643 lines. Of those, `MpCommandService` (1,422) and `MpReplies`
(467) are **1,889 lines of multiplayer command handling and multiplayer user-visible
strings**. The genuine bot concern — `CommandDispatcher`, `ICommandDispatcher`,
`ICommandReplySink`, `BotBootstrapService` — is about 730 lines.

This is why `Bot` reaches six slices: it is a façade executing other slices' operations.
Its edge to Multiplayer alone touches nineteen distinct types, including every
`…Result` enum of the match control surface.

`Bot` is the clearest case of a boundary drawn in the wrong place. The command *transport*
(parse a chat line, route it, format a reply) is a real cross-cutting concern. The
multiplayer *command surface* is Multiplayer's.

### F5 — Hosts are not boundaries *(high)*

The six host groups exist as a record of route-builder handles in one shared file. Nothing
prevents a route mapped onto one host from depending on another host's internals, and
three of the six hosts have their routes mapped from `Shared/Http` rather than from the
feature that owns the behaviour.

The `api` host demonstrates the pattern that works — `hosts.Api.MapUsersRoutes()`,
`MapMultiplayerRoutes()`, and so on, each contributed by its slice. `bancho`, `osu` and
`assets` do not follow it. Transport concerns with genuinely different lifecycles (a
long-lived binary packet exchange, a legacy web surface, a static file surface) are
currently distinguished only by which strings appear in a `RequireHost` call.

### F6 — `Shared` is where the two worst problems live *(high)*

`Shared` is a fifth of the server, and its two largest segments are the two areas above:
`Shared/Http` (3,465 lines, holding three hosts' routing and the bancho packet dispatcher)
and `Shared/Sessions` (637 lines, holding the god object). The 12 pinned
`Shared → Features` violations are concentrated in exactly these two segments plus
`Shared/Media`.

The pinned-violation test is a genuine ratchet — it asserts exact set equality, so the
list cannot grow silently and cannot shrink by accident. It is doing its job. But a
ratchet on a list of 12 is a promise to fix them later, not a boundary.

### F7 — Dead and documentation-only imports *(low)*

Five imports reference a slice whose types never appear in the file (`Auth→Users` ×3,
`Multiplayer→Beatmaps`, `Users→Multiplayer`). Seven more appear only inside a
`<see cref>` in an XML comment. Neither creates a compiled dependency; both create a
misleading `using` block. Trivial to fix, listed here so they are not mistaken for real
edges by the next reader.

---

## 3. The dependency graph, conceptually

What the folder tree implies:

```
Features/  Auth  Beatmaps  Bot  Chat  Content  Irc  Multiplayer  Scores  Spectating  Users
              (ten independent units)
Shared/    infrastructure they all sit on
Host/      composition
```

What the compiler sees:

```
                  ┌──────────────────────────────────────────────┐
                  │  one strongly connected component of 10       │
                  │  Auth Beatmaps Bot Chat Content Irc           │
                  │  Multiplayer Scores Spectating Users          │
                  └──────────────────────────────────────────────┘
                          │                        ▲
                          │ 44 live edges          │ 12 pinned violations
                          ▼                        │
                  ┌──────────────────────────────────────────────┐
                  │  Shared  (Http 3.4k, Sessions 0.6k, …)        │
                  │  GameSession: read by 9/10 slices, 71 files   │
                  └──────────────────────────────────────────────┘
```

The edges are not evenly distributed by kind. Sorting the 44 live edges by what they
actually touch:

| Kind of dependency | Example types | Edges |
|---|---|---:|
| Data access into another slice | `IUserRepository`, `IBeatmapRepository`, `ISettingsRepository` | ~13 |
| Channel membership | `IChannelRegistry`, `ChannelMembershipService`, `ChannelSession` | ~5 |
| A session type owned elsewhere | `IrcSession` | ~5 |
| Constants | `AdminKeyDefaults`, `BotBootstrapService.BotId` | 7 |
| Genuine domain collaboration | `MatchSession`, `ScoreReport`, `MatchChatMessage` | ~14 |

Roughly two thirds of the coupling is not domain collaboration at all. It is features
reaching through each other to get at data, identity and constants that they all
legitimately need.

**This is the decisive measurement**, because it says the mesh is mostly accidental. It
was tested by simulation rather than asserted:

| After | Live edges | Components |
|---|---:|---|
| today | 44 | one SCC of 10 |
| extracting shared data-access contracts, session identity and constants | 20 | SCC of 6 + Beatmaps, Content, Spectating, Users standing free |
| …and turning logout into an event instead of `Auth` calling four features | 17 | SCC of 5 + Auth free |

The knot that survives is small and is genuinely mutual:

```
Chat ──▶ Bot ──▶ Multiplayer ──▶ Chat        (command dispatch ↔ match control ↔ match channel)
Multiplayer ◀──▶ Scores                       (a match records scores; a score reports on a match)
Irc ──▶ Chat, Multiplayer ──▶ Spectating      (transport and live views)
```

Those are real bidirectional domain relationships. They need an explicit mechanism, not a
folder move — and there are three of them, not fifteen.

---

## 4. What the evidence rules in and out

The candidate space is narrower than a survey would suggest, because the constraints are
already stated and the measurements already discriminate.

**Ruled out — strict Clean Architecture.** Previously tried and rejected for this project,
and the evidence supports the rejection rather than contradicting it. F1 is not caused by
too few layers; two thirds of the coupling is data access and identity, which a
layer-per-project split makes *more* ceremonious without making it directional. The
`IUserRepository` edge from five slices does not become better by moving `IUserRepository`
one project down; it becomes better by deciding who may call it and how.

**Ruled out — vertical slices as they stand.** The current arrangement is where the
evidence was taken. It fails F1 by construction: nothing in "one project, ten folders"
creates a direction, and the one rule that tries (F2) is blind to constants.

**Ruled out — a survey-driven answer.** Hexagonal, Onion and Modular Monolith all agree on
the three things the measurements say are wrong: a shared mutable element with no owner,
transports that are not boundaries, and cross-module calls that are not declared. Choosing
between their vocabularies does not change what has to be built.

**What the evidence actually asks for**, in its own terms:

1. A **direction**. Something every feature may depend on, which depends on no feature.
   Measured payoff: 44 edges → 20, and four slices leave the knot.
2. **State that belongs to somebody**. The convention already exists (F3); it needs to be
   expressed so the compiler enforces it.
3. **Transports that are units**, not strings in a `RequireHost` call (F5).
4. **A mechanism for the three genuine mutual relationships** that survive, so they are
   deliberate and visible rather than a cycle.
5. **An enforcement rule that can see constants** (F2), or every rule above degrades
   silently.

---

## 5. Proposed target architecture

Four kinds of unit. The names matter less than the dependency rule, which is one line:

> **Platform ← Features → Hosts. Features never reference each other's internals; the
> platform never references a feature.**

### 5.1 Platform (new — a kernel, explicitly not a `Shared` dumping ground)

The thing that gives the graph its direction. It contains only what every feature
legitimately needs and what depends on no feature:

* **Player identity and connection**: the parts of `UserSession` / `GameSession` that are
  not any feature's — id, name, privilege, login time, token, connection, channel list.
* **Data-access contracts** every feature reads: the read side of `IUserRepository`,
  `IBeatmapRepository`, `ISettingsRepository`.
* **Constants and shared values**: `AdminKeyDefaults`, the system user ids, `UserBrief`.
* **Mechanisms with no domain content**: eventing, persistence plumbing, localization,
  logging, configuration, metrics, the HTTP envelope and JSON options.

The distinction from today's `Shared` is a rule, not a size limit: **the platform may not
reference `Features`, and this is enforced with no pinned exception list.** Today's 12
pinned violations become the migration's definition of done.

### 5.2 Features

Vertical slices, kept — the user's requirement 2, and the part of the current arrangement
that works. Each owns its endpoints, handlers, persistence, DI, metrics, locale fragment
and help text, as it does now.

Two changes:

* **A feature owns its own runtime state**, including the per-player part. Instead of
  `GameSession.Match`, Multiplayer keeps its own map from player id to match. Instead of
  `GameSession.Spectating`, Spectating keeps its own. This is F3's convention made real;
  because the convention already holds, this is a mechanical change, not a redesign.
* **A feature exposes a contract for what other features may call**, owned by the callee
  and deliberately small. `Bot` calling into Multiplayer goes through Multiplayer's
  contract, not through nineteen of its internal types.

`MpCommandService` and `MpReplies` move to Multiplayer (F4). `Bot` keeps the transport.

### 5.3 Hosts

Each host — bancho, api, osu-web, assets, avatar, beatmap assets, IRC — becomes a unit
that owns its own composition and its own transport concerns. A feature *contributes*
endpoints to a host; a host does not reach into a feature, and no host knows another
host's internals (F5).

This is where `Shared/Http`'s 3,465 lines go: the per-host routing to its host, the
envelope/JSON/OpenApi machinery to the platform, the bancho packet dispatcher to the
bancho host.

### 5.4 The three mutual relationships

The knot that survives every mechanical fix gets an explicit mechanism rather than a
direct call:

* **Lifecycle events** for "something happened to a player" — logged out, logged in,
  silenced. Today `Auth` and `Shared/PlayerLogoutService` call four features directly;
  instead they publish, and Multiplayer, Spectating, Chat and Irc subscribe. This inverts
  three edges and is the single highest-value change after the platform extraction.
* **A match's chat channel** — Multiplayer ↔ Chat. Model the channel as something
  Multiplayer *requests* from Chat's contract, so the edge is one-way.
* **A match's scores** — Multiplayer ↔ Scores. Scores reads a match through a read model
  on the platform (`UserBrief` already wants to live there), not through
  `MatchLiveSnapshotBuilder`.

### 5.5 How much of this is projects versus folders

The user's requirement 3 asks for real boundaries, not folders. But the previous Clean
Architecture attempt failed partly on project sprawl, so this proposal is deliberately
conservative:

**Split into projects only where the dependency rule must be mechanically impossible to
violate.** That is the platform, because it is the thing whose purity gives the graph its
direction: `Basil.Platform` as a project that cannot reference `Basil.Server` because the
project reference does not exist.

**Everything else stays in folders inside `Basil.Server`, governed by tests** — but tests
that work, which means fixing F2 first. Features and hosts do not each become a project;
that is the sprawl that made the previous attempt slow.

Expected end state:

```
Basil.Domain      no project references
Basil.Protocol    no project references
Basil.Platform    -> Domain, Protocol        (cannot reference Server: no reference exists)
Basil.Server      -> Domain, Protocol, Platform
   Features/<Slice>/     owns state, endpoints, contract
   Hosts/<Host>/         owns composition and transport
   Host/                 startup
```

Four projects, one more than today, and the new one is the one that carries the rule.

---

## 6. Invariants

Stated so they can be tested, not admired.

1. **The platform never references a feature or a host.** No pinned exception list. This
   is the invariant that gives the graph direction; an exception list would dissolve it.
2. **A feature references another feature only through that feature's published
   contract**, and the reference is declared. Internals stay internal.
3. **Every piece of per-player runtime state has exactly one owning feature**, and only
   that feature can write it. Cross-feature reads go through the owner's contract.
4. **A host never references another host.** Features contribute endpoints to hosts;
   hosts do not reach into features.
5. **Cross-feature reactions to lifecycle events go through the event mechanism**, not a
   direct call. If a feature needs to know that a player logged out, it subscribes.
6. **The feature graph, excluding platform edges, has no cycles** except those explicitly
   declared with a recorded reason. Today there are fifteen mutual pairs and none is
   declared; the target is at most the three of §5.4, each named.
7. **An abstraction exists only where it carries a boundary**, a real implementation
   swap, a lifecycle, or an external integration. One implementation and no boundary means
   no interface. (Requirement 5, stated as a rule so review can apply it.)

### The enforcement gap that must be closed first

Invariants 1, 2, 4 and 6 are all namespace-dependency rules, and today's mechanism cannot
see constant-mediated references (F2). **Fixing the enforcement is a prerequisite for the
migration, not a step within it** — otherwise each step reports success it has not earned.

Two options, to be settled in an ADR: verify dependencies from source with Roslyn rather
than from IL with NetArchTest, or forbid cross-slice `const` consumption by making shared
constants `static readonly` (which does leave an IL reference). The second is cheaper and
narrower; the first is more complete.

---

## 7. Migration strategy

Ordered so that each step is independently valuable, verifiable, and leaves the build
green. The measured edge count is the progress metric.

| # | Step | Removes | Verified by |
|---|---|---|---|
| 0 | **Fix enforcement.** Make cross-slice constant coupling visible (F2). Re-derive the true edge set; expect the declared allowlist to grow to ~44 before it shrinks. | nothing yet | the allowlist matches measurement |
| 1 | **Dead and doc-only imports** (F7). | 12 misleading imports | import graph |
| 2 | **Extract `Basil.Platform`**: constants, `UserBrief`, data-access read contracts, eventing, persistence, localization, logging, configuration, JSON/envelope. No feature references. | 44 → ~20 edges; Beatmaps, Content, Users leave the knot | SCC over the feature graph |
| 3 | **Split the session object** (F3). Identity to the platform; `Match`, `Spectating`, `InLobby`, `MpScopeMatchId` to their owning features. Writes already respect ownership, so this is mechanical. | the `Shared → Features` pinned list, and the 71-file blast radius | pinned list empty; `Shared_Should_Not_Reference_Features` needs no exceptions |
| 4 | **Logout and login become events.** `PlayerLogoutService`'s five-feature dependency inverts into five subscribers. | ~3 edges; Auth leaves the knot | SCC; the service's imports |
| 5 | **Move `MpCommandService` + `MpReplies` to Multiplayer** (F4); `Bot` keeps the transport and calls Multiplayer's contract. | Bot's 19-type edge collapses to one contract | edge type count |
| 6 | **Hosts become units** (F5). `Shared/Http` dissolves into per-host composition plus platform mechanisms. | the largest `Shared` segment | no host references another; `Shared/Http` gone |
| 7 | **Declare the three surviving mutual relationships** (§5.4) with recorded reasons, and give the match/chat and match/score pairs an explicit direction. | the last cycles | cycle count = declared count |

Steps 2 and 3 are the ones that pay. Steps 0 and 1 are cheap and make everything after
them measurable.

### Relationship to the paused migration plan

`plans/vsa-migration-plan-20260907.md` remains valid where it is about code quality and
does not depend on the current boundaries: the god-file decompositions (Tasks 1.5–1.7),
localization, the logging taxonomy, the Diagnostic API (Phase 5) and the load harness
(Phase 6).

What must be re-sequenced: anything that assumes the ten current slices are the target
shape. In particular Task 1.4 (`MatchSession` encapsulation) overlaps step 3 here, and
Phases 2–4 would otherwise repeat the current boundaries three more times.

Phase 0 is not wasted. The per-slice DI, routing, metrics and locale seams it built are
the mechanism this migration uses to move things; the `LiveEventHub` is what step 4 needs.

---

## 8. Decisions that should become ADRs

1. **The platform is a project, features and hosts are folders.** Why one project split
   and not four; why the previous project sprawl is not repeated.
2. **Dependency enforcement is source-based, or shared constants are `static readonly`.**
   Records the const-inlining blind spot and which remedy was chosen.
3. **Per-player runtime state is owned by one feature.** Names the split of
   `GameSession`, and states the read path for other features.
4. **Cross-feature lifecycle reactions use events, not direct calls.** Names the events
   and why logout in particular is not a method call.
5. **A feature's public contract is owned by the callee.** Defines what may cross a
   feature boundary and what stays internal.
6. **Hosts are units and never reference each other.** Records how a feature contributes
   endpoints.
7. **The three permitted mutual relationships**, each with its reason: match↔chat channel,
   match↔score, command dispatch↔match control. Anything else is a defect.
8. **Abstractions require a stated justification.** The rule from invariant 7, so review
   has something to point at.

---

## Appendix — key measurements

* 44 live cross-slice import edges; 7 documentation-only; 5 dead.
* All ten slices in one strongly connected component; it survives removing dead and
  doc-only imports.
* 15 mutual (two-cycle) slice pairs.
* `SliceAdjacency` declares 38 edges; 9 live edges are undeclared and invisible to the
  test, 7 of them through `AdminKeyDefaults` and `BotBootstrapService.BotId` consts.
* `GameSession`: 71 referencing files across 9 of 10 slices; imports Multiplayer and Irc.
* `PlayerLogoutService` (in `Shared`) imports 5 feature slices.
* Writes to cross-feature session fields are already single-owner: `.Match` Multiplayer
  only, `.Spectating` Spectating only, `.InLobby` Chat only, `.Privilege` Auth only.
* `Bot`: 1,889 of 2,643 lines are multiplayer command handling and replies.
* Simulated platform extraction: 44 → 20 edges, SCC of 10 → SCC of 6 plus four free
  slices. Adding logout-as-event: 17 edges, SCC of 5.
