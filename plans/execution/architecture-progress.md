# Architecture progress — the numbers that say whether this is working

The migration in `plans/basil-plan-20260909.md` is judged by two measurements, not by how many
tasks are ticked. Both come from the assessment and are re-derived the same way each time.

## How to re-measure

Attribute every `using Basil.Server.Features.X` to the slice the importing file lives in, keep only
the edges where a type declared in the target namespace actually appears in the importing file's
**code** (not its comments), then run Tarjan over the result. The scripts used to produce the
baseline are in the assessment's method section; the two traps that produced wrong numbers the
first time are worth repeating:

* A bare `\.Match\b` search counts `Regex.Match`. Anchor it to a session variable.
* A naive type-name extractor picks English words out of XML comments (`record the ...` yields a
  "type" named `the`). Require an access modifier, an uppercase initial, and skip comment lines.

## Baseline and target

| Measurement | 2026-09-08 baseline | After stage C | Target |
|---|---:|---:|---:|
| Live cross-slice edges | 44 | ~17 predicted | as few as the domain really needs |
| Strongly connected components | 1 of 10 | 5 free + a knot of 5 | no cycle that is not declared |
| Mutual (two-cycle) slice pairs | 15 | — | 3, each declared with a reason |
| Declared allowlist edges | 38, of which 9 were invisible | 45, all visible | shrinking |
| `Shared -> Features` pinned offenders | 12, of which 1 was invisible | 13 | 0 |
| Dead / documentation-only imports | 5 / 7 | 0 / 0 | 0 |

## Log

| Date | Commit | Change | Edges | Components |
|---|---|---|---:|---|
| 2026-09-08 | `e7a1bf7` | assessment measured the baseline | 44 | 1 of 10 |
| 2026-09-09 | `ed48716` | stage A made constant-mediated coupling visible; 7 edges declared, 1 `Shared` offender pinned | 44 | 1 of 10 |

Stage A changed no coupling. It changed what the rule can see, which is why the edge count is
unchanged while the declared count rose from 38 to 45.

## The rule for stage C

`plans/basil-plan-20260909.md` task C5 says to stop and report if the graph does not move as
predicted. That is not a formality: stage D splits the code into projects along these boundaries,
and a project split made against a graph that is still one component produces projects that cannot
compile without each other.
