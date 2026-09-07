# Phase 0: Foundation

Status: Implementing
Last updated: 2026-09-08T01:40:00Z (local 2026-09-08 08:40 UTC+7)

## Completed
- Task 0.1 -- captured the pre-migration baseline, commit `91d151f`
- Task 0.2 -- renamed `Basil.Web` to `Basil.Server`, commit (see below, this session)

## Current state
Task 0.2 is complete and verified: build succeeds, full suite reports 1622/1622. Task 0.3 (merge
Application + Infrastructure into Basil.Server as slices) is next.

## Remaining
- Task 0.3 -- merge `Basil.Application` and `Basil.Infrastructure` into `Basil.Server`
- Tasks 0.4 through 0.14 (owned by later workers/phases)

## Important decisions
- Task 0.2's file moves (all 46 `.cs` files, `Basil.slnx`, `Dockerfile`, `docker-compose.yml`, the
  two GitHub workflow files) were completed and left uncommitted by a prior worker killed by a
  session limit. This worker finished the two remaining loose ends -- the `Basil.Web` prose
  mentions in `src/Basil.Application/Basil.Application.csproj:19` and
  `tests/Basil.LoadTests/Basil.LoadTests.csproj:24` -- and deleted the stale, untracked
  `src/Basil.Web/obj/` directory (no tracked content was under it; confirmed with
  `git status --short -- src/Basil.Web/` returning empty before deletion).
- **Rename-induced regression found and fixed**: `dotnet test` initially reported 1621/1622 (one
  failure) after the Task 0.2 rename. `Basil.ArchitectureTests.DependencyDirectionTests
  .Application_Should_Not_HaveDependencyOn_Web` failed with "Architecture rule violated by:
  Basil.Application.Configurations.ServerOptions" -- a false positive, not a real architecture
  violation. Root cause, confirmed with a throwaway Mono.Cecil dump of `ServerOptions`'s actual IL
  dependencies (zero references to anything named `Basil.Server`) plus a probe matrix against
  NetArchTest.Rules 1.3.2 directly: NetArchTest also scans `const` field values for a dependency
  match (an internal field literally named `_serachForDependencyInFieldConstant` in its DLL
  strings). `ServerOptions.SectionName = "Basil:Server"` (an ASP.NET Core config-section key,
  unrelated to any project reference) coincidentally matches the search pattern `"Basil.Server"`
  under NetArchTest's `:`/`.`-insensitive constant scan. This was dormant while the pattern was
  `"Basil.Web"` (no config section is named `"Basil:Web"`) and only surfaced once Task 0.2's
  mechanical `Basil.Web` -> `Basil.Server` rename touched this test file's string literal too.
  Confirmed via a probe: `NotHaveDependencyOn("Basil.Bot")` likewise falsely flags
  `BotOptions.SectionName = "Basil:Bot"`, and `NotHaveDependencyOn("Basil.Server.")` (trailing dot)
  passes cleanly while still anchoring to a real namespace/type dependency. Fix: added a trailing
  dot to that one test's search string, with a comment explaining why. This is scoped narrowly --
  `Domain_Should_Not_HaveDependencyOn_Web` (no colliding constant, passes today) was deliberately
  left untouched, since Part B (Task 0.3) deletes `Application_Should_Not_HaveDependencyOn_Web`
  entirely (it references the doomed `Application.AssemblyMarker` type) and this fix/comment goes
  with it.
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
- Task 0.1 baseline artifacts (see prior entry below) -- committed at `91d151f`.
- Task 0.2 (this session): `src/Basil.Application/Basil.Application.csproj`,
  `tests/Basil.LoadTests/Basil.LoadTests.csproj` (prose `Basil.Web` -> `Basil.Server`);
  `tests/Basil.ArchitectureTests/DependencyDirectionTests.cs` (NetArchTest false-positive fix, see
  decision above); deletion of the untracked `src/Basil.Web/obj/` directory. All other Task 0.2
  moves (46 `.cs` files under `src/Basil.Server/`, `Basil.slnx`, `Dockerfile`,
  `docker-compose.yml`, `.github/workflows/{ci,release}.yml`) were already staged by the prior
  worker and are included in this task's commit.
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
- Task 0.1: `dotnet build --configuration Release` -- succeeded, 0 errors, 2 pre-existing warnings
  (unrelated nullable-null-literal and unused-event warnings, not touched by this task)
- Task 0.1: `dotnet test --configuration Release --logger "trx;LogFileName=baseline.trx"` --
  1622/1622 passed, 0 failed, 0 skipped
- Task 0.2 (this session):
  - `grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src --include=*.cs | sort -u` diffed
    byte-for-byte identical against `plans/execution/baseline/routes.txt` (confirms the src-wide
    grep used for later Task 0.3 verification is already equivalent to the `src/Basil.Web`-scoped
    baseline capture -- no drift to chase post-move).
  - `dotnet build --configuration Release` -- succeeded, 0 errors.
  - `dotnet test --configuration Release` -- first run: 1621 passed / 1 failed (see the
    NetArchTest decision above). After the one-line fix: 1622 passed / 0 failed / 0 skipped,
    matching the baseline exactly. Per-project: Domain.Tests 114, Protocol.Tests 158,
    Application.Tests 723, ArchitectureTests 9, Infrastructure.Tests 263, IntegrationTests 355.

## Known issues / blockers
- none

## Next exact step
Task 0.3: merge `Basil.Application` and `Basil.Infrastructure` into `Basil.Server` per
`plans/execution/file-move-map.md`, one atomic commit. Advisor-flagged risks to check going in:
`AllowUnsafeBlocks` is set on `Basil.Infrastructure.csproj` but not `Basil.Server.csproj` (the
`HardLink.cs` mover needs it carried over); confirm localization JSON actually lands next to
`tests/Basil.Application.Tests`'s (soon `Basil.Server`-referencing) test output, not just
`Basil.Server`'s own; check whether `tests/Basil.IntegrationTests` uses
`WebApplicationFactory<Program>` before `Program.cs` moves to `Host/` under a real namespace;
dedupe `SixLabors.ImageSharp.Web` and `Microsoft.Extensions.Logging.Abstractions`
`PackageReference`s when folding csproj files; pull the four `Services/Multiplayer/JsonMergePatch.cs`
/ `Sessions/Multiplayer/IMatchLiveEvents.cs` / `Infrastructure/Security/RijndaelScoreDecryptor.cs`
/ `Abstractions/Users/{IPasswordHasher,ITokenGenerator}.cs`-style exceptions out of their directory
moves individually rather than relying on a directory-level `git mv`; do the `LiveSseRoutes.cs`
three-way split last, after every other mechanical move is green.
