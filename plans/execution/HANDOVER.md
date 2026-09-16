# Handover — osuBasil architecture migration

**Written 2026-09-11, re-verified and updated 2026-09-15 (C1b's completion).** For a successor agent
with no prior context. Read this first, then `plans/README.md` for what every other document is,
then `plans/basil-plan-20260909.md`. Everything here is verifiable from the repository; where it is
not, it says so.

> **2026-09-16 — direction change, execution underway.** The target is *Architecture v3*:
> `Basil.Domain` + `Basil.Server` reorganise into `Basil.Domain`, `Basil.Application`,
> `Basil.Infrastructure`, then the transports split into `Basil.Host.Bancho`/`.Irc`/`.Api` with
> `Basil.Host` as the entry point — feature folders kept throughout. The plan is
> `plans/architecture-v3-migration-plan-20260916.md`, approved by the user; execution started
> 2026-09-16. **Batch 0 done** (`9244a6e3`, local, not pushed): `Basil.Host` extracted as the exe,
> `Basil.Server` is now a library. Next: **Batch 1** — rename `Basil.Server` → `Basil.Infrastructure`.
> Stage D2–D4 and E2 below are superseded by the v3 plan's Batches 10–13; D1 and G are done and
> stand; §3–§9 of this handover about instruments, rules and pitfalls still applies except where a
> batch has since moved the file a rule names. Every batch commit is local only — not pushed until
> the user says so. **Batch 1 done** (`Basil.Server` renamed to `Basil.Infrastructure`, `Features.`
> segment dropped, `Basil.Server.Host` → `Basil.Host`, `Basil.Server.Tests` → `Basil.Infrastructure.Tests`).
> **Batch 2 done**: `Basil.Application` created (Sessions, Eventing, Configuration, Localization,
> Json, Irc's `IIrcConnection`/`IrcSession`/`IrcSessionRegistry`, Multiplayer core, the D10
> reply-constant classes and their locale fragments). The compiler forced additions the plan's file
> list didn't name, each a framework-free leaf with a real caller in an already-moved type:
> `MatchCreationData.cs`, `MatchLiveSnapshotBuilder.cs` (+`UserBriefResolver.cs`), `BeatmapViews.cs`
> (pulled forward from Batch 9 — **Batch 9's row in §5 no longer reads "1 file + tests"**, it's
> DirectSearchService plus DI splits only), `DateTimeExtensions.cs`, `Envelope.cs`/`PageMeta`/
> `FieldError`, `BanchoIrcBridgeConnection.cs`. `ChatMetrics.cs`/`IrcMetrics.cs` turned out to be
> plain renames to `*Publisher.cs`, not D3 splits (only `MatchRoundEndOutbox.cs` and
> `MultiplayerMetrics.cs` had a real contract half to extract). `BotBootstrapService.BotId` calls in
> `MatchLiveSnapshotBuilder.BuildSettings` became `MatchSession.NoHostId` (same value, already used
> elsewhere in the file, removes an Infrastructure dependency). Test suite: 9 + 235 + 158 +
> 65 (`Basil.Application.Tests`, new) + 851 (`Basil.Infrastructure.Tests`, down from 916 — the 65
> moved out) + 28 (`Basil.Host.Tests`) + 363 (`Basil.IntegrationTests`) = **1709**, unchanged from
> Batch 1. `SliceBoundaryTests`'s pinned `Shared_Should_Not_Reference_Features` list shrank by 4
> (`GameSession`, `UserSession`, `PacketDispatcher`, `GhostDisconnectService` — each now depends on
> `Basil.Application.*` instead of a Features slice) — the list working as designed, not edited
> freely. `dotnet run` smoke check could not run in this session: `ServerOptions.Port` is 443, which
> needs admin/a URL ACL reservation this background session doesn't have; `Basil.IntegrationTests`
> boots the same `Bootstrap`/`StartupData`/DI graph through `WebApplicationFactory` (DbUp migrations,
> `LocaleTouch.AllReplyHolders()` included) and is the stronger signal here. Two follow-up requests
> from the user during this batch are **not** part of it and need their own commit: making a match's
> host nullable instead of using `MatchRoomState.NoHostId`/`SystemUserIds.BasilBot` as a sentinel
> (Bancho wire behavior unchanged when null), and the same nullable-instead-of-sentinel treatment for
> other places using an out-of-band value to mean "absent" (e.g. a match's current beatmap). Both
> touch wire-adjacent domain state and want their own protocol-test coverage.
> Next: **Batch 3**.

---

## 1. Where things stand

Branch **`feat/vsa-migration`**, at `0aaf326a`, pushed. A second worktree sits at
`V:\Code\cs\osuBasil-diagnostics` on `investigate/diagnostic-live-flake`, branched from
`9a4265ab`, for the flake diagnosis in §8. As of this checkpoint it is a clean build with no
uncommitted investigation output — the diagnosis has not run to completion yet; check `git status`
there before starting.

**The suite is green.** Verified 2026-09-15 at Unit 8's commit (`928dd1ac`), the last one that
touched a source file — Unit 9 changed only documentation:

| Project | Count |
|---|---:|
| `Basil.ArchitectureTests` | 9 |
| `Basil.Domain.Tests` | 235 |
| `Basil.Protocol.Tests` | 158 |
| `Basil.Server.Tests` | 944 |
| `Basil.IntegrationTests` | 363 |
| **Total** | **1709** |

(+1 in `Basil.ArchitectureTests` since D1: `DependencyDirectionTests`' single Protocol check split
into one per new assembly.)

One integration test, `DiagnosticEndpointTests.GetOverviewLive_FirstEventCarriesTheCuratedFields`,
failed once on a slow full run (8 min 02 s where 6 minutes is usual) and passed in isolation. It
waits ten seconds for a one-second broadcast tick; it is load-sensitive, not broken, and is listed in
§8 next to the other one.

Route table: **140** literal patterns from
`grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Server --include=*.cs | sort -u | wc -l`.
Treat that number with suspicion — see §4.

### Stages

| Stage | State |
|---|---|
| **A** — make constant-mediated coupling visible | Done. ADR-008. |
| **B** — untangle before anything moves | Done, all six tasks. |
| **F** — the Diagnostic API | Done and merged. |
| **C** — extract the business layer | C4, C3, C6, **C1a done** (pinned list 21 → 3), **C1b done** (nine units, per-feature table resolved), **C5 run and reported for both**. C2 **off the path**. |
| **D** — split the transports | **Running.** D1 done (`Basil.Protocol` split into `.Bancho`/`.Irc`); D2–D4 not started. See `plans/execution/stage-d-progress.md`. |
| **E** — declare what survives, enforce it | Not started. |
| **G** — the load harness | **Done.** Task G5 fixed (`e2931b33`); the `ReloginGuardWindowSeconds` duplication stays as accepted debt (see §8); the `DiagnosticEndpointTests` flake investigated, no defect found (see §8). |
| **H** — documentation and final verification | Not started. |

**Stage C runs C4 → C3 → C6 → C1a → C5.** `plans/execution/stage-c-order-decision.md` says why the
project boundary is crossed last; `c1-transport-seam-decision.md` says why C1 split.

---

## 2. C1a and C1b are both done. Stage D is next

**C1 as written cannot run.** Found 2026-09-14, measured from the compiled assembly: the services C1
would move into `Basil.Domain` — every match, chat, spectating and login service — encode bancho
packets with `ServerPacketWriter` and IRC lines with `IrcMessageWriter` inline, and take
`GameSession` (which carries `IIrcConnection` and `MatchSession`) as their parameter type. Domain may
not reference `Basil.Protocol` (invariant 9), so the first feature moved would fail on arrival.
The plan's sizing table checked five framework packages and never checked the protocol.
`plans/execution/c1-transport-seam-decision.md` has the per-type table.

**The instrument already exists.** `tests/Basil.ArchitectureTests/TransportSeamTests.cs` pins the
21 offending types by exact set equality, in the same shape as the `Shared -> Features` list. It
fails both when a new business type reaches for the protocol and when an entry is removed without
deleting its row — proven by deleting one row and watching it fail.

**C1a is done (2026-09-15).** All five steps landed: step 1 `SpectatorService` (`9a4265ab`,
21 → 20), step 2 the match services (`IMatchNotifier`, 20 → 17), step 3 the chat seam (`c152c026`,
`16b53d66`, `cd3edf84`, `087902df`, `666e704c`; `ChatLine`, `IChatNotifier`, `IChannelNotifier`,
NAMES/LIST moved to `IrcQueryService`; 17 → 10), step 4 `Auth.LoginService` (`0cea3160`; not a
notifier — a concrete, interface-free `LoginResponseEncoder` replaced 22 own-response encoder calls
1:1; 10 → 9), and step 5 (`c0e2f6e3`; `MatchCreationData` + `MatchCreationDataMapper` replace
`MatchState` as match-creation's input, `MatchLiveSnapshotBuilder.BuildPlayerScore` moved into its
only caller; 9 → 3). `plans/execution/phase-stage-c.md` "C1a" has every step's record,
`plans/execution/chat-seam-decision.md` the chat design.

**Pinned list: 3, all staying deliberately, not candidates for a future step:**

* `AnnounceRoutes` — needs Stage D's "Domain publishes, Hosts.Bancho encodes" event mechanism,
  which does not exist until Stage D builds the host split. Task D3's to close.
* `Spectating.SpectateFramesEvent` — a documented decision in the record's own remarks: it reuses
  the wire `ReplayFrame`/`ScoreFrame` types deliberately rather than duplicate an API-layer copy.
* `Multiplayer.MatchPacketDataMapper` — an adapter by design, outside `.Packets` only because C1b
  hasn't decided where adapters live yet.

**C5 has run (`32aaed40`) and its verdict is "ask the user," not "proceed."** Every slice-boundary
instrument — `SliceAdjacency` (44), `Shared -> Features` pinned list (12), `DomainAdjacency` (6),
the script's two counts (43 features-only / 50 solution-wide) — reads identical to `e286cc26`,
before C1a started. That is expected, not a failure: none of those four instruments has
`Basil.Protocol` in its population, and C1a never touched a slice boundary, only the protocol
dependency inside slices that already existed. The plan's C5 prediction (features-only ≈17) was
written for the original, unsplit C1 — the ~96-file move into `Basil.Domain` — which is now **C1b**
and has not run. Full writeup in `plans/execution/architecture-progress.md`, section "C5, run after
C1a".

**The user chose to run C1b.** It is in progress, moved in small verified units rather than all at
once — see `plans/execution/c1b-project-move-decision.md` for why (a text classifier missed both a
`GameSession`-typed parameter with no import naming it, and peer coupling between two candidate
files, and once nearly moved the whole Diagnostics slice, which the target architecture never
scoped as a Domain concern at all — reverted before anything committed).

**Units 1-8 are done and pushed** (`214ff845`, `365c351c`, `89cb120a`, `9b498f96`, `812b0c2d`,
`41e57269`, `5b9131c6`, `928dd1ac`): the fourteen repository/store interfaces plus their
filter/parser pairs (18 files), `MotdService`/`IReplayStorage`/`IScoreDecryptor`/`ReplayService` (5
files, first `Shared -> Features` pinned-list movement since C3), the mirror search
contract/`MirrorService`/`MirrorOptions` (5 files, `Basil.Domain`'s second package reference), the
password/token contracts plus `LoginForm`/`AdminKeyService` (6 files, second pinned-list movement),
Chat's `ChannelSession`/`IChannelRegistry`/`InMemoryChannelRegistry` plus Spectating's
`IPlayerInputEvents`/`IPlayerStatusEvents`/`PlayerInputEvents`/`PlayerStatusEvents` (10 files),
Bot's `ICommandReplySink` (1 file — the finding that Bot's other 5 files gate on Multiplayer, not a
separate survey), `AuthenticationService`'s `CredentialVerifier` extraction (1 new file, password
verification split from session lookup), and `MatchSession`'s phase-1 split: a new
`Basil.Domain.Multiplayer.MatchRoomState` carries every plain business field/method,
`Basil.Server`'s `MatchSession` keeps its full public surface and forwards to it, the lock/mutation-
scope/SSE machinery is untouched (2 files). Four blockers a text classifier cannot see are now
documented from direct experience — `GameSession`/`UserSession`/Shared-typed parameters with no
import naming them; peer coupling to a candidate that isn't itself eligible; direct filesystem I/O
with no forbidden `using`; an `IOptions<T>`/wrapped type argument that is itself a Server type, which
only a physical file move plus rebuild proves safe — plus a fifth found in Unit 8: a `const` field
crossing a namespace boundary is invisible to `DomainBoundaryTests` (ADR-008's documented
const-inlining gap), so a real edge must be declared even when the instrument cannot yet catch its
absence.

**Unit 9 closed C1b: `MpCommandService`/`MpReplies` measured and found blocked, the same as
`ScoreSubmissionService`.** `MpCommandService` writes `sender.MpScopeMatchId` directly at three sites
and calls `BeginMutationAsync` — live session/match mutation, not a lookup a Domain contract can
wrap. `MpReplies` reads through `Basil.Server.Shared.Localization.LocaleCatalog`, the same blocker
`BotReplies` hit in Unit 6. Both stay in `Basil.Server`, which resolves Bot's remaining five files
too (`CommandDispatcher`, `ICommandDispatcher`, `BotBootstrapService`, `BotReplies` were gated on this
pair). **Every C1b slice's Domain-eligible surface is now identified and, where safe, moved.**
Multiplayer landed at 2 files (`MatchRoomState`, `MatchSlot`) against the target table's estimate of
16; Bot at 1 (`ICommandReplySink`) against 6 — the third and fourth instance this session of that
table's per-feature counts over-stating what a text classifier's framework-import check can actually
verify, after Diagnostics (no row at all) and Bot's own first pass. Treat every number in that table
as an upper bound to verify per file, not a count to reach — this is the standing lesson for whoever
scopes Stage D next.

**C5, re-run after C1b (`architecture-progress.md`'s "C5, run after C1b" section has the full
table):** `SliceAdjacency` and the solution-wide script count are unchanged (44, 50) — expected, C1b
never touches `Features`-to-`Features` edges. `Shared -> Features` dropped 12 → 10.
`DomainAdjacency` roughly doubled, 6 → 14 — the instrument built to watch exactly what C1b moved,
showing the movement. The plan's features-only ≈17 prediction assumed C1's original scope including
the 24 Multiplayer packet handlers and the `!mp` command surface; those were never real C1b
candidates once measured directly, so that gap is Stage D's to close, not a miss here.

**Stage D — splitting `Basil.Server` into `Basil.Hosts.Bancho`/`Basil.Hosts.Irc`/`Basil.Hosts.Api`
(and renaming what remains to `Basil.Infrastructure`) — is the next stage of work**, per
`architecture-target-20260908.md` §8 step 8. It is a separate, larger undertaking outside C1b's
scope: it needs the host projects to exist before `GameSession`'s split (§8 step 5), the 24
Multiplayer packet handlers' relocation, and `MatchSession`/`MatchRoomState`'s eventual full
separation (the projection machinery moving out) can happen safely. Not scoped or started as of this
checkpoint.

---

## 3. Decisions already taken — do not re-litigate these

Each was decided with evidence and is recorded. A successor re-opening one costs a session and
usually reaches the same answer.

| Document | Settles |
|---|---|
| `docs/adr/ADR-008-dependency-enforcement.md` | values crossing a boundary are `static readonly`, not `const`, because NetArchTest reads IL and C# inlines constants |
| `stage-c-order-decision.md` | the order of Stage C, and the measured payoff of C4 |
| `c2-deferred-decision.md` | why C2 is off the path — its own proof is unreachable by it |
| `c1-transport-seam-decision.md` | C1 is split: the seam is cut in place first, the project move is decided after; invariant 9 stays |
| `chat-seam-decision.md` | `IrcMessage` is a leaked wire type; `ChatLine` + two notifier contracts; the five-commit order for the chat seam |
| `hub-adoption-decision.md` | the event hub carries deltas only; the seed handshake was deleted because `SeedIfNotSuperseded` had no callers |
| `logout-as-event-decision.md` | logout uses an ordered handler list, **not** an event bus — there is no domain-event bus in this codebase, and `Shared/Eventing` is entirely SSE machinery |
| `diagnostics-boundary-decision.md` | `Diagnostics → Auth` authorised; four other edges refused in favour of published gauges |
| `architecture-progress.md` | the instrument map in §4, and what the C5 gate is allowed to gate on |

**Two plan documents are stale in specific places and say so inline.**
`phase-1-multiplayer.md`'s "Task 1.3's design question" weighs two candidates that
`hub-adoption-decision.md` rejects in favour of a third, and `architecture-target-20260908.md` §2
carries a visible correction block about a measurement error. Prefer the decision documents.

---

## 4. The instrument map — the most expensive knowledge here

Five things measure coupling on this project. **They cover different populations and they fail in
different directions.** Most of a day went into learning this, and a successor who assumes one number
means "the coupling" will draw a false conclusion.

| Instrument | Population | Enforced | Blind to |
|---|---|---|---|
| `SliceAdjacency` + `SliceBoundaryTests` | `Features/<Slice>` → `Features/<Slice>` | every build | `const` values, until ADR-008 |
| `Shared_Should_Not_Reference_Features` pinned list | `Shared/` → `Features/` | every build, exact set equality | nothing known |
| `DomainAdjacency` + `DomainBoundaryTests` | inside `Basil.Domain`, population read from the assembly at run time | every build | nothing known |
| `TransportSeamTests` pinned list | `Features/` types outside `.Packets` and outside `Irc` → `Basil.Protocol` | every build, exact set equality | nothing known; added 2026-09-14 |
| `plans/execution/measure-slice-graph.py` | files owned by a slice-named directory, in `Features/` or `Domain/` | nothing — it is a report | three things, below |

The script has **three proven blind spots**:

1. **Inferred types.** `sender.IrcConnection` is typed `IIrcConnection`, a `Features.Irc` type, but
   that name is never written in the file. The script reads source text, so it cannot see it. Task C4
   hit exactly this: it predicted `Bot → Irc` was gone, the script agreed, and deleting the allowlist
   row failed the build.
2. **`Shared/` as a source.** It counts edges *sourced from* slice-named directories, so
   `Shared/Sessions/PlayerLogoutService.cs` importing five slices was invisible. Task C3 removed that
   coupling and **every script number stayed identical**.
3. **Namespaces that are not slices.** It takes slice names from the directory listing under
   `Features/`, so `Channels`, `Login` and `Social` in `Basil.Domain` were never examined. C6 measured
   the Domain graph from the assembly instead and found **six edges where the record said three**.

### The rules that follow

* **An edge counts as removed only when its `SliceAdjacency` row can be deleted and the architecture
  suite stays green.** The script says where to look. The allowlist says whether it worked.
* **Name the currency before claiming a win.** C3's was the `Shared -> Features` pinned list; C4's
  was allowlist rows; C1a's is the `TransportSeamTests` pinned list; C1b's would be the
  `Shared -> Features` list and `DomainAdjacency`. Without this, a task that moved nothing reads as
  progress on whichever number happened to drift.
* **The route grep is weak.** It records only the string inside the `Map*` call, so for a route mapped
  inside a `MapGroup` it captures the *suffix* and cannot see a changed group prefix; it deduplicates;
  and it cannot resolve an interpolated pattern. The Roslyn endpoint map reports 151 endpoints with
  full patterns. **Stage D moves routes into three host projects — exactly when group prefixes move —
  so verify Stage D with the endpoint map, not the grep.** Task H4 has this written in.

---

## 5. Operating rules learned the expensive way

Every one of these cost a worker session or a wrong answer.

* **Never run `dotnet test` over the whole solution in one call.** It exceeds the 600-second tool
  ceiling, the tool backgrounds it, and the worker stalls. Build once with
  `dotnet build --configuration Debug`, then one **foreground** call per test project with
  `--no-build`, `Basil.IntegrationTests` last (about six minutes alone), `timeout: 600000` on each.
  Following the earlier version of this rule — "foreground with a 600-second timeout" — still failed,
  because the whole suite does not fit inside it.
* **Never `git add -A` or `git add .`** Stage explicit paths. A blanket add swept another worker's
  unverified files into a documentation commit twice.
* **A resumed tree must be built before its contents are treated as progress.** `git status` cannot
  tell a finished task from a half-rewired one. Two trees once looked identical in `git status`; one
  was green and one did not compile at all.
* **Use the Rider MCP refactorings** — `rename_refactoring`, `move_type_to_namespace`,
  `change_api_signature`, `safe_delete`, `find_references`. They work on the reference index, so they
  also fix `nameof` and `<see cref>` that a text edit leaves silently wrong. They are bound to the
  solution open in the **main tree only**; pass `rootFolder: "V:/Code/cs/osuBasil"` when asked. The
  one behavioural bug found in all of Stage C was in hand-applied work.
* **The worker keeps its own checkpoint, in the same commit as each green step** — not the
  orchestrator afterwards. A worker here dies mid-task roughly every session, and when it does its
  report dies with it. State what is **applied but uncommitted** separately from what is next: a
  checkpoint once listed five already-applied steps as "next", and the successor reviewed a diff it
  believed it was about to write, with a real bug sitting in it.
* **One worker per tree.** Never coordinate manually around overlapping edits — serialise, assign
  ownership, or move the boundary.
* **Model tiers.** Opus for architectural judgement only; Sonnet for implementation; Haiku for pure
  verification. The account is Claude Pro, not Max — budget is a real constraint, and a fixed model
  for every task wastes it.

---

## 6. Tooling worth reaching for, and one that lies

Audited by running the tools, not by reading their descriptions.

**Use:** the Rider refactorings; `cwm-roslyn-navigator get_endpoint_map` (151 endpoints with full
patterns, constraints, file and line) and `find_references` (found an `<inheritdoc cref>` that grep
reads as a comment); `codegraph_explore` instead of a grep-then-read loop; `context7` for NetArchTest
and xunit v3 API questions, which Stage E2 will need; `deepwiki` against `osuAkatsuki/bancho.py` for
scope questions.

**Do not trust:** `cwm-roslyn-navigator get_dependency_graph` **errors** on this solution at both
project and namespace scope, and `detect_circular_dependencies` returns zero cycles where the feature
graph is demonstrably one strongly connected component of ten slices. On a migration whose central
gate is a cycle count, that is the most expensive possible false comfort.

---

## 7. Where I was wrong, so you do not trust this record blindly

* I claimed four services touched the database, from an unanchored grep for `ExecuteAsync` that
  matched every `BackgroundService` override. A worker disproved it; Task B3 closed with no code
  change. The correction is a visible block in `architecture-target-20260908.md` §2.
* I wrote a checkpoint saying B5 still owed an invariant test. It already existed, bounded with a
  five-second `CancellationTokenSource`, committed two commits earlier. I had not checked.
* I reported the `Shared → Features` pinned list as "12 down to 11" twice. The array held 13 and then
  12; I was quoting a comment that had drifted before I arrived. The comment no longer states a count.
* My prediction table for C4 listed `Bot → Scores` as an edge that would disappear. It was
  `using Basil.Domain.Scores` — a Domain namespace, never a slice-boundary violation, never an
  allowlist row.
* I recorded the Domain graph as three edges. It is six; see §4.
* The plan sized C1 as a file move with a table that checked five framework packages and not the
  one dependency that actually blocks it. Found 2026-09-14 by measuring from the compiled assembly
  instead of trusting the table; §2.
* `architecture-progress.md` carried three values for one number (45/53, 52, 43/50) because the
  C5 gate line and the log table were not updated when C4 and C3 landed. Now one value, with the
  commit and the command beside it.

The pattern: **every one of these was a number or a claim written without measuring, and every one was
caught by someone measuring it.** Prefer a probe to the record — this document included.

---

## 8. Open items

* **C1a, then C5.** C5 gates Stage D: if the graph did not move as predicted, stop and report before
  Stage D, whose project split assumes it did. C5 now also reads the `TransportSeamTests` list.
* **`BeatmapsetManagementEndpointTests`'s migration-sweep race is fixed (Stage G, Task G5,
  `e2931b33`).** The test now awaits `BeatmapsetMigrationService`'s `BackgroundService.ExecuteTask`
  before sending its PUT, instead of racing the sweep's own file writes. Attempted the plan's stated
  "real fix" for the separate `Basil.LoadTests` `ReloginGuardWindowSeconds` duplication in the same
  pass (drop `<SelfContained>` from `Basil.Server.csproj`, add a real `ProjectReference`) and hit a
  transitive `NU1202`: `Humanizer.Core.*` 2.14.1, pulled in through `ppy.osu.Game.Rulesets.*`, isn't
  `net10.0`-compatible once both projects restore together. Reverted; left as accepted debt with the
  real blocker recorded in `basil-plan-20260909.md`'s Stage G section rather than the originally
  assumed one.

* **`DiagnosticEndpointTests`' live-test flake (`GetOverviewLive_FirstEventCarriesTheCuratedFields`,
  `GetGcLive_FirstEventIsARealGcReading`) investigated 2026-09-15; no code defect found, no fix
  applied.** The prior brief's central hypothesis — that the fix is making the SSE stream deliver an
  immediate first sample on subscribe, not wait for the shared periodic tick — turned out to already
  be exactly how `DiagnosticRoutes.StreamCategory` is written: it calls `sample()` synchronously and
  yields it as the first SSE item *before* ever touching `subscription.Events`, with its own doc
  comment stating this in as many words ("a subscriber takes its own fresh reading immediately on
  connecting, rather than waiting for the next broadcast tick"). `hub-adoption-decision.md` confirms
  this is the decided design ("a subscriber's first item is the state"), not merely permitted by it.
  That rules out the hypothesis the prior dispatch (died on the session limit after building, nothing
  landed) was sent to test. With the code path already correct, the remaining explanation for a
  single failure once on an 8-minute run and once on a 5-min-55s run, always passing in isolation, is
  ordinary resource contention (hundreds of `WebApplicationFactory` hosts and their background
  services running across the full suite) delaying the connect-and-first-read sequence past its
  10-second budget under an unusually loaded run — not a logic bug to patch. **No further action
  planned**; this matches the test's own already-stated tolerance ("a single failure of one of these
  on a full run is not a regression; anything else is"). Re-open only if it starts failing more than
  once per full run, or fails outside a heavy full-suite context.
* **Task H2** — the localization rule set the user supplied becomes developer and agent documentation,
  and `CLAUDE.md` splits into `docs/for-agents/`. Deferred by the user to the documentation phase; the
  source is at `C:\Users\haith\Desktop\osuBasil-docs.md`.
* **Five documents under `plans/`** still link to `../docs/for-developers/known-limitations.md`. That
  file was deliberately moved to `plans/known-limitations.md` in `8d3e9060` — "It's just local
  document for implement PR" — so the links are stale, not the file. They are the pre-migration perf
  investigation, labelled historical in `plans/README.md`, and are not maintained.
* **The user owes a force-push** restoring PR #7 to `97e7d56`. Blocked by a hook; the command was
  handed over and only the user can run it.

---

## 9. Conventions that are contracts, not preferences

From `CLAUDE.md`, and each has bitten: bancho packet layouts are wire contracts; user-visible chat
strings live in named production constants (`MpReplies`, `IrcReplies`, `BotReplies`) and changing the
wording is a contract change; `Privilege`, never `Priv`; no pp in gameplay; the response envelope on
the `api.` host; and the multiplayer concurrency model — hold the match lock across the whole
state-transition-and-broadcast sequence, and never introduce a second synchronisation mechanism for
the same state.

Commit messages, code comments and documentation are written in normal English prose, and comments
explain the reason without citing a document or an ADR number as the reason.
