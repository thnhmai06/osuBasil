# `plans/` — what is live, what is a decision, what is history

Local working documents for the architecture migration on `feat/vsa-migration`. Nothing here is
user documentation; `docs/` owns that. **Start at `execution/HANDOVER.md`.**

## Working documents — kept current, in the same commit as the change they describe

| Document | Owns |
|---|---|
| `execution/HANDOVER.md` | current state, next task, instrument map, operating rules, open items |
| `architecture-v3-migration-plan-20260916.md` | **the next plan, awaiting user review**: Domain / Application / Infrastructure split, then `Basil.Host.*` transports; per-feature ownership, 14 batches, what the old plan loses |
| `basil-plan-20260909.md` | the plan executed through Stage D1 and G; D2–D4/E2 superseded by the v3 plan |
| `execution/architecture-progress.md` | the coupling numbers, per instrument, with the commit each was measured on |
| `execution/phase-stage-c.md` | Stage C worker checkpoint: what each task did, what is applied but uncommitted |
| `execution/orchestration.md` | operating lessons: one worker per tree, test-run form, staging rules, tooling audit |
| `execution/measure-slice-graph.py` | the report script; a search tool, not a gate — see the instrument map |

## Decision records — settled, do not re-open without new evidence

| Document | Settles |
|---|---|
| `execution/const-visibility-experiment.md` | `static readonly` over `const` for boundary-crossing values (ADR-008) |
| `execution/stage-c-order-decision.md` | Stage C runs in reverse, project boundary crossed last |
| `execution/c2-deferred-decision.md` | C2 off the path; `.Match` absorbed into C1 |
| `execution/c1-transport-seam-decision.md` | C1 split: seam first (C1a), move later (C1b); the `TransportSeamTests` instrument |
| `execution/c1b-project-move-decision.md` | C1b's working checkpoint: verified-unit-at-a-time moves, blockers a text classifier misses, unit progress |
| `execution/chat-seam-decision.md` | the chat seam: `ChatLine`, `IChatNotifier`, `IChannelNotifier`, five-commit order |
| `execution/hub-adoption-decision.md` | the event hub carries deltas only |
| `execution/logout-as-event-decision.md` | logout is an ordered handler list, not an event bus |
| `execution/diagnostics-boundary-decision.md` | `Diagnostics -> Auth` only; other edges refused for published gauges |
| `execution/mutation-invariant-audit.md` | no `Invalidate()`, no suppression path in `MatchMutationScope` |

## Specifications — inputs the plan was written from; measured facts in them are dated

| Document | Note |
|---|---|
| `architecture-assessment-20260908.md` | the measurements; §2 of the target carries a correction to its database finding, and it never measured business → `Basil.Protocol` (see `c1-transport-seam-decision.md`) |
| `architecture-target-20260908.md` | target structure and invariants; §1.2 decisions are the user's |
| `vsa-migration-design-20260907.md` | eventing, mutation-scope and diagnostics design (revision 3) |
| `diagnostic-metric-inventory-20260907.md` | probe-verified metric inventory; do not re-derive |
| `localization-rules-input-20260909.md` | user-supplied rule set, input to Task H2 |
| `execution/baseline/` | the Phase 0 baseline every verification diffs against |

## Historical — read for reasoning, never for current state

* `vsa-migration-plan-20260907.md` — superseded by `basil-plan-20260909.md`, whose last section
  records which of its 57 tasks are alive, absorbed or dead
* `execution/phase-0-foundation.md`, `execution/phase-1-multiplayer.md`,
  `execution/phase-5-diagnostics.md` — checkpoints of finished phases. `phase-1`'s "Task 1.3's
  design question" was overtaken by `hub-adoption-decision.md`
* `execution/file-move-map.md` — the Task 0.3 move map, executed
* `known-limitations.md` — moved here from `docs/` in `8d3e9060` as a local investigation record;
  the `perf-*` and `rc11-*` documents that link to its old path are the pre-migration perf
  investigation and are not maintained
