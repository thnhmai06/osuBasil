# Orchestration state

Last updated: 2026-09-07T23:20:00Z (local 2026-09-08 06:20 UTC+7)

## How to resume

Read this file, then `git status`, `git log --oneline -20`, and the checkpoint of whichever
phase is not `Done`. Continue from that phase's **Next exact step**. Do not re-investigate
anything already recorded here or in `plans/vsa-migration-design-20260907.md`,
`plans/diagnostic-metric-inventory-20260907.md`, or `plans/execution/baseline/`.

Authoritative documents:

* `plans/vsa-migration-plan-20260907.md` — the task-by-task plan being executed
* `plans/vsa-migration-design-20260907.md` — the approved design (revision 3)
* `plans/diagnostic-metric-inventory-20260907.md` — probe-verified metric inventory, input to
  Phase 5; do **not** re-derive it
* `plans/execution/file-move-map.md` — the per-directory destination map for Task 0.3, derived from
  the tree at `d5d1b32`; do **not** re-derive it

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
| 0 Foundation | Implementing | nothing | Sonnet worker (Tasks 0.4, 0.5, 0.6) |
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
| 2026-09-08 06:20 UTC+7 | Tasks 0.4, 0.5, 0.6 | running |

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

## Advisor checkpoints

| After | Done | Notes |
| --- | --- | --- |
| Phase 0 | no | folder move vs real restructure; adjacency allowlist honesty; `Shared` purity; baseline diff |
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
| 2026-09-08 06:20 UTC+7 | 2026-09-08 11:21 UTC+7 | pending |

The usage limit resets at 01:10 UTC+7, so the first continuation fires just after that rather
than a flat five hours out. Subsequent cycles go back to +5h unless a reset time is known.

Cron jobs are session-only: they do not survive this Claude session ending. If a new session
picks this work up, its first action after reading this file is to schedule a fresh +5h reminder.

## Next action for the orchestrator

Review what the current worker lands for Tasks 0.4–0.6: the adjacency allowlist must be small and
every edge justified in one line, `Shared` must still hold only its nine approved segments, and the
route diff against the baseline must be empty. Then delegate Tasks 0.7 through 0.9 (metric split,
localization catalog, configuration source chain).
