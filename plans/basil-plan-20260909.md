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
* **Never run tests in the background.** Foreground, and wait. Backgrounded `dotnet test` has
  stalled three workers on this project.
* **Commit the moment a task is green.** Session limits end workers mid-task; a committed task is
  never redone.
* Commit messages, code comments and documentation are written in normal English prose. Comments
  are self-contained: they explain the reason, never cite a document or ADR number as the reason.

**Test oracle.** The suite was 1648 passed / 0 failed / 0 skipped at commit `c5f06e9`. A task that
changes the count says why.

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

- [ ] **Step 1: Reproduce the blind spot**

`Features/Multiplayer/MatchRoutes.cs` uses `AdminKeyDefaults.Policy`, a `public const string` in
`Features/Auth`. `SliceAdjacency` does not declare `Multiplayer -> Auth`. Confirm the architecture
suite is green anyway.

- [ ] **Step 2: Try the cheap remedy**

Change `AdminKeyDefaults`'s three members and `BotBootstrapService.BotId` from `const` to
`static readonly`. Rebuild and re-run `Slices_Should_Only_Reference_Declared_Slices`.

Expected: it now fails, naming `Multiplayer -> Auth`, `Beatmaps -> Auth`, `Content -> Auth`,
`Auth -> Bot`, `Content -> Bot`, `Multiplayer -> Bot`, `Users -> Bot`.

- [ ] **Step 3: If it fails to fail, stop and report**

If the seven edges stay invisible, `static readonly` is not enough and the rule must be verified
from source with Roslyn instead. That is a different task and a different ADR; report it rather
than improvising.

- [ ] **Step 4: Record the result**

Write the outcome, with the exact test output, to `plans/execution/const-visibility-experiment.md`.
Commit.

### Task A2: Declare the edges the experiment revealed, then decide each one

- [ ] Add the seven now-visible edges to `SliceAdjacency` **with the justification each already
  has** (they exist; they were only invisible). The suite goes green.
- [ ] For each, record whether it survives the migration or is removed by a later task:
  `*/ -> Auth` via `AdminKeyDefaults` and `* -> Bot` via `BotBootstrapService.BotId` are both
  constants that move to `Basil.Domain` in Stage C, which removes the edge entirely.
- [ ] Commit.

### Task A3: Remove the dead and documentation-only imports

Thirteen imports reference a slice whose types the file never uses in code:

* dead: `Auth/AdminKeyService.cs`, `Auth/BCryptPasswordHasher.cs`, `Auth/GuidTokenGenerator.cs`
  (all `-> Users`), `Multiplayer/MatchSubResourceRoutes.cs` (`-> Beatmaps`),
  `Users/UserRoutes.cs` (`-> Multiplayer`, and separately `Basil.Protocol.Multiplayer`)
* documentation-only: `Beatmaps/CachingBeatmapsetRepository.cs`, `Bot/ICommandDispatcher.cs`,
  `Bot/ICommandReplySink.cs`, `Irc/IrcAuthenticationService.cs`,
  `Irc/BanchoIrcBridgeConnection.cs`, `Irc/IIrcConnection.cs`,
  `Multiplayer/MatchControlService.cs`

- [ ] Delete the dead ones outright.
- [ ] For the documentation-only ones the `<see cref>` needs the import to resolve. Either keep the
  import and leave a one-line comment saying the reference is documentation only, or change the
  `cref` to plain text. Prefer plain text where the link adds nothing.
- [ ] Build, run the full suite, commit.

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

### Task B3: Give the four database-touching services a contract

`Auth/LoginService`, `Beatmaps/BeatmapsetGarbageCollectorService`,
`Beatmaps/BeatmapsetMigrationService`, `Beatmaps/BeatmapWatcherService` open connections directly.

- [ ] For each, name what it actually needs as a method on an existing repository contract, or a
  new one if none fits. Do not invent a repository per service.
- [ ] Move the SQL into the adapter that owns that table.
- [ ] Verify: no file outside a repository or store matches `SqliteConnection|QueryAsync|ExecuteAsync|QueryFirstOrDefault|CommandDefinition`.

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
  object.
- [ ] Verify: the `Shared_Should_Not_Reference_Features` pinned list loses its
  `Shared.Sessions.*` entries. The test asserts exact set equality, so the list must be edited
  deliberately — that edit is the proof.

### Task C3: Logout and login become events

`PlayerLogoutService` imports five feature slices. It publishes instead, and Multiplayer,
Spectating, Chat, Irc and Bot subscribe.

- [ ] Verify: the service imports no feature. Auth leaves the strongly connected component.

### Task C4: Move `MpCommandService` and `MpReplies` into Multiplayer

1,889 of `Bot`'s 2,643 lines are multiplayer command handling and multiplayer reply strings. `Bot`
keeps the dispatcher and the reply sink — the command transport — and calls Multiplayer through its
contract.

- [ ] Verify: `Bot -> Multiplayer` drops from nineteen types to one contract.

### Task C5: Measure the graph

- [ ] Re-run the assessment's edge and component measurement. Expected: from 44 edges and one
  component of ten, down to roughly 17 edges with Auth, Beatmaps, Content, Users and Spectating
  standing free.
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

---

## Stage H — close it out

### Task H1: Write the nine ADRs

Listed in `plans/architecture-target-20260908.md` §9. They record decisions already taken, so they
are written from what was built, not from what was planned.

### Task H2: Rewrite `docs/for-developers/architecture.md`

It currently carries an "out of date" banner and describes the five-project layout. Rewrite it once,
against the finished structure — which is why it was deferred rather than edited after each phase.

### Task H3: Full verification

Route table, metric names, OpenAPI documents, schema objects and the full suite against the Phase 0
baseline, with every difference explained.

### Task H4: Review the whole diff for unrelated changes, then a final advisor review

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
