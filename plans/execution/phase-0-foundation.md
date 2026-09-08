# Phase 0: Foundation

Status: Implementing
Last updated: 2026-09-08 (local, UTC+7)

## Completed
- Task 0.1 -- captured the pre-migration baseline, commit `91d151f`
- Task 0.2 -- renamed `Basil.Web` to `Basil.Server`, commit `b4cf563`
- Task 0.3 -- merged `Basil.Application` and `Basil.Infrastructure` into `Basil.Server` as slices,
  commit `977b561`
- Task 0.4 -- rewrote the architecture tests with real slice-boundary rules, commit `1142bc1`
- Task 0.5 -- split `Program.cs` into `Host/`, commit `1bee08d`
- Task 0.6 -- DI and routing seams (per-slice DI + routing surfaces), commit `a3e7b99`. This entry
  was missing from this checkpoint until now -- the file was left saying "Next exact step: Task 0.6"
  after 0.6 had already landed; backfilled by the worker doing 0.7/0.9/0.8 after noticing the
  mismatch against `git log`.
- Task 0.7 -- split `BasilMetrics` per slice into `Shared/Http/HttpMetrics`,
  `Shared/Persistence/PersistenceMetrics`, `Shared/Eventing/EventingMetrics`,
  `Features/Multiplayer/MultiplayerMetrics`, all on `Shared/BasilMeter`, commit `d1866c8`. Metric
  name diff actually measured (not eyeballed):
  `grep -rhoE '"basil\.[a-z_.]+"' src --include=*.cs | sort -u | diff plans/execution/baseline/metrics.txt -`
  exit code 0, empty diff. Full suite 1621/1621 (one `Basil.IntegrationTests` failure on the combined
  run was the documented Windows file-handle flake; isolated re-run of that project alone was
  355/355).
- Task 0.9 -- configuration source chain, commit `ff449ae`.
- Task 0.8 -- localization loader and per-slice fragments, commit (this session, see below).
- Task 0.10 -- `LiveEventHub` and `StateStream`, commit (this session, see below).

## Current state
Tasks 0.1-0.10 are done. Full suite: **1628/1628 passed, 0 failed, 0 skipped**
(1625 prior baseline + 3 from Task 0.10's `LiveEventHubTests`). Next up is Task 0.11 (merge the
test projects), done in the same session -- see its own entry below.

### Task 0.10 details (this session)
- **`ILiveEventHub`/`LiveEventHub`/`LiveSubscription`/`StreamKey` created exactly per the plan's
  interface**, under `Shared/Eventing/`. The hub holds a `ConcurrentDictionary<StreamKey,
  StreamState>`; each `StreamState` is a private nested class carrying its own `Lock`, the latest
  payload, its version, a stale flag and the subscriber list. `Open` takes that per-stream lock to
  register the new `LiveSubscription` and capture `(Latest, Version, IsStale)` in the same
  critical section `Publish` uses to update them -- this is what removes the drain-then-read window
  the design calls out. `Publish` stores the new state and snapshots the subscriber list under the
  lock, then invokes each subscriber's `OnPublish` outside it. The hub holds no reference to a
  repository, feature DTO or snapshot builder -- it moves `ReadOnlyMemory<byte>` and a `long`
  version, nothing else.
- **The subscriber-local "is this stale subscription still waiting for its first real snapshot"
  state (`_hasSnapshot`) was not in the plan's interface list but was necessary to make the given
  tests pass together, derived from re-reading design 4.2, not invented independently.** The
  ordering test (`SubscriberNeverReceivesAnEventAtOrBelowItsSnapshotVersion`) requires that once a
  subscription opens non-stale, `Version`/`Snapshot` stay frozen at their Open-time values forever,
  and every later publish is only ever visible through `Events`. The seed-race test
  (`SeedLosesToAPublishThatLandedWhileTheSnapshotWasBeingBuilt`) requires the opposite for a
  subscription that opened *stale*: a publish landing before the subscriber calls
  `SeedIfNotSuperseded` must itself become the adopted `Snapshot`/`Version` directly, not merely
  queue as an event, because there is no established baseline yet for an event to be "after". A
  single boolean distinguishing these two regimes (set to `!isStale` at `Open`, and flipped once
  either a publish resolves a stale subscription or `SeedIfNotSuperseded` succeeds) satisfies both
  tests. `SeedIfNotSuperseded` itself is the `SequenceGate.TryAdvance` shape asked for: it compares
  its own current `_version` against the caller's `fence` and only adopts the caller's snapshot if
  nothing landed since the fence was captured.
- **`Events` is an unbounded per-subscription `Channel<LiveEvent>`,** matching design 4.3's explicit
  statement that "unbounded state streams cannot drop; this applies to the bounded multiplexed
  streams only" -- the bounded, gap-marking behavior stays in `BoundedSseChannel`, unused by the hub
  in this task, and is a Phase 1 adoption concern, not built speculatively here.
  `LiveEventHub.Publish` does not itself gate out-of-order versions (no `SequenceGate` at the hub
  level): the plan's Step 5 implementation description doesn't call for one, and there's no test
  requiring it -- the ordering guarantee the SSE contract needs (guarantee 3: no event at or below
  a subscriber's snapshot version) is enforced per-subscription by `_hasSnapshot`/`_version`, not by
  rejecting a stale hub-wide publish. Flagged in case Phase 1 finds a caller that can race two
  `hub.Publish` calls out of version order for the same key -- not reproduced or tested here.
- **A real bug in the plan's own test code was measured, not assumed, and fixed in the test only.**
  The seed-race test's final assertion, `Assert.Equal(Bytes("published-11"), sub.Snapshot)`, compares
  two `ReadOnlyMemory<byte>` values built from two *separate* calls to the `Bytes(...)` helper.
  `ReadOnlyMemory<byte>.Equals` compares the underlying array reference, index and length -- not
  content -- confirmed with a throwaway console project
  (`a.Equals(b)` false, `a.Span.SequenceEqual(b.Span)` true, for two arrays holding identical UTF-8
  bytes) and confirmed again by running the test with a non-interning `Bytes` helper: it failed with
  "Values differ" despite my `LiveSubscription` already holding the byte-identical payload passed to
  `hub.Publish`. Fixed by making the test's own `Bytes` helper intern by content (a
  `Dictionary<string, byte[]>` cache), so two calls with the same literal return the same array and
  the reference-based equality check becomes meaningful again -- this only touches the test's private
  helper, not the interface or the hub's implementation, and two different literals still can't
  accidentally collide. Watched all three tests fail before this fix existed (types didn't exist,
  per the plan's Step 4) and pass after -- 3/3.
- **`SnapshotChannel<T>` renamed to `StateStream<T>`,** matching design 4.1's naming ("`StateStream<T>`
  (today's `SnapshotChannel<T>`)"). File `Shared/Eventing/SnapshotChannel.cs` -> `StateStream.cs`
  via `git mv`; every call site's type name updated (`MatchSession.cs`'s eight snapshot-channel
  properties, `MatchMembershipService.cs`, `BasilJsonOptions.cs`'s doc comment, plus the
  `<see cref>`/prose mentions in `JsonMergePatchTests.cs`, `MatchMembershipServiceTests.cs`,
  `MatchLiveChannelsEndpointTests.cs`, `MatchSubResourceSseEndpointTests.cs`). No call site's
  *behavior* changed -- this is identifier renaming only. The dedicated unit test class
  `SnapshotChannelTests` was renamed to `StateStreamTests` (file `git mv`'d too) since it directly
  tests the renamed type and had to be touched anyway to keep compiling; this is a bookkeeping
  rename, not new coverage.
- **`SnapshotChannel.cs`'s stray `using Basil.Server.Features.Multiplayer;`** (unused; the type it
  once needed is nowhere referenced in the file) was left as-is during the rename -- not part of this
  task's surgical scope, flagged here rather than silently cleaned up.
- **No slice adopts the hub in this task**, per the plan. `IMatchLiveEvents`, `MatchLiveEvents` and
  every `LiveSseRoutes`/`SseEndpoints` call site are untouched -- confirmed by the diff containing
  no changes to any of those files.
- Test placement: `tests/Basil.Application.Tests/Shared/Eventing/LiveEventHubTests.cs`, the same
  temporary-home pattern Task 0.8/0.9 used (`tests/Basil.Server.Tests` doesn't exist until Task
  0.11, done next in this same session). Namespace `Basil.Application.Tests.Shared.Eventing`,
  mirroring the eventual `Basil.Server.Tests.Shared.Eventing` home Task 0.11 gives it.
- Verification: `dotnet build --configuration Release` -- 0 errors (same 18 pre-existing warnings).
  Full suite: **1628/1628 passed, 0 failed, 0 skipped** on the first full run except the documented
  `BeatmapsetManagementEndpointTests.PutBeatmapset_Valid_ReplacesTheBeatmapsetsFilesAndReturns202`
  flake (500 vs 202); isolated re-run of that test class alone: 16/16 passed, confirming the known
  flake, not a regression. Arithmetic: 1625 (prior) + 3 (`LiveEventHubTests`) = 1628.

### Task 0.8 details (this session)
- **`LocaleCatalog`/`LocaleFragment`/`LocaleKey` built as three small types under
  `Shared/Localization/`**, per the plan: `LocaleKey.Flatten` walks a nested `JsonElement` into
  dotted `(key, value)` pairs; `LocaleFragment.Load` reads one file into a `FilePath` +
  `Entries` dictionary; `LocaleCatalog.Load` merges every fragment under
  `Data/Localization/*.json`, throwing (naming both files) on a duplicate key. Coverage test
  (`EveryReferencedKeyExistsAndEveryKeyIsReferenced`) written and watched to fail
  (`LocaleCatalog does not exist`) before any of the three types existed, per the plan's TDD step.
- **`LocaleTouch` (forces `MpReplies`/`IrcReplies` static init so the coverage test sees every
  referenced key) had to move out of `Shared/Localization/` into `Host/`, contradicting where the
  plan's own snippet implies it should live.** First attempt put it in
  `Shared/Localization/LocaleTouch.cs`; `dotnet test tests/Basil.ArchitectureTests` immediately
  caught it: `Shared_Should_Not_Reference_Features` failed with a new offender,
  `Basil.Server.Shared.Localization.LocaleTouch`, because the type necessarily names
  `MpReplies`/`IrcReplies` by type, which live in `Features/Bot`/`Features/Irc`. This is exactly the
  kind of cross-slice knowledge `Shared` is pinned against (see Task 0.4's decision on that test).
  Moved the file to `Host/LocaleTouch.cs` (namespace `Basil.Server.Host`, alongside
  `StartupData.cs`, which now calls `LocaleTouch.AllReplyHolders()` instead of touching the two
  members inline) -- `Host/` already legitimately depends on every slice as the composition root.
  **If a later task moves `LocaleTouch` back toward `Shared`, re-run
  `Shared_Should_Not_Reference_Features` first; it will fail again for the same reason.**
- **Key mapping applied exactly per the plan's Step 4, including one internally-inconsistent
  instruction, flagged rather than silently resolved**: BasilBot.json's 13 non-`Dispatch`/`General`
  categories (`Make`, `Join`, `In`, `Settings`, `Lock`, `Move`, `Name`, `Invite`, `Referee`, `Team`,
  `Map`, `Start`, `Moderation`) became `Commands.Mp.<Category>.<Member>`; `Dispatch` and `General`
  were both flattened into a single `General.<Member>` namespace (no `Commands.Mp` or category
  prefix), per the plan's literal "Dispatch/General to General.*". **This is inconsistent with
  design doc 4.5's own definition of `General.*` as "not command-related"** --
  `Dispatch`'s six members (`NotScopedToAnyMatchHint`, `UnknownMpSubcommand`, `CreatorOnlyMp`,
  `MpNotUsableFromLobby`, `MpChainNotUsableFromLobby`, `MpInDmOnly`) are genuinely `!mp`-dispatch
  errors, and `General`'s own members (`WhereUsage`, `FaqUsage`, `RollResult`, etc.) are
  command-specific usage/reply text for `!where`/`!faq`/`!roll`, not "general" in the sense 4.5
  describes. Followed the plan's literal instruction anyway (no member-name collision between the
  two categories, so the merge is mechanically safe) since Task 0.8 is explicitly a content-move-only
  task -- **flagged for whoever does Task 2.2 ("Localize Bot, Chat and IRC fragments"), which is
  positioned to give these a more coherent home.**
  Irc.json's 6 categories became `Irc.<Category>.<Member>`, matching design 4.5 exactly with no
  ambiguity.
- **Value-set verification, measured**: flattened both old files' values (154 total) and both new
  fragments' values (154 total) with a throwaway script, sorted, and diffed --
  **empty diff, exit 0**. No reply text changed; only key shape and file location did, as required.
- **Fragment delivery to test project output directories** verified directly, not assumed: after
  `dotnet build`, `bot.en.json`/`irc.en.json` are present under
  `tests/{Basil.Application.Tests,Basil.Infrastructure.Tests,Basil.IntegrationTests}/bin/Debug/net10.0/Data/Localization/`.
  Uses the same `<Content Update="...\*.json" Link="Data\Localization\%(Filename)%(Extension)">`
  pattern Task 0.3 already established for the old `Shared/Localization/*.json` glob -- replaced
  that glob with `Features\**\Locale\*.json` in `Basil.Server.csproj`. **Constraint this creates,
  not obvious from the csproj line alone: every slice's fragment filename must be unique across the
  whole tree, since the `Link` flattens `Features/<Slice>/Locale/<file>.json` down to
  `Data/Localization/<file>.json` with no slice subfolder.** Task 1.8 adding
  `Features/Multiplayer/Locale/multiplayer.en.json` is fine under this constraint; a future fragment
  reusing an existing filename would silently overwrite another slice's copy in the output directory
  (MSBuild would not error -- this is a real gap the build doesn't guard, unlike the key-duplicate
  guard `LocaleCatalog.Load` throws on).
- **Self-defending guards confirmed for whoever does Task 1.8** (moves the `Commands.Mp.*` keys out
  of `bot.en.json` into `Features/Multiplayer/Locale/multiplayer.en.json`): a copy-instead-of-move
  produces a duplicate key across the two fragments, and `LocaleCatalog.Load` throws loudly at
  startup/test time naming both files -- verified this throws by construction (`TryAdd` false path),
  not just by reading the code. Separately, a new or moved reply holder that isn't added to
  `Host/LocaleTouch.AllReplyHolders()` shows up as an **orphaned key** in
  `LocaleCatalogTests.EveryReferencedKeyExistsAndEveryKeyIsReferenced`, not a silent gap.
- **Pre-existing bug found, not fixed, flagged clearly**: `docker-compose.yml:11` bind-mounts
  `./src/Basil.Application/Data/Localization:/app/Data/Localization:ro`. `src/Basil.Application/`
  has not existed since Task 0.3 merged it into `Basil.Server` -- this mount source path was already
  dead before this task touched anything. Docker Compose creates a missing bind-mount source
  directory as empty on the host, so a real `docker compose up` today would mount an **empty**
  directory over `/app/Data/Localization`, shadowing the image's baked-in fragments entirely --
  exactly the failure mode `docs/for-technicians/docker.md:175` warns about ("a missing
  `Localization/` file specifically prevents Basil from starting at all"). This predates Task 0.8
  (it's a Task 0.2/0.3 rename that was never propagated to `docker-compose.yml`), is unrelated to
  the localization *content* restructuring this task did, and is out of this task's surgical scope
  to fix -- **flagged for whoever next touches Docker/deployment**: the fix is updating
  `docker-compose.yml:11` to `./src/Basil.Server/Features/Bot/Locale` and
  `./src/Basil.Server/Features/Irc/Locale` (two separate mounts, or a build step that stages both
  into one host directory first, since the fragments no longer live under one shared folder on
  disk the way `Shared/Localization/` did).
- `docs/for-technicians/{configuration,docker,deployment}.md` updated in this commit: filenames
  `BasilBot.json`/`Irc.json` -> `bot.en.json`/`irc.en.json` everywhere they appeared as literal
  paths (directory-tree diagrams, the settings-file table). Left the pre-existing stale
  `Basil.Web`/`Basil.Application` path-prefix staleness in the same files untouched -- unrelated to
  this task, predates it (Task 0.2/0.3), out of surgical scope. Left `docs/adr/ADR-007-*.md`
  entirely untouched -- it is a historical decision record, not a living reference.
- Verification: `dotnet build --configuration Release` -- 0 errors. Full suite: **1625/1625 passed,
  0 failed, 0 skipped**, no flake this run. Confirmed `Basil.ArchitectureTests` specifically both
  before the `LocaleTouch` relocation (7/8, the new offender) and after (8/8, clean).

### Task 0.9 details (this session)
- **The plan's Test 1 does not discriminate the fix, measured, not assumed.** Ran the plan's exact
  `EnvironmentVariablesDoNotOverrideApplicationSettings` test against the *unmodified* production
  code (before adding `Sources.Clear()`) and it already passed. Root cause, confirmed with a
  throwaway diagnostic dump of `builder.Configuration.Sources` before/after `Configure`:
  `WebApplication.CreateBuilder` inherits 12 sources, including three separate
  `EnvironmentVariablesConfigurationSource` registrations, but the pre-existing (unfixed)
  `ConfigurationSetup.Configure` appends `Data/appsettings.json` via `AddJsonFile` *after* returning
  from `CreateBuilder` -- so the appended file already won every precedence race regardless of
  whether the inherited sources were ever cleared. The design doc's claim that
  "`Basil__Server__Port` silently outranked the file" does not reproduce against the current
  (post-0.5/0.6) code; it was an ordering accident that already happened to resolve correctly, not
  an active bug. Advisor-reviewed decision: keep the `Sources.Clear()` fix anyway (single source of
  truth as a structural property of the source *list*, not of append order, is still the right
  design), but replace the non-discriminating verification with one that actually goes red before
  the fix: `Configure_ReplacesTheInheritedSourcesWithExactlyTheIntendedThree` asserts
  `builder.Configuration.Sources` is exactly `[JsonConfigurationSource("Data/appsettings.json"),
  JsonConfigurationSource("Data/appsettings.{Env}.json"), CommandLineConfigurationSource]` via
  `Assert.Collection` -- this fails at 13 sources pre-fix, passes at 3 post-fix. Kept the original
  `EnvironmentVariablesDoNotOverrideApplicationSettings` test too (it still pins the real
  user-visible contract, just wasn't sufficient alone), plus the plan's staging-environment test.
  Commit message states what was actually measured, not the plan's unverified claim.
- **Env var inventory checked before trusting `Sources.Clear()` is safe**:
  `grep -n "ASPNETCORE\|DOTNET_" docker-compose.yml Dockerfile .github/workflows/*.yml` returns only
  `docker-compose.yml:14: ASPNETCORE_ENVIRONMENT: Production`. No other `ASPNETCORE_*`/`DOTNET_*`
  variable is relied on anywhere in this repo's deployment surface, so clearing the inherited source
  list has no other host-configuration blind spot to worry about.
- **Test placement: `tests/Basil.Application.Tests/Host/ConfigurationSourceTests.cs`, not a new
  `Basil.Server.Tests` project.** `tests/Basil.Server.Tests` does not exist yet -- Task 0.11 ("Merge
  the test projects") creates it later, and creating it early would collide with that task's own
  `Basil.slnx` edit, which this worker was told not to start. `Basil.Application.Tests` already
  references `Basil.Server.csproj` and already owns a `Configurations/` folder of config-binding
  tests, so this is a natural, temporary home; Task 0.11's file-move sweeps it into
  `Basil.Server.Tests/Host/` for free. **Same placement decision applies to Task 0.8's coverage
  test.**
- **`ConfigurationSetup` is `internal`, so the test needs `InternalsVisibleTo`.** Added
  `<InternalsVisibleTo Include="Basil.Application.Tests"/>` to `Basil.Server.csproj` rather than
  making `ConfigurationSetup` public (rule 3/CLAUDE.md: don't widen production surface for test
  convenience). **Whoever does Task 0.11 must rename this entry to `Basil.Server.Tests` when the
  test project is renamed**, or the merged project loses access and the build breaks.
- Verification: `dotnet build --configuration Release` -- 0 errors. Full suite: **1624/1624 passed,
  0 failed, 0 skipped** (baseline 1621 + 3 new tests in `ConfigurationSourceTests`; no flake this
  run).
- `docs/for-technicians/configuration.md` updated in the same commit: "Configuration precedence"
  section now states the three sources explicitly (`appsettings.json` ->
  `appsettings.{Environment}.json` -> command-line args) and that environment variables configure
  the host (`ASPNETCORE_ENVIRONMENT`) only, not an individual Basil setting. Also fixed a now-false
  downstream claim in the same file ("prefer environment variables for deployment-specific values")
  that the source-chain fix made incorrect -- changed to recommend bind-mounting
  `appsettings.json`/an environment overlay instead, since env vars no longer reach Basil settings at
  all. Left the pre-existing stale `src/Basil.Web/Data/appsettings.json` link in the "Configuration
  file" section untouched -- unrelated staleness from Task 0.2's rename, out of this task's surgical
  scope.

## Remaining
- Tasks 0.10 through 0.14 (owned by later workers/phases)

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

## Task 0.6 planning notes (historical -- 0.6 is done, commit `a3e7b99`)
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

## Next exact step
Task 0.10 is complete and committed this session. Full suite: 1628/1628. The next step in this same
session is **Task 0.11** (merge the test projects) -- see its own entry below once done; if resuming
cold, read that task's section fresh. Two things worth carrying forward from before Task 0.10:
- The **pre-existing, unfixed `docker-compose.yml:11` dead bind-mount path** (see Task 0.8 details
  above) is real and will bite the first person who actually runs `docker compose up` on this
  branch. Not this phase's blocker, but worth a one-line fix whenever Docker is next touched.
- `Shared_Should_Not_Reference_Features` (see Task 0.4's decision and Task 0.8's `LocaleTouch`
  entry above) is a live tripwire, not just documentation -- any new `Shared/*` type that names a
  `Features/*` type by type will fail it immediately. Check this test first if a build error is
  confusing after adding something to `Shared/`.
