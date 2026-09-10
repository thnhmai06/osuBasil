# Basil Implementation Plan — 2026-09-09

> **For agentic workers:** work task by task. Each task ends green and committed. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** give Basil a dependency architecture — a business layer that no framework reaches into,
transports that are real boundaries, and runtime state that belongs to somebody — while finishing
the refactor and observability work the original plan started.

**Supersedes:** `plans/vsa-migration-plan-20260907.md`. That plan is not deleted; §10 below records
which of its 57 tasks are alive, absorbed or dead, and alive ones are still executed from their
original text.

**Specs:**

* `plans/architecture-assessment-20260908.md` — the measurements every decision here rests on
* `plans/architecture-target-20260908.md` — the target structure, its invariants and their reasons
* `plans/vsa-migration-design-20260907.md` — the eventing, mutation-scope and diagnostics design

---

## Global constraints

Copied verbatim from the specs. Every task's requirements implicitly include this section.

* **Business code never references a framework.** No web or HTTP framework, no ORM, no database
  library or implementation, no transport framework, no external service implementation.
  `Basil.Domain` also never references `Basil.Protocol.*` (target invariant 9).
* **No host project references another host project.**
* **Every piece of per-player runtime state has exactly one owning feature.** Only that feature
  writes it.
* **A feature reaches another feature only through a contract owned by the callee.**
* **Cross-feature reactions to lifecycle events go through events**, never a direct call.
* **Transport adapters translate; they do not decide.** A packet handler or route past roughly 60
  code lines, or one that mutates domain state directly, means logic has leaked out of `Domain`.
* **An abstraction exists only where it carries a boundary**, a real implementation swap, a
  lifecycle, or an external integration. One implementation and no boundary means no interface.
* **Refactor with the IDE, not with a text editor.** Rider MCP is connected to the solution in
  `V:\Code\cs\osuBasil` and its refactorings update every reference the IDE indexes. Use them for
  anything that changes an identifier or the shape of a call:

  | Change | Tool |
  | --- | --- |
  | rename a type, member or parameter | `mcp__rider__rename_refactoring` |
  | move a type to another namespace | `mcp__rider__move_type_to_namespace` |
  | add, remove or reorder parameters | `mcp__rider__change_api_signature` |
  | delete a type or member | `mcp__rider__safe_delete` |
  | find every usage before deciding | `mcp__rider__find_references` |
  | pull a block out into a method | `mcp__rider__extract_method` |

  A search-and-replace finds text. These find *references*, which is not the same set: `nameof(...)`,
  XML `<see cref>` and other language-aware usages are updated too, and a conflict refuses the
  refactoring instead of leaving a broken build to discover later. Pass `preview: true` first on
  anything with a wide blast radius and read `affects` before applying.

  **Caveat:** Rider MCP is bound to the open solution, which is the main tree only. In
  `V:\Code\cs\osuBasil-diagnostics` there is no Rider instance, so text editing plus the compiler
  is the fallback there — and worth saying out loud in a report when it was used.

  This matters most in Stages C and D, which move roughly ninety-six files between projects and
  namespaces. That is `move_type_to_namespace` work, not `sed` work.
* **Never run tests in the background, and never run the whole suite in one call.** Measured
  2026-09-10: `dotnet test` over the solution exceeds the 600-second tool ceiling, because
  `Basil.IntegrationTests` alone takes five and a half minutes. A single foreground call cannot
  finish, so the tool backgrounds it — which is the failure this rule exists to prevent, reached by
  obeying the rule. Instead: `dotnet build` once, then one foreground `dotnet test --no-build` call
  per test project, integration tests last.

  ```bash
  dotnet build --configuration Debug
  for p in Basil.ArchitectureTests Basil.Domain.Tests Basil.Protocol.Tests Basil.Server.Tests Basil.IntegrationTests; do
      dotnet test "tests/$p" --no-build --configuration Debug
  done
  ```

  Backgrounded `dotnet test` has stalled more than five workers here; it is the most common way a
  worker dies with uncommitted work.
* **Commit the moment a task is green.** Session limits end workers mid-task; a committed task is
  never redone.
* Commit messages, code comments and documentation are written in normal English prose. Comments
  are self-contained: they explain the reason, never cite a document or ADR number as the reason.

**Test oracle.** Per tree, 0 failed / 0 skipped, and the passing count is:

| Tree | Branch | Count | As of |
|---|---|---|---|
| `V:\Code\cs\osuBasil` | `feat/vsa-migration` | 1658 | `1c29ce45` |
| `V:\Code\cs\osuBasil-diagnostics` | `feat/vsa-phase-5-diagnostics` | 1699 | `b1904ac3` |

The two counts differ because the diagnostics tree carries Stage F tests the main tree has not
merged yet. A task that changes its tree's count says why, and updates the row.

---

## Target solution layout

`Basil.slnx` already groups projects into `/Sources/` and `/Tests/`. The target adds two nested
folders so the transports and the protocols read as the units they are:

```text
/Sources/
    Basil.Domain                     business. No framework reference, no protocol reference.
    Basil.Infrastructure             external adapters. Was Basil.Server.
    Basil.Host                       entry point and composition.

/Sources/Protocol/
    Basil.Protocol.Bancho            binary packet format
    Basil.Protocol.Irc               IRC message format

/Sources/Hosts/
    Basil.Hosts.Bancho               bancho., osu., b.
    Basil.Hosts.Irc                  the TCP IRC listener
    Basil.Hosts.Api                  api., assets., a.

/Tests/
    Basil.Domain.Tests
    Basil.Infrastructure.Tests       was Basil.Server.Tests
    Basil.Hosts.Tests                the three host projects' adapters
    Basil.Protocol.Tests             both protocols
    Basil.IntegrationTests
    Basil.ArchitectureTests
    Basil.LoadTests
```

Project references, and nothing else:

```text
Basil.Domain            -> (nothing)
Basil.Protocol.Bancho   -> (nothing)
Basil.Protocol.Irc      -> (nothing)
Basil.Infrastructure    -> Domain
Basil.Hosts.Bancho      -> Domain, Protocol.Bancho, Infrastructure
Basil.Hosts.Irc         -> Domain, Protocol.Irc, Infrastructure
Basil.Hosts.Api         -> Domain, Infrastructure
Basil.Host              -> everything
```

Every project is organized by feature inside, so a feature's name appears in the same place
everywhere: `Basil.Domain/Multiplayer/`, `Basil.Hosts.Bancho/Multiplayer/`, and so on.

---

## Stage A — make enforcement real

Nothing else is verifiable until this is done. The architecture test currently passes while nine
real cross-slice edges exist, because the C# compiler inlines `const` values and NetArchTest reads
IL.

### Task A1: Prove which remedy actually closes the const blind spot

**This is an experiment whose result changes the plan. Do not skip to implementing one option.**

- [x] **Step 1: Reproduce the blind spot**

`Features/Multiplayer/MatchRoutes.cs` uses `AdminKeyDefaults.Policy`, a `public const string` in
`Features/Auth`. `SliceAdjacency` does not declare `Multiplayer -> Auth`. Confirm the architecture
suite is green anyway.

- [x] **Step 2: Try the cheap remedy**

Change `AdminKeyDefaults`'s three members and `BotBootstrapService.BotId` from `const` to
`static readonly`. Rebuild and re-run `Slices_Should_Only_Reference_Declared_Slices`.

Expected: it now fails, naming `Multiplayer -> Auth`, `Beatmaps -> Auth`, `Content -> Auth`,
`Auth -> Bot`, `Content -> Bot`, `Multiplayer -> Bot`, `Users -> Bot`.

- [x] **Step 3: If it fails to fail, stop and report**

If the seven edges stay invisible, `static readonly` is not enough and the rule must be verified
from source with Roslyn instead. That is a different task and a different ADR; report it rather
than improvising.

- [x] **Step 4: Record the result**

Write the outcome, with the exact test output, to `plans/execution/const-visibility-experiment.md`.
Commit.

### Task A2: Declare the edges the experiment revealed, then decide each one

- [x] Add the seven now-visible edges to `SliceAdjacency` **with the justification each already
  has** (they exist; they were only invisible). The suite goes green.
- [x] For each, record whether it survives the migration or is removed by a later task:
  `*/ -> Auth` via `AdminKeyDefaults` and `* -> Bot` via `BotBootstrapService.BotId` are both
  constants that move to `Basil.Domain` in Stage C, which removes the edge entirely.
- [x] Commit.

### Task A3: Remove the dead and documentation-only imports

Thirteen imports reference a slice whose types the file never uses in code:

* dead: `Auth/AdminKeyService.cs`, `Auth/BCryptPasswordHasher.cs`, `Auth/GuidTokenGenerator.cs`
  (all `-> Users`), `Multiplayer/MatchSubResourceRoutes.cs` (`-> Beatmaps`),
  `Users/UserRoutes.cs` (`-> Multiplayer`, and separately `Basil.Protocol.Multiplayer`)
* documentation-only: `Beatmaps/CachingBeatmapsetRepository.cs`, `Bot/ICommandDispatcher.cs`,
  `Bot/ICommandReplySink.cs`, `Irc/IrcAuthenticationService.cs`,
  `Irc/BanchoIrcBridgeConnection.cs`, `Irc/IIrcConnection.cs`,
  `Multiplayer/MatchControlService.cs`

- [x] Delete the dead ones outright.
- [x] For the documentation-only ones the `<see cref>` needs the import to resolve. Either keep the
  import and leave a one-line comment saying the reference is documentation only, or change the
  `cref` to plain text. Prefer plain text where the link adds nothing.
- [x] Build, run the full suite, commit.

---

## Stage B — untangle before anything moves

Every file that mixes transport with business must be split **before** Stage D moves files into host
projects, or the move drags business logic into a host and it has to come back out.

### Task B1: Split `MatchSubResourceRoutes` (1,424 lines, 33 members)

Was Task 1.7 of the old plan; execute from its text. One endpoint file per sub-resource under
`Features/Multiplayer/Endpoints/`: `MatchChatEndpoints`, `MatchHostEndpoints`,
`MatchRefereeEndpoints`, `MatchBanEndpoints`, `MatchSlotEndpoints`, `MatchTimerEndpoints`,
`MatchAbortEndpoints`, `MatchCloseEndpoints`. Request and response records move to the file that
uses them.

**Additional requirement not in the old task:** this file holds 16 of the 47 match-lock sites and
mutates match state directly. Each endpoint keeps its route and its argument binding; every state
change moves behind a service call. Verify the route table is byte-identical against
`plans/execution/baseline/routes.txt`.

### Task B2: Split `MatchRoutes` (627 lines)

Same treatment. It is the other file that mixes HTTP with domain mutation.

### Task B3: ~~Give the four database-touching services a contract~~ — closed, not needed

**Closed 2026-09-09 without a code change. The finding behind it was a measurement error.**

The pattern used to detect database access included an unanchored `ExecuteAsync`, which matches the
`ExecuteAsync` override every `BackgroundService` is required to declare. `Auth/LoginService`,
`Beatmaps/BeatmapsetGarbageCollectorService`, `Beatmaps/BeatmapsetMigrationService` and
`Beatmaps/BeatmapWatcherService` were flagged on that basis alone; all four already route through
repository contracts.

Re-measured with an anchored pattern:

```
using Dapper|SqliteConnection|IDbConnection|CommandDefinition|[.]QueryAsync|[.]QueryFirstOrDefault
```

every database call site in the server is a `Sqlite*Repository`, a `Sqlite*Store`, or the
persistence plumbing — sixteen files, every one an adapter. **The database boundary already holds.**

Use the anchored pattern for the Stage E verification. The unanchored one reports six false
positives, and it would report them again for anyone who re-derives this.

### Task B4: Finish `MatchMutationScope`

Old Task 1.2, unchanged, and its design question is already settled: **no `Invalidate()` and no
suppression path** (see `plans/execution/mutation-invariant-audit.md`, Decision). A worker was
dispatched for this and died on a session limit before writing anything.

### Task B5: Adopt the event hub

Old Task 1.3. Before writing anything, read the stale-subscriber hazard recorded under Task 0.10 in
`plans/execution/phase-0-foundation.md`, and the analysis in
`plans/execution/phase-1-multiplayer.md` under "Task 1.3's design question". The choice is between
handing the hub both a full snapshot and a delta, or stopping `LiveSubscription.OnPublish` from
adopting a delta as a snapshot. The second is cheaper and keeps the hub a loudspeaker; confirm
against the real call sites before committing to it.

### Task B6: Decompose `MatchControlService` (1,303 lines) and `MatchMembershipService` (902)

Old Tasks 1.5 and 1.6. **`MatchControlService` also owns audit Observation 1**: `SetHostAsync` and
`PUT /matches/{id}/hosts` never check that the target occupies a slot, which violates the host
invariant with no exception involved. **`MatchMembershipService` owns Observation 5**: `StartAsync`
can set `InProgress` with zero occupied slots. Fix each with the decomposition that already
rewrites it, and add a regression test that pins the invariant rather than the call order.

---

## Stage C — extract the business layer

**The tasks run C4, C3, C2, C6, C1, C5**, not in numbered order. Every restructuring step happens inside
`Basil.Server`, where the compiler checks each move and the suite is runnable at every intermediate
point; the project boundary is crossed once, at the end, on code that has stopped moving. A
half-finished restructuring is a compiling tree with a measurable edge count. A half-finished
ninety-six-file project move is not, and four workers on this project have now been killed mid-task.
The reasoning and C4's measured payoff are in `plans/execution/stage-c-order-decision.md`.


### Task C1: Create `Basil.Domain` as the business layer

The biggest single move. 96 files by measurement: services, live state, contracts.

- [ ] Add the project reference direction first: `Basil.Server -> Basil.Domain` already exists, so
  nothing new is needed — the constraint is that `Basil.Domain` gains **no** package reference to a
  framework. Verify by inspecting its csproj after the move, not by intention.
- [ ] Move per feature, one feature per commit, so a failure is revertable on its own.
- [ ] `MatchSession` splits here, and the layering rule is what forces it: its eight
  `StateStream<T>` channels, `SseSubscriberRegistry` and `SequenceGate` serialize JSON merge
  patches and cannot live in `Domain`. The business object keeps slots, host, settings and
  progress; the projection stays behind in what becomes the API host.
- [ ] `AdminKeyDefaults` and the system user ids move here too, which removes the seven edges Task
  A2 declared.
- [ ] Verify after every feature: full suite green, route table and metric names unchanged.

**What `Basil.Domain` will need, measured 2026-09-09.** Excluding routes, packet handlers,
persistence and DI files, 143 files are business-layer candidates. Of those:

| Uses | Files | Verdict |
|---|---:|---|
| `ILogger<T>` | 28 | **Allowed.** `Microsoft.Extensions.Logging.Abstractions` contains abstractions only, and the codebase already logs through it rather than through Serilog — zero feature files import Serilog. |
| `IOptions<T>` | 12 | **Allowed**, same reasoning. |
| `IMemoryCache` | 4 | **Not business.** `CachingBeatmapRepository`, `CachingBeatmapsetRepository`, `CachingSettingsRepository` and their sibling are caching decorators over repositories: they are infrastructure and go to `Basil.Infrastructure`, not `Domain`. |
| `HttpClient` | 1 | **Not business.** `HttpMirrorSearchClient` is an external-service adapter and goes to `Basil.Infrastructure`. |
| `IHostedService` | 0 | — |

So `Basil.Domain`'s package list ends up as the two abstraction packages and nothing else. Verify
that by reading its csproj after the move, not by intention: the constraint is a project that
*cannot* reach a framework, and a stray package reference silently removes that guarantee.

`AdminKeyAuthenticationHandler` also lands in this bucket by file classification but is an ASP.NET
authentication handler; it belongs to `Basil.Hosts.Api`.

### Task C2: Split `GameSession`

The god object dissolves along the boundaries rather than by decree. Every row below follows the
write-ownership the code already has:

| Member | Goes to |
|---|---|
| `Enqueue(byte[])`, `Dequeue()` | the bancho transport (Stage D; until then, `Basil.Server`) |
| `IrcConnection` | the IRC transport (same) |
| `Id`, `Name`, `Privilege`, `LoginTime`, `Country`, channel set | `Basil.Domain` |
| `Match` | `Basil.Domain/Multiplayer` |
| `Spectating`, `Spectators` | `Basil.Domain/Spectating` |
| `ModeStats`, `Status` | `Basil.Domain/Users` |
| `InLobby` | `Basil.Domain/Chat` |
| `MpScopeMatchId` | `Basil.Domain/Multiplayer` |

- [ ] Each feature keeps its own map from player id to its state, rather than a field on a shared
  object. **Those maps stay in `Features/<Slice>`.** The destination column above is where each
  member ends up once C1 has run; C2 splits in place and moves nothing between projects, because
  C1 is what crosses that boundary and doing half of it early is how the two tasks blur together.
- [ ] Verify: the `Shared_Should_Not_Reference_Features` pinned list loses its
  `Shared.Sessions.*` entries. The test asserts exact set equality, so the list must be edited
  deliberately — that edit is the proof.

### Task C3: Logout stops importing five slices

`PlayerLogoutService` imports five feature slices. **The design is settled in
`plans/execution/logout-as-event-decision.md`: an ordered handler list, not an event bus.** There is
no domain-event bus in this codebase — `Shared/Eventing` is entirely SSE machinery — so "becomes an
event" would have meant building one, and the requirement the verification line actually states is
dependency inversion, not messaging.

- [ ] Verify: the service imports no feature. Auth leaves the strongly connected component.

### Task C4: Move `MpCommandService` and `MpReplies` into Multiplayer

1,889 of `Bot`'s 2,643 lines are multiplayer command handling and multiplayer reply strings. `Bot`
keeps the dispatcher and the reply sink — the command transport — and calls Multiplayer through its
contract.

- [ ] Verify: `Bot -> Multiplayer` drops from nineteen types to one contract.

### Task C6: Give `Basil.Domain` its own graph rule, before C1 moves anything into it

**This runs before C1, and C1 is blocked on it.** Measured 2026-09-10, after C4.

Every instrument this migration has loses sight of the moved code at the moment C1 moves it:

| Instrument | What happens at C1 |
|---|---|
| `measure-slice-graph.py` features-only | collapses by construction — the files left `Features/` |
| `SliceAdjacency` + NetArchTest | stops covering them — its rule is scoped to `Features/<Slice>`, so `Basil.Domain.Multiplayer -> Basil.Domain.Users` is invisible to it |
| `measure-slice-graph.py` solution-wide | still spans them, and is the one instrument with a **proven** blind spot: Task C4 showed it cannot see an inferred type such as `sender.IrcConnection` |

So after C1 the only thing watching ninety-six moved files would be the weakest of the three. C1
moves per feature, one commit each; a rule that lands first checks each feature's move as it arrives,
and a rule that lands afterwards finds out at the end, with everything moved and no revertable unit.

Stage E2 already lists this rule — "the feature graph inside `Basil.Domain` is acyclic except for the
declared relationships". It is pulled forward because C5 cannot gate on an instrument that does not
exist yet.

- [ ] Add the rule over `Basil.Domain`'s internal namespaces, in `Basil.ArchitectureTests`, in the
  same declared-allowlist shape as `SliceAdjacency` so both read alike.
- [ ] **It starts with a pinned list of three, not empty.** Measured on the current tree, three
  cross-slice edges already have their owning file under `src/Basil.Domain`:
  `Multiplayer -> Beatmaps` and `Multiplayer -> Scores` (both `Multiplayer/Round.cs`), and
  `Scores -> Beatmaps` (`Scores/Submission.cs`, `HitCounts.cs`, `Mods.cs`). Each is a plausible
  domain relationship rather than an accident — a round has a beatmap and produces scores, a
  submission is against a beatmap — so each is declared with its reason, and that list is C5's honest
  Domain baseline.
- [ ] Verify the rule fails when it should: add an edge that is not declared and watch it break,
  before trusting a green run.
- [ ] Stage E2 then covers the remaining invariants rather than this one.

### Task C5: Measure the graph

The measurement is `plans/execution/measure-slice-graph.py`, run from the repository root. It
derives an edge from a using directive or a fully qualified reference crossing a `Features/<Slice>`
boundary, and reports edges, mutual pairs, strongly connected components, and which slices have no
outgoing edge left.

It reports two counts, and **both** have to fall. The `features-only` count reproduces the
assessment's definition and is the frame the prediction was made in; it drops when files merely
leave `Features/`, which Stage C does ninety-six times. The `solution-wide` count follows a slice
wherever its files live, so it falls only when a dependency actually goes away.

- [ ] Re-run it. The **Stage C entry baseline**, measured on `ef96b008` — the commit that closes
  Stage B and carries the merged Stage F — and recorded in
  `plans/execution/architecture-progress.md`, is **45 features-only / 53 solution-wide edges, one
  component of ten.** Eleven slices now, and the cycle still has ten: Diagnostics sits outside it
  with one declared outgoing edge and nothing depending on it.
- [ ] Expected after Stage C: features-only falls to roughly 17, with Auth, Beatmaps, Content,
  Users and Spectating standing free. **Solution-wide must fall with it.** If features-only reaches
  17 while solution-wide sits near 52, the coupling was relocated into `Basil.Domain` rather than
  removed, and Stage D's project split would be made against a graph that is still one component.
- [ ] **An edge counts as removed only when its `SliceAdjacency` row can be deleted and the
  architecture suite stays green.** The script is a search tool, not a gate: it reads source text, so
  it cannot see a dependency whose type name is never spelled — an inferred property type such as
  `sender.IrcConnection` typed `IIrcConnection`. Task C4 hit exactly that and the edge read as gone
  while the compiled dependency was still there. `plans/execution/architecture-progress.md` records
  why the allowlist is the stronger instrument and the script the weaker one.
- [ ] State C5's result in **allowlist rows removed**, which every build enforces, and record the
  script's two counts alongside. A script edge that vanishes while its row cannot be deleted is a
  misattribution, not progress.
- [ ] Record the actual numbers in `plans/execution/architecture-progress.md`. **If the graph did
  not move as predicted, stop and report before Stage D** — Stage D's project split assumes it did.

---

## Stage D — split the transports

Only safe once Stages B and C have taken the business logic out of the files being moved.

### Task D1: Split `Basil.Protocol` in two

Measured: zero cross-references between the halves, in both directions.

- [ ] `Basil.Protocol.Bancho` takes `Packets/`, `Multiplayer/`, `Binary/`, `BanchoMessage.cs`,
  `LoginFailureReason.cs`.
- [ ] `Basil.Protocol.Irc` takes `Irc/`.
- [ ] `Basil.Protocol.Tests` covers both; keep its 158 tests passing with **no `.cs` change** —
  namespace-only edits at most. The bancho packet layouts are a wire contract.
- [ ] Update `Basil.slnx` with the `/Sources/Protocol/` folder.

### Task D2: Create the three host projects

- [ ] `Basil.Hosts.Bancho` — the bancho protocol routes, the packet dispatcher, `PacketBuilders`,
  the osu-web routes, the 46 packet handlers, and the bancho outbox taken from `GameSession`.
- [ ] `Basil.Hosts.Irc` — the whole of today's `Features/Irc`: TCP listener, connection, session,
  authentication, query service, replies. It has always been a transport, not a feature.
- [ ] `Basil.Hosts.Api` — the `api.`, `assets.` and `a.` hosts, the envelope and OpenAPI machinery,
  pagination, the JSON options, and every feature's routes and views.
- [ ] Update `Basil.slnx` with the `/Sources/Hosts/` folder.
- [ ] Verify: no host project references another. This is enforced by the absent project reference,
  so the check is reading the three csproj files.

### Task D3: `AnnounceRoutes` — the API action that broadcasts to bancho clients

`AnnounceRoutes` calls `ServerPacketWriter` so an HTTP announce reaches connected osu! clients.
After D2 that is the API host needing something the bancho host owns, which is forbidden.

- [ ] `Basil.Domain` publishes the announcement as an event; `Basil.Hosts.Bancho` subscribes and
  encodes it. The API host never learns a bancho packet exists.
- [ ] `MatchRoutes` uses the `MatchState` enum in a view and `OpenApiExampleExtensions` documents
  `ReplayFrame`/`ScoreFrame` as API examples — the HTTP schema currently describes bancho wire
  structures. Both get a view type owned by the API host.
- [ ] Verify: the generated OpenAPI documents change only where a bancho wire type was replaced by
  an API view, and the change is deliberate in every case.

### Task D4: Rename `Basil.Server` to `Basil.Infrastructure`

What remains after C and D is persistence adapters, storage, media and external services. The name
states what the project may contain.

- [ ] Rename the project, the root namespace, `tests/Basil.Server.Tests`, and the `slnx` entries.
- [ ] Fold by mechanism inside: `Persistence/`, `Storage/`, `Media/`, `External/`.
- [ ] Watch for the NetArchTest false positive already seen once on this project: a const whose
  *value* matches the searched pattern is reported as a dependency.

---

## Stage E — declare what is left, and enforce it

### Task E1: Declare the three surviving relationships

Three mutual dependencies are genuine domain relationships, not accidents:

* **match ↔ chat channel** — a match owns a channel; the channel routes commands back
* **match ↔ score** — a match records scores; a score reports on its match
* **command dispatch ↔ match control** — resolved by C4, verify it is gone

- [ ] Give the first two an explicit direction: the reader takes a projection, not the live object.
- [ ] Record each with its reason. Anything not on this list is a defect.

### Task E2: Write the architecture tests for the new invariants

- [ ] `Basil.Domain` references no framework and no protocol.
- [ ] No host references another host.
- [ ] The feature graph inside `Basil.Domain` is acyclic except for the declared relationships.
- [ ] A transport adapter is under roughly 60 code lines, or is listed as an accepted exception with
  a reason. Today two files fail this and both are fixed by Stage B.

---

## Stage F — the Diagnostic API

Old Phase 5, eight tasks, unchanged in substance. `plans/diagnostic-metric-inventory-20260907.md` is
**input**: it is probe-verified against this runtime and must not be re-derived.

Re-scoped only in placement: the diagnostic endpoints belong to `Basil.Hosts.Api`, and the samplers
that read the runtime belong to `Basil.Infrastructure`.

Carry-forward constraints from the design review: the `MeterListener` is a singleton whose lifetime
is the process and whose memory must not grow; the JSONL flush strategy is chosen by measurement,
not assumption.

---

## Stage G — the load harness

Old Phase 6, four tasks. Data must persist continuously: a run that crashes at 13h42m keeps every
sample up to 13h42m. The harness consumes the Diagnostic API into one timeline.

The `Basil.LoadTests` duplicated `ReloginGuardWindowSeconds` constant is accepted debt from Phase 0
and is repaid here; the real fix is moving `<SelfContained>` out of the csproj to publish time.

`Basil.LoadTests` already exists — 65 files, `OutputType=Exe`, `IsTestProject=false`, no `[Fact]`.
`dotnet test` skips it correctly, so it is absent from the five-project oracle by design rather than
by oversight. Stage G extends it; it does not create it.

Stage G was blocked on Stage F for the diagnostic client. **F is merged as of `b932f4b4`, so G is
unblocked.**

### Task G5: the test that passes by winning a race

`BeatmapsetManagementEndpointTests.PutBeatmapset_Valid_ReplacesTheBeatmapsetsFilesAndReturns202`
passes because `BeatmapsetMigrationService`'s startup sweep usually finishes before the assertion, not
because anything makes it. Found on 2026-09-10 while fixing the inverse defect in
`BeatmapDifficultyEndpointTests`, and recorded in `docs/for-developers/testing.md`.

- [ ] Give the test a way to wait for the migration pass to complete, or assert against a signal the
  service publishes, rather than against the clock. A test whose outcome depends on winning a race
  passes for the wrong reason and eventually fails for the right one.
- [ ] It lands here rather than in Stage B because it is load-dependent, and Stage G is where load is
  generated on purpose.

---

## Stage H — close it out

### Task H1: Write the nine ADRs

Listed in `plans/architecture-target-20260908.md` §9. They record decisions already taken, so they
are written from what was built, not from what was planned.

### Task H2: Localization rules, and consolidate the agent instructions

Two documentation asks the user raised on 2026-09-09, deliberately deferred to this stage rather
than interleaved with the restructure. The source material is
`plans/localization-rules-input-20260909.md`, a 1,497-line rule set the user wrote covering
hierarchy, naming and humanised phrasing for **all** localization, not only bot commands.

- [ ] Turn it into an authoritative document for developers, and a companion for agents. The
  observed problem it responds to is that the Bot slice's locale sections are muddled, so the
  document has to be usable as a review checklist, not only as prose.
- [ ] **Split `CLAUDE.md` into `docs/for-agents/`.** The goal is one place an agent reads,
  whichever agent it is — Claude, Codex or anything else — instead of instructions scattered
  between a root file and the docs tree. `CLAUDE.md` keeps only what a harness must load
  automatically and points at the rest.
- [ ] Apply the `humanizer` skill to the resulting prose, which the user asked for by name.
- [ ] Register both in `docs/index.md`, which owns authoritative topic ownership.

### Task H3: Rewrite `docs/for-developers/architecture.md`

It currently carries an "out of date" banner and describes the five-project layout. Rewrite it once,
against the finished structure — which is why it was deferred rather than edited after each phase.

### Task H4: Full verification

Route table, metric names, OpenAPI documents, schema objects and the full suite against the Phase 0
baseline, with every difference explained.

- [ ] **Verify routes with the Roslyn endpoint map, not the grep baseline.** Measured 2026-09-10:
  `mcp__plugin_dotnet-claude-kit_cwm-roslyn-navigator__get_endpoint_map` reports 151 endpoints with
  their full patterns, constraints, file and line, where `plans/execution/baseline/routes.txt`
  holds 140 deduplicated literals. The two are not the same measurement, and the grep one is the
  weaker of them in three ways that matter for Stage D: it captures only the string written inside
  the `Map*` call, so for a route mapped inside a `MapGroup` it records the **suffix** rather than
  the full path and cannot see a changed group prefix; it deduplicates, so two hosts mapping the
  same suffix collapse to one entry; and it cannot resolve an interpolated pattern at all, which is
  how most of the diagnostic routes are written. Stage D moves routes into three host projects,
  which is precisely when group prefixes move.
- [ ] Keep the grep check as well, for continuity with the Phase 0 baseline, but state which of the
  two any claim rests on.

### Task H5: Review the whole diff for unrelated changes, then a final advisor review

---

## Old plan: what is alive, absorbed, dead

| Old task | Status here |
|---|---|
| Phase 0, tasks 0.1–0.14 | **Done.** Committed and verified. |
| 1.1 mutation invariant audit | **Done.** Its decision changed the design: no `Invalidate()`. |
| 1.2 `MatchMutationScope` | Alive → **B4** |
| 1.3 adopt the hub | Alive → **B5** |
| 1.4 `MatchSession` encapsulation | **Absorbed** by C1 (projection split) and C2 (`GameSession`). |
| 1.5, 1.6 decompositions | Alive → **B6**, plus audit Observations 1 and 5 |
| 1.7 `MatchSubResourceRoutes` | Alive → **B1**, and now required *before* Stage D |
| 1.8 localization, 1.9 logging | Alive, run per feature during Stage C |
| 1.10, 1.11 | **Absorbed** by C5 and E2 |
| Phase 2 (Chat/Bot/IRC) | **Re-scoped.** Irc becomes a host (D2); Bot loses 1,889 lines (C4). What remains is localization, logging and test triage, run during Stage C. |
| Phase 3 (Users/Auth) | Alive. The mute API, username validation and sensitive search filters are unaffected by the restructure; run during Stage C. |
| Phase 4 (Beatmaps/Scores/Content) | Alive, same treatment. |
| Phase 5 Diagnostics | Alive → **Stage F** |
| Phase 6 load harness | Alive → **Stage G** |
| Phase 7 final sweep | Alive → **Stage H**, plus the nine ADRs |

---

## Execution model

Unchanged from Phase 0, because it worked: **Opus orchestrates, Sonnet implements.** Opus analyses,
decides direction, and reviews what was actually built — not what was reported. A worker's claim is
checked against the repository; that is how the seven missing packet handlers were caught in Task
0.6 when the worker had reported the total as verified.

Tasks are batched per worker, not one worker per task.

**Checkpoints.** Every stage owns a file under `plans/execution/`. A worker updates it before
committing. A new session resumes from the checkpoint plus `git status` and `git log`, and
re-investigates nothing already recorded.

**Branch.** `feat/vsa-migration`. Check it before every push: implementation never lands on
`chore/perf-investigation`, which is PR #7.

**Continuation.** At the start of every cycle, write the checkpoint and immediately schedule a
+5h reminder — or earlier if a usage reset time is known, since firing just after a reset recovers
most of an hour.
