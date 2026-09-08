# Phase 0: Foundation

Status: Implementing
Last updated: 2026-09-08T07:10:00Z (local 2026-09-08 14:10 UTC+7)

## Completed
- Task 0.1 -- captured the pre-migration baseline, commit `91d151f`
- Task 0.2 -- renamed `Basil.Web` to `Basil.Server`, commit `b4cf563`
- Task 0.3 -- merged `Basil.Application` and `Basil.Infrastructure` into `Basil.Server` as slices,
  commit `977b561`
- Task 0.4 -- rewrote the architecture tests with real slice-boundary rules, commit `1142bc1`
- Task 0.5 -- split `Program.cs` into `Host/`, commit `1bee08d`

## Current state
Task 0.5 is complete: build succeeds with 0 errors, full suite is 1621/1621 passed, 0 failed, 0
skipped -- a clean run this time, no flake. `src/Basil.Server/Host/Program.cs` no longer exists;
`Host/Bootstrap.cs` (`public sealed class Bootstrap`) is the new entry point, and
`tests/Basil.IntegrationTests` uses `WebApplicationFactory<Bootstrap>`. Verified by actually running
the built server that the "Host" log category still applies (SourceContext
`Basil.Server.Host.Bootstrap`, output still tagged `[Host]`). Task 0.6 (DI and routing seams) is
next.

## Remaining
- Task 0.6 -- DI and routing seams
- Tasks 0.7 through 0.14 (owned by later workers/phases)

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
- **Task 0.3 arithmetic**: baseline 1622 minus 4 architecture tests = **1618 expected**, matching
  the actual run. The 4 removed tests, all in `tests/Basil.ArchitectureTests/DependencyDirectionTests.cs`,
  named the now-deleted `Application`/`Infrastructure` assemblies via `typeof(Application.AssemblyMarker)`
  / `typeof(Infrastructure.AssemblyMarker)`: `Application_Should_Not_HaveDependencyOn_Infrastructure`,
  `Application_Should_Not_HaveDependencyOn_Web`, `Application_Should_Not_HaveDependencyOn_Frameworks`,
  `Infrastructure_Should_Not_HaveDependencyOn_Web`. The two static field declarations
  (`ApplicationAssembly`, `InfrastructureAssembly`) were also removed -- they no longer compile,
  since the types they name are gone. Kept, unchanged: the five tests that take an assembly by
  string (`Domain_Should_Not_HaveDependencyOn_{Infrastructure,Application,Web,Frameworks}` and
  `Protocol_Should_Not_HaveDependencyOn_AnyOtherBanchoProject`) -- these still compile and still
  assert something true. Task 0.4 replaces the lost coverage with real slice-boundary rules.
- **Trap 1 (SQL migrations), proven, not assumed**: after the move the five `.sql` files are
  embedded under the new resource prefix `Basil.Server.Shared.Persistence.Migrations.*` (confirmed
  via `Assembly.GetManifestResourceNames()` on the built `Basil.Server.dll` -- exactly 5, all
  present). `DbUp`'s single-arg `WithScriptsEmbeddedInAssembly` filters by `.sql` suffix only, so
  the folder/namespace change didn't silently drop any script. Ran a full fresh migration (deleted
  `Data/Basil.db`, forced `-t:Rebuild` so the OpenAPI-generation step's app startup actually
  re-migrated instead of an incrementally-skipped no-op build) and dumped `sqlite_master`. Diffed
  against `plans/execution/baseline/schema.txt`: identical modulo (a) CRLF-vs-LF and DDL
  whitespace-amount differences from how the two capture methods read the `.sql` text (confirmed
  with `diff -b`, semantically empty), and (b) one new, expected object -- DbUp's own
  `SchemaVersions` journal table, absent from the baseline only because Task 0.1's baseline dump
  applied the raw `.sql` files directly with a throwaway script instead of running the real
  `SqlMigrationRunner`/DbUp. No table, column, index, or trigger differs. First attempt at this
  check reused a stale `Data/Basil.db` left over from earlier Release builds during this same
  session and failed with `SQLite error 1: table Users already exists` -- exactly the "looks like
  failure, actually a stale-artifact problem" mirror image of the trap's warning. That db file is
  gitignored build output (`bin/`), not a source artifact; deleting it and rebuilding fixed it.
- **Trap 2 (AssemblyMarker deletion)**: see the Task 0.3 arithmetic entry above.
- **Trap 3 (localization content)**: `Shared/Localization/{BasilBot,Irc}.json` ship as `<Content
  Update=...>` (not `Include=...` -- the SDK's default globbing already picks up every `.json`
  under the project tree as Content, and a duplicate explicit `Include` is a hard `NETSDK1022`
  error) with `Link="Data\Localization\%(Filename)%(Extension)"`, confirmed landing at
  `bin/Release/net10.0/win-x64/Data/Localization/*.json` after a real build.
- **The "namespace nesting" trap, discovered mid-task, not anticipated by the plan**: C# treats a
  dotted namespace declaration (`namespace A.B.C;`) as textually nested inside `A` and `A.B`
  regardless of which file declares which segment, so a type in `Basil.Application.Packets.Channels`
  saw `Basil.Application.Packets.IPacketHandler` with no `using` at all, purely because the child
  namespace sat under the parent. Flattening every slice into its own namespace tree
  (`Basil.Server.Features.<Slice>` no longer nested under anything holding `IPacketHandler`,
  `UserSession`, `SseSubscriberRegistry`, etc.) broke every one of these free rides at once. This
  was the dominant source of compile errors after the physical move and the namespace-declaration
  rewrite (hundreds of `CS0246`/`CS0234`), not namespace mapping mistakes. Fixed by driving a
  compiler-error -> missing-type -> defining-file's-namespace -> inject-`using` loop to convergence
  (see Files changed for the throwaway scripts used, none committed), then a second, smaller pass
  for the same problem inside XML-doc `<see cref="...">` text (`CS1574`), which the compiler
  doesn't error on but does warn on, and which CLAUDE.md's own `Directory.Build.props` comment
  calls out as something this codebase treats as a real build issue.
- A related, deliberate exception-handling choice: the move map calls out roughly a dozen
  directory-vs-file exceptions (`JsonMergePatch.cs`, `IMatchLiveEvents.cs`,
  `RijndaelScoreDecryptor.cs`, the two `IPasswordHasher`/`ITokenGenerator` files, and similar). Every
  one was moved individually rather than via a directory-level `git mv`, exactly as the file-move
  map instructs.
- `LiveSseRoutes.cs` (`src/Basil.Server/Routing/Api/`) was the one genuine code split, done last as
  planned: match-scoped handlers (`HandleMain`, `HandleSettings`, `HandleHost`, `HandleRefs`,
  `HandleBans`, `HandleTimer`, `HandleSlots`, `HandleLiveSlot`, `HandleChat` plus the chat-flush
  helper) became `Features/Multiplayer/MatchLiveRoutes.cs`; the per-player input/status stream
  (`HandleInput`) became `Features/Spectating/PlayerLiveRoutes.cs`; the genuinely shared plumbing
  (`IsSseRoute`, `SetSseHeaders`, `NotLive`, `SseError`, `RegisterWithMatch`, `Subscribe`,
  `SubscribeWithSnapshot`, `SubscribeMultiWithSnapshot`, plus the `ReconnectionInterval` constant)
  became `Shared/Eventing/SseEndpoints.cs`. `SetSseHeaders` was not in the plan's short list of
  "shared helpers" but is genuinely cross-slice infrastructure with zero feature-specific logic, so
  it went to `Shared/Eventing` too rather than being duplicated in both feature files. Call sites
  in `MatchRoutes.cs`, `MatchSubResourceRoutes.cs`, `UserRoutes.cs` and three `Shared/Http/*`
  files (`ApiRequestLoggingMiddleware`, `EnvelopeMiddleware`, `EnvelopeSchemaTransformer`,
  `OpenApiExampleExtensions`, `RequestMetricsMiddleware`) updated accordingly. This is behavior-
  identical code motion -- no stream's wire format, headers, or subscription semantics changed.
- **New cross-slice edge for Task 0.4 to enumerate, not yet in the draft adjacency list**:
  `Features/Users/UserRoutes.cs` calls `Features.Spectating.PlayerLiveRoutes.HandleInput` for its
  per-player `/users/{userId}/live` endpoint (the input/status stream is Spectating-owned per the
  move map, but the route itself is grouped under Users). `("Users", "Spectating")` needs adding to
  `SliceAdjacency.Allowed`. This is a real, single-direction edge the mechanical move surfaced, not
  a circular reference -- expected, since the plan's own draft list says to "start from the real
  compile errors, not from this draft."
- **`Basil.LoadTests` fallout (Phase 6 territory, touched only enough to keep the solution
  building)**: `tests/Basil.LoadTests/Basil.LoadTests.csproj` referenced `Basil.Application.csproj`
  for `LoginService.ReloginGuardWindowSeconds` (a `const int`), used in
  `Configuration/ScenarioSettings.cs` to compute a post-warmup settle time. That project no longer
  exists, and `Basil.Server` cannot be referenced from a non-self-contained project (the SDK
  rejects it; see the pre-existing comment already in that csproj about publishing it as a
  subprocess instead). Removed the dead `ProjectReference`, and duplicated the constant's value
  (`10`) as a private `const int` in `ScenarioSettings.cs` with a comment explaining why it isn't a
  reference and to keep it in sync if the server's guard window changes. This is a one-line,
  well-documented duplication of a magic number, not a harness redesign -- flagging it explicitly
  in case Phase 6 wants a cleaner answer (e.g., a shared constants file both projects can reference).
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
- Task 0.4 (this session), commit `1142bc1`: new
  `tests/Basil.ArchitectureTests/{SliceAdjacency,SliceBoundaryTests}.cs`.
  `tests/Basil.ArchitectureTests/DependencyDirectionTests.cs` unchanged (already in its final
  state from Task 0.3).

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
- Task 0.4 (this session):
  - `dotnet build --configuration Release` -- succeeded, 0 errors.
  - `dotnet test tests/Basil.ArchitectureTests` -- 8/8 passed.
  - `dotnet test --configuration Release` (full suite) -- 1621 total: Domain.Tests 114,
    Protocol.Tests 158, ArchitectureTests 8, Application.Tests 723, Infrastructure.Tests 263,
    IntegrationTests 355 (354 passed + 1 known flake). Re-running
    `BeatmapsetManagementEndpointTests` alone: 16/16 passed, confirming the flake.

## Known issues / blockers
- **Pre-existing Windows file-lock flake in `Basil.IntegrationTests`, not caused by this move.**
  Four full-suite runs this session: one clean 1618/1618; the rest each had exactly one failure,
  never the same test twice with the same signature, never reproducing under `--filter` isolation
  or a same-file rerun:
  - `BeatmapDifficultyEndpointTests.GetDifficulty_PrivateBeatmapsetWithoutAdminKey_ReturnsNotFound`:
    `System.IO.IOException : The process cannot access the file 'vivid.osu' because it is being used
    by another process.` at `FileSystem.RemoveDirectoryRecursive` inside `Dispose()`.
  - `BeatmapsetManagementEndpointTests.PutBeatmapset_Valid_ReplacesTheBeatmapsetsFilesAndReturns202`:
    one run failed only the assert (`Expected: Accepted / Actual: InternalServerError`); a later run
    failed with the *same* trace shape as the sibling above --
    `System.IO.IOException : The process cannot access the file 'old.osu' because it is being used
    by another process.` at `RemoveDirectoryRecursive` inside `Dispose()` (line 100), this time
    wrapped in an `AggregateException` together with the 500-vs-202 assert failure. Same root class:
    a Windows file handle (from the endpoint's own file write, HTTP response buffering, or a
    concurrently-running test class touching a `TestBeatmapsets`-style temp directory) not yet
    released when the test's `Dispose()` tries a recursive delete; the endpoint's own request
    occasionally loses the same race and returns 500 instead of 202.
  - `git diff 91d151f -- tests/Basil.IntegrationTests/BeatmapsetManagementEndpointTests.cs` and the
    same diff for `BeatmapDifficultyEndpointTests.cs`: only `using` lines changed (the mechanical
    namespace rewrite); test bodies, `Dispose()` implementations, and fixture handling are
    byte-identical to the pre-migration baseline. Confirms this is an environmental/Windows
    scheduling flake surfaced by full-suite parallel execution, not a rename- or move-induced
    regression.
  - **For whoever runs the suite next (Task 0.4 and later)**: if you see 1617/1 fail or 1616/2 fail
    in `Basil.IntegrationTests` on one of these two tests, this is the known flake -- rerun before
    treating it as a regression. If a *different* test fails, or one of these two fails with a new
    trace shape, treat it as new and investigate.

## Task 0.4 details (this session)
- **Method**: started `SliceAdjacency.Allowed` empty, ran
  `Slices_Should_Only_Reference_Declared_Slices` against the real merged `Basil.Server` assembly,
  and derived every edge from actual NetArchTest failures rather than the plan's draft list. The
  plan's draft had 19 edges; the real codebase has **38**. Two throwaway probe fixtures
  (`ProbeAdjacency.cs`/`ProbeEdgeDetail.cs`/`ProbeDeps.cs`, a small IL-token scanner using
  `System.Reflection` to resolve which specific type each violation pointed at) were used to
  identify the exact offending file+target-type per edge, then deleted before committing --
  nothing from them is in the repo.
- **Every edge has a one-line justification in `SliceAdjacency.cs`** naming the file(s) that need
  it. None required moving a file out of its slice -- every edge traced back to a real, sensible
  cross-slice service call (e.g. `Auth -> {Chat,Content,Irc,Multiplayer,Spectating,Users}` all come
  from `LoginService`/`AdminKeyService`/`ClientIntegrityService`/`AuthenticationService` doing
  exactly what login/auth needs to do: seed channel membership, read settings, check anticheat
  context, publish presence, resolve the user).
- **`("Users", "Spectating")` was pre-approved by the orchestrator** and added with that
  justification (`Features/Users/Packets/ChangeActionHandler.cs`, `UserRoutes.cs`'s
  `/users/{id}/live`).
- **One observation flagged, not acted on**: `Multiplayer.UserBrief`/`UserBriefResolver` are used
  by Chat, Bot, Scores and Spectating as a generic "summary view of a user" DTO, and are the sole
  reason for the `("Spectating", "Multiplayer")` edge (every Spectating file that touches
  Multiplayer touches only `UserBrief`). This is a plausible "file in the wrong slice" case --
  `UserBrief` arguably belongs in `Users` -- but moving it changes ~8 files' `using`s and would add
  a new `(Users, Irc)` edge (via `UserBriefResolver` -> `IrcSession`), a design question wider than
  Task 0.4's surgical scope. Left as-is; flagged for whoever next touches Multiplayer or Users.
- **`Shared_Should_Not_Reference_Features` is a pinned baseline (14 named types), not an absolute
  ban** -- see `SliceBoundaryTests.cs`'s doc comment for the full list and per-type reason. This
  was an advisor-reviewed decision: the plan's Step 3 draft has no allowlist mechanism for this
  rule, but running it against the real assembly found 14 pre-existing violations (`GameSession`/
  `UserSession` holding a live `MatchSession` and IRC bridge connection; several `Shared/Http`
  host-route files inlining slice logic instead of only delegating to it; three `Shared/Media`
  asset providers; `FileSystemReplayStorage`) that predate this migration and are out of Phase 0's
  scope to fix -- `GameSession`/`UserSession` in particular is Task 1.4's ("MatchSession model
  encapsulation") territory. The test asserts set-equality against the 14 names, so a *new*
  Shared -> Features edge still fails the build, and fixing an existing offender fails too (a nudge
  to shrink the pinned list, not silence the test).
- **`Shared_Segments_Should_Come_From_The_Allowlist`** explicitly excludes the bare
  `Basil.Server.Shared` namespace (no segment) from the check, so `BasilMetrics.cs` (which Task 0.7
  splits) doesn't need a tenth segment added.
- `DependencyDirectionTests.cs` needed no edits -- Task 0.3 already left it in the desired end
  state (five kernel-purity tests, no Application/Infrastructure references).
- Two pre-existing carry-over items from Task 0.3, not resolved by Task 0.4, still open for later
  phases:
  - `Basil.LoadTests`' `ScenarioSettings.ReloginGuardWindowSeconds` is a locally-duplicated
    constant (see the Task 0.3 entries above) -- Phase 6's territory.
  - `AnnounceRoutes.cs` (Content) has a dead `using Basil.Server.Features.Bot;` (no real
    dependency, confirmed via probe) -- harmless, not touched, since removing unrelated dead code
    is out of this task's surgical scope.

## Task 0.5 details (this session)
- **`ConfigureRouting`/`Configure<ServerOptions>` stayed inline in `Bootstrap.Main`**, not folded
  into `SliceRegistration.AddAll` as originally planned -- an advisor review caught that this would
  make Task 0.6 move them a second time. Both are one call each, right after `KestrelSetup.Configure`
  and before `SliceRegistration.AddAll`, exactly where they were in the original `Main`.
  `ConfigureRouting` stayed a private static method on `Bootstrap` (it already had its own XML doc
  and didn't fit any of the plan's other 11 files).
- **The CategoryEnricher fix (flagged last session) was applied and verified by actually running the
  built server**, not just by test count: `CategoryEnricher.cs`'s rule
  `("Basil.Server.Program", false, "Host")` -> `("Basil.Server.Host.Bootstrap", false, "Host")`; ran
  `Basil.Server.exe` directly and confirmed the banner still logs `[Host]  Basil.Server.Host.Bootstrap: ...`
  rather than falling through to the `[App]` fallback category. Its doc comment's stale
  `Program.ConfigureSerilog` reference was also updated to `SerilogSetup.Configure`.
- **The namespace-nesting trap (documented for Task 0.3) recurred, in reverse, for this task**:
  `Program` lived in the bare `Basil.Server` namespace, an *ancestor* of every
  `Basil.Server.Shared.*`/`Basil.Server.Features.*` namespace, so several files referenced `Program`/
  `ILogger<Program>` with no explicit `using`. Moving it to the sibling namespace
  `Basil.Server.Host` broke exactly one such file for real:
  `src/Basil.Server/Shared/Http/BanchoProtocolRoutes.cs` (used `ILogger<Program>` inside the login
  packet-exchange handler) -- fixed with an added `using Basil.Server.Host;` and
  `ILogger<Program>` -> `ILogger<Bootstrap>`. Grepped the whole tree for `\bProgram\b` after the
  move to confirm no other file was relying on the same implicit visibility; none were (the other
  hits were all doc-comment prose, fixed below).
- **Doc-comment prose referencing `Program.cs` updated** (these are directly about the file being
  moved, not unrelated drive-by edits): `AdminKeyAuthenticationHandler.cs`, `EnvelopeMiddleware.cs`,
  `ReplyLocale.cs`, `Basil.Server.csproj` (two comments), and
  `tests/Basil.IntegrationTests/OpenApiDocumentEndpointTests.cs`.
- **`tests/Basil.IntegrationTests`**: all 33 files using `WebApplicationFactory<Program>` moved to
  `WebApplicationFactory<Bootstrap>` with `using Basil.Server;` -> `using Basil.Server.Host;`
  (verified first that every one of those 33 files had exactly one `using Basil.Server;` line, used
  for nothing but `Program`, before doing the global replace). Two files
  (`TestDoubles.cs`, `InMemoryMenuBannerRepository.cs`) got touched by the same sed pass but had no
  actual match -- their working-tree diff was empty (a pure CRLF/LF touch), so they were
  `git checkout --`-ed back out rather than committed as no-op noise.
- No `StartupObject` is set in `Basil.Server.csproj` or `Basil.slnx`; the SDK finds the sole
  `Main` method automatically, so no build-file change was needed for the entry-point rename.
- Verification: `dotnet build --configuration Release` -- 0 errors (only the same 16 pre-existing
  `NU1510`/nullable/unused-event warnings). Full suite: **1621/1621 passed, 0 failed, 0 skipped** --
  a clean run, the documented flake did not reproduce this time.

## Next exact step
Task 0.6: DI and routing seams, per the plan. This worker (continuing in the same session) has
already read both `Host/ApplicationDependencyInjection.cs` (namespace `Basil.Application`,
`AddApplication`) and `Host/InfrastructureDependencyInjection.cs` (namespace `Basil.Infrastructure`,
`AddInfrastructure`) in full and mapped every registration to its owning slice by checking each
registered type's actual file location (not guessed from its name). Key findings for whoever
resumes this if the session ends mid-task:
- Both DI files register a mix of per-slice types and genuinely `Shared/`-owned types
  (`DatabaseOptions`, `StorageOptions`, the shared `IMemoryCache`, `PacketDispatcher`,
  `GhostDisconnectService`, `ISessionRegistry<GameSession>`, `IResponseCache`/
  `FileSystemResponseCache`) -- the latter go into `Host/SliceRegistration.cs`'s new
  `AddSharedInfrastructure`, per the plan.
- A few registrations are easy to mis-slice by name alone and were double-checked against the
  defining file: `IClientHashRepository`/`IRelationshipRepository`/`IUserLogRepository` are Users
  (not Auth); `ISessionRegistry<IrcSession>`'s concrete `IrcSessionRegistry` is Irc; `MirrorOptions`
  binds in Beatmaps (the type lives in `Shared/Configuration` but the feature is Beatmaps' mirror
  service); `IResponseCache`/`FileSystemResponseCache` is genuinely Shared (`Shared/Storage`), used
  by the audio-preview cache which is Shared/host-route code, not a single slice.
- `BanchoHostGroups.cs` (`Shared/Http`) today calls `MapApiGroup`/`MapOsuWebGroup`/
  `MapBeatmapAssetGroup`/`MapAvatarGroup`/`MapAssetsGroup`, each of which is defined in a *different*
  `Shared/Http/*.cs` file and itself calls slice-owned `group.MapXxxRoutes()` (e.g. `ApiHostRoutes.
  MapApiGroup` calls `group.MapMatchRoutes()`, `group.MapUserRoutes()`, etc; `AssetsHostRoutes.
  MapAssetsGroup` calls `group.MapMenuAssetRoutes()`/`group.MapBeatmapsetAssetRoutes()`). Per an
  advisor-reviewed decision during Task 0.4: move only these *delegating* slice-route calls out to
  `SliceRegistration.MapAll` (called against the exposed host group); leave each host-group file's
  own inline logic (health checks, docs redirects, the whole raw-protocol handler bodies in
  `BanchoProtocolRoutes.MapBanchoGroup` and `OsuWebRoutes.MapOsuWebGroup`) exactly where it is --
  those files are already in the `Shared_Should_Not_Reference_Features` pinned baseline from Task
  0.4 and are not this task's problem to fix.
- `BanchoHostGroups.MapAll` needs to change from calling `.MapXxxGroup()` on each constructed group
  to instead **returning** the constructed groups (a small record/tuple: Bancho, OsuWeb,
  BeatmapAssets, Avatar, Api, Assets), so `SliceRegistration.MapAll` can call each host-group's own
  remaining `MapXxxGroup()` (for the non-delegating parts) and then the slice-owned
  `MapXxxRoutes()` calls, per host group, in the same order the routes register today.
- Content has 8 separate existing route files (`AnnounceRoutes`, `FaqRoutes`, `MenuAssetRoutes`,
  `MenuBannerRoutes`, `MenuIconRoutes`, `MenuSeasonalRoutes`, `MirrorSettingsRoutes`,
  `MotdSettingsRoutes`) split across two host groups (`api.` and `assets.`) -- plan to give Content
  two aggregating methods (`MapContentRoutes` for the `api.` host's pieces, a second for the
  `assets.` host's `MenuAssetRoutes`) rather than forcing one method across two unrelated route
  groups.
- After this task: run the route-table diff
  (`grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src --include=*.cs | sort -u` against
  `plans/execution/baseline/routes.txt`) and confirm it is empty, and confirm the full suite is
  still exactly 1621/1621 (this task must not change the count).

Also see "Known issues / blockers" above before treating any `Basil.IntegrationTests` failure on
`BeatmapDifficultyEndpointTests` or `BeatmapsetManagementEndpointTests` as new.
