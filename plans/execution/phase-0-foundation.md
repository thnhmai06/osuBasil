# Phase 0: Foundation

Status: Implementing
Last updated: 2026-09-07T16:10:00Z

## Completed
- Task 0.1 -- captured the pre-migration baseline, commit (pending, see below)

## Current state
Task 0.1 is done and its artifacts are staged under `plans/execution/baseline/`. Task 0.2
(rename `Basil.Web` -> `Basil.Server`) is next, not yet started.

## Remaining
- Task 0.2 -- rename `Basil.Web` to `Basil.Server`
- Tasks 0.3 through 0.14 (owned by later workers/phases)

## Important decisions
- The plan's Task 0.1 Step 4 assumes a single `Basil.Web.json` OpenAPI document. In reality the
  build's `Microsoft.Extensions.ApiDescription.Server` step emits **six** named documents (one per
  host/tag group: `bancho`, `osuweb`, `beatmapassets`, `avatar`, `assets`, `basilapi`), written to
  `src/Basil.Web/obj/Basil.Web_<name>.json` on every Release build. All six were captured under
  `plans/execution/baseline/openapi/<name>.json` rather than a single `openapi.json`, since they
  collectively cover the full API surface and dropping five of them would leave most of the
  surface undiffed after later phases. `basilapi.json` is the api.<domain> host envelope-covered
  document; the other five are Bancho/asset/avatar hosts.
- `tests/Basil.LoadTests` is `IsTestProject=false` (NBomber harness, `OutputType=Exe`) and is
  correctly excluded by `dotnet test`. Its 0 tests are not part of the 1622-test baseline and this
  is expected, not a gap.
- The schema dump used a throwaway console project under the scratchpad directory (outside the
  repo, per instructions), referencing `Microsoft.Data.Sqlite` 10.0.11 directly, applying the five
  migration `.sql` files from `src/Basil.Infrastructure/Persistence/Migrations/` in filename order
  against a temp-file SQLite database, then dumping `sqlite_master`. Nothing from that project was
  added to the repository.

## Files changed
- `plans/execution/baseline/base-sha.txt` -- `97e7d5691f63677deb744ac23dc243973ffaffc6`
- `plans/execution/baseline/build.txt` -- tail of the Release build (2 warnings, 0 errors)
- `plans/execution/baseline/test-summary.txt` -- full-suite baseline: **1622 total / 1622 passed /
  0 failed / 0 skipped**, per-project breakdown included
- `plans/execution/baseline/openapi/*.json` -- six generated OpenAPI documents (see decision above)
- `plans/execution/baseline/schema.txt` -- 46 `sqlite_master` objects (12 tables, 1 sqlite_sequence,
  18 named/auto indexes, 6 triggers) from a freshly migrated database
- `plans/execution/baseline/metrics.txt` -- 7 metric name literals matching `"basil.*"` under `src/`
- `plans/execution/baseline/routes.txt` -- 138 `Map(Get|Post|Put|Patch|Delete)` route literals under
  `src/Basil.Web/`
- `plans/execution/baseline/locale-keys.txt` -- 154 `BasilBot.*`/`Irc.*` localization keys

## Verification
- `dotnet build --configuration Release` -- succeeded, 0 errors, 2 pre-existing warnings
  (unrelated nullable-null-literal and unused-event warnings, not touched by this task)
- `dotnet test --configuration Release --logger "trx;LogFileName=baseline.trx"` -- 1622/1622 passed,
  0 failed, 0 skipped

## Known issues / blockers
- none

## Next exact step
Task 0.2: `git mv src/Basil.Web src/Basil.Server`, then `git mv
src/Basil.Server/Basil.Web.csproj src/Basil.Server/Basil.Server.csproj`, update `Basil.slnx`,
rewrite `Basil\.Web` -> `Basil.Server` across `.cs`/Dockerfile/compose files, repoint
`ProjectReference`s, then verify with `dotnet build --configuration Release` and `dotnet test`
against this baseline's 1622/1622 count.
