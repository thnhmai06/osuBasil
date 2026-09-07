# Orchestration state

Last updated: 2026-09-07T15:42:00Z (local 2026-09-07 22:42 SEAST)

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
| 0 Foundation | Not started | nothing | — |
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

none

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
| 2026-09-07 22:42 SEAST | 2026-09-08 03:42 SEAST | pending |

Cron jobs are session-only: they do not survive this Claude session ending. If a new session
picks this work up, its first action after reading this file is to schedule a fresh +5h reminder.

## Next action for the orchestrator

Push the four design/plan commits, cut `feat/vsa-migration`, then delegate Phase 0 Task 0.1
(baseline capture) to a Sonnet worker.
