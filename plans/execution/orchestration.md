# Orchestration state

Last updated: 2026-09-08T04:15:00Z (local 2026-09-08 11:15 UTC+7)

## How to resume

**Check the current branch against the Branch layout table below before any push.** Implementation
lands on `feat/vsa-migration`, not on `chore/perf-investigation` -- a push made without checking put
the whole Phase 0 migration onto PR #7's branch. It was a fast-forward and nothing was lost, but the
remote branch now carries work that does not belong to it.

Read this file, then `git status`, `git log --oneline -20`, and the checkpoint of whichever
phase is not `Done`. Continue from that phase's **Next exact step**. Phase 0 is `Done`; Phase 1
has not started, and starting it is a deliberate act — read the **Pause point** section first. Do not re-investigate
anything already recorded here or in `plans/vsa-migration-design-20260907.md`,
`plans/diagnostic-metric-inventory-20260907.md`, or `plans/execution/baseline/`.

Authoritative documents:

* `plans/basil-plan-20260909.md` — **the plan being executed.** Supersedes the 2026-09-07 plan; its
  §10 records which of that plan's 57 tasks are alive, absorbed or dead
* `plans/architecture-assessment-20260908.md` — the measurements every architecture decision rests on
* `plans/architecture-target-20260908.md` — the target structure and its invariants
* `plans/execution/architecture-progress.md` — the edge and component numbers, re-measured per stage
* `plans/execution/const-visibility-experiment.md` — why `static readonly` was chosen over Roslyn
* `plans/vsa-migration-plan-20260907.md` — superseded; alive tasks are still executed from its text
* `plans/vsa-migration-design-20260907.md` — the approved design (revision 3)
* `plans/diagnostic-metric-inventory-20260907.md` — probe-verified metric inventory, input to
  Phase 5; do **not** re-derive it
* `plans/execution/file-move-map.md` — the per-directory destination map for Task 0.3, derived from
  the tree at `d5d1b32`; do **not** re-derive it

## One worker at a time in this working tree

Two workers were run concurrently on 2026-09-09 with disjoint file ownership. The file split held —
neither touched the other's files — but the working tree itself does not support it:

* Concurrent `dotnet build` / `dotnet test` runs hold locks on shared `bin/` output, producing
  MSB3027 and MSB3021 copy failures that abort a run outright. One worker could not complete a full
  suite run at all.
* Concurrent git operations race on `index.lock`.
* Worst: a worker trying to isolate its own verification ran `git stash`, which swept up the other
  worker's uncommitted in-progress files. Nothing was lost — it checked `git diff stash@{0}` and
  restored before dropping the entry — but that is one careless step away from destroying an hour
  of another worker's work.

## Never `git add -A` while a worker owns files in this tree

This has now happened twice, both times to the orchestrator, both times immediately after it had
written a warning about the same hazard.

* `3140b4a7`, a documentation commit, swallowed the half-written `MatchMutationScope`.
* `ba3de03a`, another documentation commit, swallowed the eight files a worker left behind when it
  died on a session limit — which turned out to be the *finished* service conversion.

Nothing was lost either time, but the message describes documentation while the diff contains
source, and the code went in **unverified**: a documentation commit runs no build. The second batch
was only confirmed green afterwards, by luck rather than by process.

**Stage explicit paths.** `git add plans/ docs/` for a documentation commit, `git add <file> <file>`
for a code one. `git add -A` is safe only when `git status` holds nothing you did not write, and the
cheapest way to be sure of that is not to reach for it.

**So: one worker per tree.** Real parallelism needs `git worktree`, and as of 2026-09-09 the one the
design always assumed exists:

| Tree | Branch | Track |
| --- | --- | --- |
| `V:\Code\cs\osuBasil` | `feat/vsa-migration` | stages A-E, H |
| `V:\Code\cs\osuBasil-diagnostics` | `feat/vsa-phase-5-diagnostics` | stage F, the Diagnostic API |

Each worktree has its own `bin/` and `obj/`, so concurrent builds and test runs no longer fight over
file locks, and each has its own index.

**Stage B cannot be parallelised, and that is a symptom rather than an obstacle.** B1/B2, B4, B5 and
B6 all edit the same five files -- `MatchSubResourceRoutes`, `MatchRoutes`, `MatchControlService`,
`MatchMembershipService`, `MpCommandService`. They collide because those files are god files, which
is what B1, B2 and B6 exist to fix. Parallelism inside multiplayer becomes possible after the
decomposition, not before it, so the stage runs one worker deep on purpose.

## Execution model

Opus orchestrates: holds the architecture, tracks the dependency graph, decides ordering and
parallelism, gates file ownership, reviews the repository state rather than trusting a report,
and decides direction when reality contradicts the plan.

Sonnet implements: code, refactors, migrations, tests, APIs, localization, logging, docs,
verification. Workers are spawned per stream or per tightly-coupled task group — **not** one per
task. 47 tasks does not mean 47 workers.

When a worker hits an ambiguity it escalates with evidence (the problem, what the code/test/runtime
actually shows, what the plan expected, where reality differs, options if known, the relevant
diff). Opus decides direction; the worker implements it.

### Delegation tiers

The Agent tool in this build exposes `model` but no per-call reasoning effort — effort comes from an
agent definition's frontmatter, and this repository has no `.claude/agents/`. What is actually
controllable is the model tier and how much analysis the prompt demands, so those are tuned to the
task shape rather than left at one default.

| Task shape | Model | Prompt discipline |
| --- | --- | --- |
| Pure verification: run a build, a suite, a diff, and report numbers | Haiku | exact commands, exact output format, no latitude |
| Mechanical high-volume: file moves, namespace sweeps, locale key relocation, doc prose | Sonnet | prescriptive; every known trap named up front; no design latitude |
| Implementation with design content: the event hub, the mutation scope, the mute API, the diagnostic collectors | Sonnet | test-first steps written out; an explicit escalate-rather-than-guess list |
| Architectural reasoning, ambiguity resolution, reviewing a diff for drift | Opus (the orchestrator, or the advisor) | — |

Verification is the tier most often over-served: running a suite and reporting six numbers does not
need a frontier model, and Phase 0 alone has a dozen such checkpoints.

**This table was written and then ignored for most of stages A and B**, which is worth recording
because the failure was not subtle. Every dispatch went to Sonnet regardless of shape, and the
orchestrator did roughly two hours of the mechanical lock-site conversion itself — the most
expensive tier doing the cheapest work, on a Claude Pro plan the user had explicitly said to spend
carefully. The reason it drifted is that a worker failing looks like evidence the tier is too low,
when the actual cause was a prompt that did not say how to run a five-minute test command. Diagnose
the prompt before demoting the task.

## Branch layout

| Branch | Purpose |
| --- | --- |
| `chore/perf-investigation` | PR #7, open. Carries the investigation plus the four design/plan documents that came out of it. |
| `feat/vsa-migration` | Cut from `chore/perf-investigation` at `d5d1b32`. All migration implementation lands here. |

Phase 5 runs in a worktree at `../osuBasil-diagnostics` on `feat/vsa-phase-5-diagnostics`,
branched from `feat/vsa-migration` once Task 0.10 has landed.

## Phase status

| Phase | Status | Blocked on | Owner |
| --- | --- | --- | --- |
| 0 Foundation | Done | — | closed by Task 0.14 on 2026-09-08 |
| Architecture assessment | Done | — | measured, target agreed, plan rewritten 2026-09-09 |
| Stage A enforcement | Done | — | `ed48716`; the const blind spot is closed |
| Stage B untangle | Implementing | nothing | B4 and B3/A3 with workers |
| Stage C business layer | Not started | Stage B | — |
| Stage D transports | Not started | Stage C, gated on C5 | — |
| Stage E declare | Not started | Stage D | — |
| Stage F diagnostics | Not started | Stage D | — |
| Stage G load harness | Not started | Stage F | — |
| Stage H close out | Not started | everything | — |
| 1 Multiplayer | Not started | Phase 0 | — |
| 2 Chat/Bot/IRC | Not started | Phase 1 + Task 1.11 gate | — |
| 3 Users/Auth | Not started | Phase 1 + Task 1.11 gate | — |
| 4 Beatmaps/Scores/Content | Not started | Phase 1 + Task 1.11 gate | — |
| 5 Diagnostics | Not started | Task 0.10 only | — |
| 6 Load harness | Not started | Phase 5 | — |
| 7 Final sweep | Not started | Phases 1–6 | — |

## Unsatisfied dependency edges

* Phase 0 → everything
* Task 0.10 → Phase 5
* Phase 1 → Phases 2, 3, 4 (patterns)
* Task 1.11 → Phases 2, 3, 4 running in parallel (file-disjointness gate)
* Phase 5 → Phase 6

## Running workers

| Started | Scope | Status |
| --- | --- | --- |
| 2026-09-07 22:44 UTC+7 | Tasks 0.1 + 0.2 | killed by the session limit partway through 0.2; 0.1 committed at `91d151f`, 0.2's moves left on disk uncommitted |
| 2026-09-08 01:20 UTC+7 | finish Task 0.2, then Task 0.3 | both committed (`b4cf563`, `977b561`); killed by the session limit afterwards, tree clean |
| 2026-09-08 06:20 UTC+7 | Tasks 0.4, 0.5, 0.6 | all three landed (`1142bc1`, `1bee08d`, `a3e7b99`); the worker stalled three times waiting on backgrounded test runs, so the orchestrator verified and committed 0.6 itself |
| 2026-09-08 08:05 UTC+7 | Tasks 0.7, 0.8, 0.9 | killed by the session limit before touching a file; nothing to clean up |
| 2026-09-08 11:11 UTC+7 | Tasks 0.7, then 0.9, then 0.8 | all three committed |
| — | Tasks 0.10-0.13 | committed |
| — | Task 0.14 | run by the orchestrator; Phase 0 closed |

No workers are running. Phase 0 is done.

## Evidence captured so far

Baseline (`plans/execution/baseline/`, committed at `91d151f`):

* **1622 tests, 1622 passed, 0 failed, 0 skipped** — the oracle every later task diffs against
* six OpenAPI documents, one per host group, under `baseline/openapi/`
* `schema.txt`, `routes.txt`, `metrics.txt`, `locale-keys.txt`, `build.txt`, `base-sha.txt`
* `tests/Basil.LoadTests` is `IsTestProject=false` and is correctly outside that total

Task 0.2 verified on disk before delegating: all 46 `.cs` files present under `src/Basil.Server/`,
`src/Basil.Web/` reduced to stale `obj/` output, and only two `Basil.Web` mentions left anywhere —
both prose in csproj comments.

## Orchestrator decisions

**`Users -> Spectating` is an approved adjacency edge.** It appeared during the merge and was not in
the design's draft list. Verified one-directional, from `Features/Users/Packets/ChangeActionHandler.cs`
and `Features/Users/UserRoutes.cs`. The per-player live stream is addressed under the user resource
(`/users/{id}/live`) and spectating consumes user presence changes, so the direction is the natural
one. Task 0.4 records it in the allowlist with that justification.

**The load harness's duplicated constant is accepted debt until Phase 6.** Task 0.3 could not keep
`tests/Basil.LoadTests` referencing the server project: `Basil.Server` carries
`<SelfContained>true</SelfContained>`, and the SDK refuses a non-self-contained project reference to
a self-contained one. `LoginService.ReloginGuardWindowSeconds` is therefore duplicated into
`ScenarioSettings.cs` with a comment.

The right fix is to drop `<SelfContained>` from the csproj and pass it at publish time instead —
self-containment is a publish concern, not a compile-time one. That touches the CI and release
workflows, and Phase 6 rewrites the harness anyway, so doing it now would mean touching the same
area twice. Deferred to Phase 6, where it belongs.

## Test oracle

| Point | Total | Why it moved |
| --- | --- | --- |
| Baseline (`91d151f`) | 1622 | — |
| After Task 0.3 (`977b561`) | 1618 | four `DependencyDirectionTests` removed: they took the deleted `Application`/`Infrastructure` `AssemblyMarker` types by `typeof` and could not compile. Task 0.4 restores the coverage with slice rules. |
| After Task 0.4 (`1142bc1`) | 1621 | three new slice-boundary tests |
| After Task 0.6 (`a3e7b99`) | 1621 | unchanged, as a seam refactor should be |

**Known flake, already investigated — do not re-open it.**
`BeatmapDifficultyEndpointTests.GetDifficulty_PrivateBeatmapsetWithoutAdminKey_ReturnsNotFound` and
`BeatmapsetManagementEndpointTests.PutBeatmapset_Valid_ReplacesTheBeatmapsetsFilesAndReturns202`
intermittently fail with `IOException: The process cannot access the file '<x>.osu'` inside
`RemoveDirectoryRecursive` during `Dispose()`. A Windows file-handle race, present before the
migration; `git diff 91d151f` on both files shows only `using` lines changed. A 1617/1 on either is
the flake; anything else is real.

## Hazards from the Task 0.3 merge — all three cleared

1. **SQL migrations as `EmbeddedResource`** — cleared. All five `.sql` resources resolve under the
   new prefix `Basil.Server.Shared.Persistence.Migrations.*`, confirmed via
   `Assembly.GetManifestResourceNames()`, and a freshly migrated `sqlite_master` diffs clean against
   `baseline/schema.txt` apart from DbUp's own `SchemaVersions` journal table.
2. **`AssemblyMarker` deletion** — cleared. Exactly four tests removed, named in the test-oracle
   table above; the five string-based purity tests kept.
3. **Localization content** — cleared, and it was worse than expected: the JSON has to reach the
   output of `Basil.IntegrationTests`, `Basil.Application.Tests` and `Basil.Infrastructure.Tests`
   too, not only `Basil.Server`. Done with `<Content Update>` plus `Link`; `<Content Include>`
   collides with the SDK's auto-globbing and fails as `NETSDK1022`.

## Open architecture questions — answered in the Phase 0 review

Both were checked against the code rather than the design, and both mechanisms hold. Neither slice
boundary moves as a result, so Phase 1 inherits the structure as built.

**1. The adjacency allowlist has 38 edges.** The concern was that a list permitting 42% of all
possible slice pairs describes no boundary at all. Sampling the edges says otherwise: every entry
carries a one-line justification naming the specific type that needs it, and the entries are not
uniform — `Bot` and `Auth` account for six outbound edges each, which is what an orchestration-shaped
slice looks like, while most slices have one or two. The list is a description of real coupling, not
a rubber stamp. It stays as written, and the edge count is a metric to watch across Phases 1-4
rather than a defect to fix now.

**2. `Shared_Should_Not_Reference_Features` pins twelve existing violations.** The worry was that a
pinned exception list only blocks the thirteenth violation. It does more than that: the test asserts
the offender set for *exact* equality, so adding an edge fails and removing one also fails until the
entry is deleted on purpose. That is a ratchet in both directions, not a floor. Two entries
(`ApiHostRoutes`, `AssetsHostRoutes`) already dropped out during Task 0.6, which is the mechanism
working. The rule is honest about what it guarantees; the largest remaining cluster
(`Shared.Sessions.*` reaching into `Multiplayer`) is Task 1.4's to unwind.

## Regression caught during Task 0.6 review

`AddChat` registered the Chat slice's services but none of its seven packet handlers, so the
composition root resolved 39 of 46. `ChannelJoinHandler`, `ChannelPartHandler`, `LobbyJoinHandler`,
`LobbyPartHandler`, `SendPublicMessageHandler`, `SendPrivateMessageHandler` and
`ToggleBlockNonFriendDmsHandler` would all have been dropped by `PacketDispatcher` as unknown packet
types — logged at debug, skipped, with the entire chat feature dead and every other test still
green. Caught by `CompositionRootTests.ResolvesBanchoPacketDispatcherWithAllHandlers`.

The worker had reported the handler total as verified and unchanged. It was not. This is the
standing reason the orchestrator reviews repository state rather than accepting a report.

## Advisor checkpoints

| After | Done | Notes |
| --- | --- | --- |
| Phase 0 | yes | 2026-09-08 — both open questions answered below; four defects found and fixed in 0.14 |
| Phase 1 | no | hub ignorance of business logic; mutation scope footgun; ordering contract enforced by test |
| Phases 2–4 | no | boundary drift; new coupling; allowlist growth |
| Phase 7 | no | final review against the actual diff |

## Continuation cycle

A +5h continuation reminder is scheduled at the start of every working cycle, immediately after
the checkpoint is written — not after five hours have elapsed. When it fires and the work is
unfinished, resume from repository state and schedule the next one.

| Scheduled at | Fires at | Status |
| --- | --- | --- |
| 2026-09-07 22:42 UTC+7 | 2026-09-08 03:42 UTC+7 | cancelled — superseded |
| 2026-09-07 22:55 UTC+7 | 2026-09-08 00:13 UTC+7 | cancelled — reset time corrected |
| 2026-09-07 23:00 UTC+7 | 2026-09-08 01:13 UTC+7 | fired — cycle resumed |
| 2026-09-08 01:20 UTC+7 | 2026-09-08 06:17 UTC+7 | fired — cycle resumed |
| 2026-09-08 06:20 UTC+7 | 2026-09-08 11:21 UTC+7 | cancelled — superseded once the pause point moved |
| 2026-09-08 11:15 UTC+7 | 2026-09-08 16:14 UTC+7 | cancelled — Phase 0 finished and work was paused |
| 2026-09-08 15:23 UTC+7 | 2026-09-08 20:23 UTC+7 | cancelled — superseded once the reset time was known |
| 2026-09-08 15:31 UTC+7 | 2026-09-08 19:24 UTC+7 | fired |
| 2026-09-09 01:20 UTC+7 | 2026-09-09 05:24 UTC+7 | lost — the Claude process exited at 02:22 and took the job with it; nothing resumed until 06:45 |
| 2026-09-09 06:47 UTC+7 | 2026-09-09 11:47 UTC+7 | pending — the reset is 11:40, so this fires seven minutes into the fresh window |

The usage limit resets at 01:10 UTC+7, so the first continuation fires just after that rather
than a flat five hours out. Subsequent cycles go back to +5h unless a reset time is known.

Phase 1 is in flight, and a reminder is scheduled. When a reset time is known it beats a flat +5h:
firing at 19:24 against a 19:20 reset recovers most of an hour that a 20:23 wake-up would have idled
away. When no reset time is known, fall back to +5h.

Cron jobs are session-only, and on 2026-09-09 that cost four and a half hours: the process exited at
02:22 with a reminder pending for 05:24, the reminder died with it, and nothing resumed until the
user returned at 06:45. Scheduling a fresh reminder is therefore the first action of any new
session, before reading anything else -- a checkpoint nobody wakes up to read is worth nothing.
Surviving a process restart needs something outside the process, which cron here is not.

## Task 0.14 — baseline verification

Every artifact captured in Task 0.1 was re-derived from the migrated tree and compared. Measured,
not asserted:

| Artifact | Result |
| --- | --- |
| Full test suite | 1634 passed, 0 failed, 0 skipped (Protocol 158, Domain 114, Architecture 6, Server 1001, Integration 355) |
| Route table | identical to the baseline |
| Metric names | identical to the baseline |
| `Basil.Protocol.Tests` | zero `.cs` changes against the baseline; only the csproj xunit reference moved to xunit.v3 — the wire contract is untouched |
| Database schema | 46 objects in the baseline, 46 now, names matching; all six migrations apply cleanly to a scratch database |
| `Users` table | `SafeName` is a stored generated column, `SilenceEnd` is nullable |
| `src/` projects | exactly `Basil.Domain`, `Basil.Protocol`, `Basil.Server` |
| `Shared` segments | nine, all from the allowlist; no tenth appeared |
| OpenAPI (6 documents) | see below |

The architecture project reports 6 rather than the baseline's 8 because Task 0.14 deleted two
vacuous tests, not because coverage was lost — see the defects below.

### OpenAPI

`assets`, `avatar`, `beatmapassets` and `osuweb` are byte-identical once CRLF is normalized. The
other two differ, in each case by exactly one intended change and nothing else:

* `basilapi` — `silenceEnd` moves from the example value `1970-01-01T00:00:00+00:00` to `null`, and
  its schema from `"type": "string"` to `"type": ["null", "string"]`. That is Task 0.13's contract
  change, and the only one in the document.
* `bancho` — the `POST /` description loses two lines and keeps the other 83 unchanged. Those two
  lines were a defect, described below.

Comparison requires normalizing three things, or the real differences drown: literal `
` escapes
*inside* description strings, the generated `timestamp` example values, and the generated
`lastChanged` example value.

### Defects found and fixed during 0.14

* **Leaked source in a public API description.** `BanchoProtocolRoutes.BanchoPacketCatalog` wrapped
  its content in a four-quote raw literal but left the previous declaration line
  (`private const string BanchoPacketCatalog = """`) and its closing `""";` *inside* the string. The
  bancho `POST /` description therefore opened with a line of C# and closed with a raw-string
  terminator, visible to every consumer of the generated document. Pre-existing — it is in the Task
  0.1 baseline too — and found only because 0.14 read the generated documents rather than diffing
  them mechanically. Both stray lines removed and the delimiter returned to three quotes.
* **No `.gitattributes`, with `core.autocrlf=true`.** All 368 source files are LF in the working
  tree and LF in the repository, but a fresh clone would check them out as CRLF, which changes the
  raw string literals that feed the OpenAPI descriptions and so changes the generated documents.
  That would make the baseline comparison this task depends on produce spurious differences for the
  next person who runs it. Pinned with `* text=auto eol=lf`; the working tree needed no
  renormalization, so the commit carries no churn.
* **Two vacuous architecture tests.** `Domain_Should_Not_HaveDependencyOn_Infrastructure` and
  `..._Application` assert against namespaces that no longer exist, so they can never fail. Deleted.
  `..._Web` was renamed to `..._Server` to match the assembly it actually checks, the Protocol rule
  dropped its two dead namespace strings, and the class remarks now point at `SliceBoundaryTests`
  for the in-assembly rules instead of promising them as future work.
* **`CLAUDE.md` described the deleted five-project layout.** This is the worst of the four, because
  that file is loaded into the context of every worker: it was actively instructing them with a
  structure that no longer exists. Its Architecture section now describes the three projects, the
  `Features`/`Shared`/`Host` split, the ten slices, and the two enforced rules.
  `docs/for-developers/architecture.md` gets a banner saying it is out of date and pointing at
  `CLAUDE.md`; rewriting it in full stays with Phase 7 so it is written once against the finished
  structure rather than re-edited after every phase.

## State Phase 1 inherits

**The event hub is built but unadopted.** `Shared/Eventing` holds the new `ILiveEventHub` and its
supporting types, but `IMatchLiveEvents`/`MatchLiveEvents` still exist and still carry every
publish. That is what Task 0.10 scoped, and it is correct — but it means two parallel eventing
mechanisms live in `Shared/Eventing` right now. Task 1.3 replaces the old one. Anyone starting Phase
5 in a worktree is building on the new hub while Phase 1 has not yet retired the old one.

## Pause point

**Phase 0 is complete and signed off. Work stops here.** Resuming into Phase 1 is a deliberate act,
per the standing instruction to pause when the next phase begins.

Phase 1 starts at Task 1.1. Its first two sequencing constraints are already recorded above: the hub
is unadopted until Task 1.3, and `Shared.Sessions.*` reaching into `Multiplayer` is Task 1.4's to
unwind.
