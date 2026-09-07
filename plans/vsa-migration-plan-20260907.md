# Basil Vertical Slice Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate Basil from Clean Architecture to Vertical Slices in one pass, folding in the model, eventing, localization, logging, configuration, User-slice, observability, and testing work the 2026 performance investigation surfaced, so no area of the codebase is refactored twice.

**Architecture:** Three source projects — `Basil.Domain` and `Basil.Protocol` unchanged as shared kernels, and `Basil.Server` (the renamed `Basil.Web`) absorbing `Basil.Application` and `Basil.Infrastructure`. Inside `Basil.Server`, `Features/<Slice>/` owns its endpoints, handlers, packet handlers, SQL, DI, metrics, locale fragment and help text; `Shared/` holds only cross-cutting infrastructure, policed by a fixed namespace-segment allowlist; `Host/` is the composition root. Cross-slice references are legal only when declared in an adjacency allowlist enforced by architecture tests. No mediator library — handlers are plain classes called directly by endpoints, packet handlers, and commands.

**Tech Stack:** .NET 10 (`net10.0`), ASP.NET Core Minimal APIs, Serilog, Dapper + Microsoft.Data.Sqlite 10.0.11 (SQLite 3.53.3), DbUp, `System.Diagnostics.Metrics`, `System.Net.ServerSentEvents`, xunit v3 (migrated from xunit 2.9.3), NSubstitute, NetArchTest, NBomber.

**Spec:** `plans/vsa-migration-design-20260907.md` (revision 3)
**Supporting input:** `plans/diagnostic-metric-inventory-20260907.md` — a verified, probe-backed metric inventory. Phase 5 implements against it row by row and does **not** re-investigate .NET metric availability.

---

## Global Constraints

* Target framework `net10.0`; `LangVersion` latest; nullable enabled; `EnforceCodeStyleInBuild` true. Do not change `Directory.Build.props` beyond what a task states.
* Central Package Management is on. Every version goes in `Directory.Packages.props`; `PackageReference` in a csproj carries no `Version`.
* **No new NuGet dependency** may be added except the xunit v3 packages named in Task 0.12. In particular: no mediator library, no OpenTelemetry exporter, no logging sink.
* Bancho packet bytes are a frozen contract. `tests/Basil.Protocol.Tests` must pass byte-identical at every commit. If a packet test fails, the change is wrong — never update the expected bytes.
* IRC wire text and numerics are equally frozen.
* The API response envelope contract (`docs/for-client/response-envelope.md`) does not change. The generated OpenAPI document may change only where a task says it changes.
* User-visible strings live in the localization system. A new hardcoded user-facing literal in production code is a review failure.
* Tabs, not spaces, for indentation in `.cs` files — match the surrounding file.
* Commit messages: Conventional Commits type prefix, imperative subject, body explaining *why*. Every commit ends with:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01FmxfGAtnYRQoE6M3kAFzmH
  ```
* Never run `git push`, open a PR, or force-push without being asked.
* Do not run the 12–24h soak. It is out of scope and blocks nothing.

---

## Execution Model

### Checkpoints — read this before starting any task

Nothing may depend on context that lives only inside an agent. Before your first task, create `plans/execution/` and the checkpoint files below. **Update the relevant checkpoint at every status transition, before any long-running operation, and at the end of every task — then commit it.**

```
plans/execution/orchestration.md          main-agent state
plans/execution/phase-0-foundation.md
plans/execution/phase-1-multiplayer.md
plans/execution/phase-2-chat-bot-irc.md
plans/execution/phase-3-users-auth.md
plans/execution/phase-4-beatmaps-scores.md
plans/execution/phase-5-diagnostics.md
plans/execution/phase-6-load-harness.md
plans/execution/phase-7-sweep.md
```

Phase checkpoint template — copy verbatim, fill in, never leave a heading empty:

```markdown
# Phase N: <name>

Status: Not started | Investigating | Implementing | Blocked | Ready for review | Done
Last updated: <ISO 8601 UTC>

## Completed
- Task N.x — <one line on what landed>, commit <sha>

## Current state
<where the work stands right now, in two or three sentences>

## Remaining
- Task N.y — <what is left>

## Important decisions
- <decision> — <why>

## Files changed
- <path or area>

## Verification
- <command run> — <result>

## Known issues / blockers
- <issue, or "none">

## Next exact step
<one concrete action, naming the file and the change>
```

`orchestration.md` additionally carries: every phase's status; which worktree path and branch each active stream uses; which dependency edges are still unsatisfied; which subagents are running and what they were asked for; and the main agent's next action.

**Resume contract:** read the checkpoint, read `git status` and `git log --oneline -20`, continue. A checkpoint that does not support that is not written well enough — fix it before moving on.

### Session-limit cycle

Sessions cap around five hours. Set up a recurring check that re-reads orchestration state and continues:

```
/loop 5h Read plans/execution/orchestration.md and plans/vsa-migration-plan-20260907.md, then continue the migration from the next exact step recorded there.
```

This is user-triggered. Do not assume a scheduler exists.

### Parallelism and worktrees

At most two streams run at once. The second runs in a worktree so neither blocks the other's build/test cycle:

```bash
git worktree add ../osuBasil-diagnostics -b feat/vsa-phase-5-diagnostics
```

Parallel pairs, in order of availability:
* Phase 1 (main) ‖ Phase 5 (worktree) — Phase 5 only needs Phase 0's hub.
* Phase 2 ‖ Phase 3 ‖ Phase 4 — only after Task 1.11's overlap re-check passes.

### Subagents

Spawn one only when it measurably shortens wall-clock time or reduces investigation risk. Two are planned, both Sonnet, both with a fixed output shape:

* **Metric inventory** — already complete. `plans/diagnostic-metric-inventory-20260907.md`. Do not re-run.
* **Test triage classification** (Task 1.9 / 3.7 / 4.5) — mechanical application of a stated criterion over ~1400 tests.

Architectural judgment is not delegated. It goes to the advisor checkpoints, which review the diff that exists rather than the plan that was written.

### Advisor checkpoints

After Phase 0, after Phase 1, after Phases 2–4 complete, and at Phase 7. Each review looks at the actual diff (`git diff <phase-start-sha>..HEAD --stat` plus the interesting files), not at this document.

---

## File Structure

### Target tree

```
src/
  Basil.Domain/                          unchanged
  Basil.Protocol/                        unchanged
  Basil.Server/                          renamed from Basil.Web (keeps its csproj)
    Host/
      Bootstrap.cs                       Main, pipeline order, host lifetime
      SerilogSetup.cs                    was Program.ConfigureSerilog
      KestrelSetup.cs                    was Program.ConfigureKestrel
      ConfigurationSetup.cs              was Program.ConfigureConfiguration (+ Sources.Clear)
      OpenApiSetup.cs                    was Program.ConfigureOpenApi + tag groups
      CorsSetup.cs                       was Program.ConfigureCors
      ImageSharpSetup.cs                 was Program.ConfigureImageSharp
      AuthSetup.cs                       was Program.ConfigureAuth
      JsonSetup.cs                       was Program.ConfigureJson
      StartupData.cs                     was Program.InitializeDataAsync
      StartupBanner.cs                   was Program.LogStartupBanner + BasilArt
      SliceRegistration.cs               enumerates every slice's AddXxx / MapXxx
    Features/
      Multiplayer/  Chat/  Bot/  Irc/  Users/  Auth/
      Beatmaps/  Scores/  Spectating/  Content/  Diagnostics/
    Shared/
      Eventing/  Sessions/  Persistence/  Localization/
      Logging/  Http/  Configuration/  Media/  Storage/
tests/
  Basil.Domain.Tests/                    unchanged in scope
  Basil.Protocol.Tests/                  unchanged in scope
  Basil.Server.Tests/                    Application.Tests + Infrastructure.Tests merged,
                                         laid out as Features/<Slice>/ + Shared/<Concern>/
  Basil.IntegrationTests/                unchanged in scope
  Basil.ArchitectureTests/               rewritten in Task 0.4
  Basil.LoadTests/                       reworked in Phase 6
```

### Where each existing area lands

| From | To |
| --- | --- |
| `Application/Sessions/Multiplayer/*`, `Application/Services/Multiplayer/*`, `Application/Packets/Multiplayer/*`, `Application/Backgrounds/MatchRoundEndOutbox.cs`, `Infrastructure/Sessions/InMemoryMatchRegistry.cs`, `Infrastructure/Sessions/MatchLiveEvents.cs`, `Infrastructure/Persistence/Repositories/SqliteMatchRepository.cs`, `Web/Routing/Api/Match*.cs`, `Web/Routing/Api/LiveSseRoutes.cs` (match streams) | `Features/Multiplayer/` |
| `Application/Services/Chat/*`, `Application/Sessions/Channels/*`, `Application/Packets/Channels/*`, `Infrastructure/Sessions/InMemoryChannelRegistry.cs`, `Infrastructure/Persistence/Repositories/SqliteChannelRepository.cs` | `Features/Chat/` |
| `Application/Services/Bot/*` | `Features/Bot/` |
| `Application/Services/Irc/*`, `Application/Sessions/Irc/*`, `Application/Sessions/IrcSession.cs`, `Infrastructure/Irc/*`, `Infrastructure/Sessions/IrcSessionRegistry.cs` | `Features/Irc/` |
| `Application/Services/Users/*`, `Application/Abstractions/Users/*`, `Application/Abstractions/Social/*`, `Infrastructure/Persistence/Repositories/SqliteUser*.cs`, `SqliteRelationshipRepository.cs`, `SqliteUserLogRepository.cs`, `SqliteClientHashRepository.cs`, `Infrastructure/Cache/CachingUserRepository.cs`, `Web/Routing/Api/UserRoutes.cs`, `Web/Routing/Bancho/AvatarRoutes.cs`, `Application/Packets/Users/*` | `Features/Users/` |
| `Application/Services/Authentication/*`, `Application/Services/Anticheat/*`, `Application/Abstractions/Login/*`, `Infrastructure/Security/*`, `Infrastructure/GuidTokenGenerator.cs`, `Infrastructure/Persistence/Repositories/SqliteIngameLoginRepository.cs`, `Web/Auth/*`, `Web/Routing/Api/AdminKeyRoutes.cs` | `Features/Auth/` |
| `Application/Services/Beatmaps/*`, `Application/Abstractions/Beatmaps/*`, `Infrastructure/Beatmaps/*`, `Infrastructure/Persistence/Repositories/SqliteBeatmap*.cs`, `Infrastructure/Cache/CachingBeatmap*.cs`, `Infrastructure/Performance/PpyOsuCalculator.cs`, `Web/Routing/Api/BeatmapsetRoutes.cs`, `Web/Routing/Assets/BeatmapsetAssetRoutes.cs`, `Web/Routing/Bancho/BeatmapAssetRoutes.cs` | `Features/Beatmaps/` |
| `Application/Services/Scores/*`, `Application/Abstractions/Scores/*`, `Infrastructure/Persistence/Repositories/SqliteScoreRepository.cs`, `SqliteLeaderboardStore.cs`, `Web/Routing/Api/ScoreRoutes.cs` | `Features/Scores/` |
| `Application/Services/Spectating/*`, `Application/Sessions/Spectating/*`, `Application/Packets/Spectating/*`, `Infrastructure/Sessions/PlayerInputEvents.cs`, `PlayerStatusEvents.cs` | `Features/Spectating/` |
| `Application/Services/Content/*`, `Application/Abstractions/Content/*`, `Application/Abstractions/Settings/*`, `Infrastructure/Persistence/Repositories/SqliteMenuBannerRepository.cs`, `SqliteSettingsRepository.cs`, `Infrastructure/Cache/CachingSettingsRepository.cs`, `Web/Routing/Api/Menu*.cs`, `FaqRoutes.cs`, `MotdSettingsRoutes.cs`, `MirrorSettingsRoutes.cs`, `AnnounceRoutes.cs`, `Web/Routing/Assets/MenuAssetRoutes.cs` | `Features/Content/` |
| *(new)* | `Features/Diagnostics/` |
| `Application/Services/{SnapshotChannel,SequenceGate,SseSubscriberRegistry,BoundedSseChannel}.cs`, `Application/Sessions/Multiplayer/IMatchLiveEvents.cs` | `Shared/Eventing/` |
| `Application/Sessions/{GameSession,UserSession,ISessionRegistry,PlayerLogoutService}.cs`, `Infrastructure/Sessions/GameSessionRegistry.cs`, `Application/Backgrounds/GhostDisconnectService.cs` | `Shared/Sessions/` |
| `Infrastructure/Persistence/{SqliteConnectionFactory,SqlMigrationRunner,SqliteInstrumentation,DatabaseConnectionStringBuilder}.cs`, `Migrations/*.sql` | `Shared/Persistence/` |
| `Application/Services/ReplyLocale.cs` | `Shared/Localization/` |
| `Web/Logging/*` | `Shared/Logging/` |
| `Web/Middleware/*`, `Web/OpenApi/*`, `Web/Routing/{ContentTypes,NumericIdRouteConstraint}.cs`, `Routing/Api/{Pagination,RouteDocs,ApiHostRoutes,AbbreviationRedirectRoutes}.cs`, `Routing/Bancho/BanchoHostGroups.cs`, `Application/Formats/*`, `Application/Services/Multiplayer/JsonMergePatch.cs` | `Shared/Http/` |
| `Application/Configurations/*` | `Shared/Configuration/` |
| `Infrastructure/Media/*` | `Shared/Media/` |
| `Infrastructure/Storage/*`, `Infrastructure/System/HardLink.cs`, `Application/Abstractions/Storage/*` | `Shared/Storage/` |
| `Application/Diagnostics/BasilMetrics.cs` | split — see Task 0.7 |
| `Application/Packets/{IPacketHandler,PacketDispatcher,PacketBuilders}.cs` | `Shared/Http/Bancho/` |

`Application/Abstractions/*` interfaces move next to their single implementation inside the owning slice; the interface file survives only where a second implementation exists (the four `Infrastructure/Cache/Caching*Repository` decorators).

---

# Phase 0 — Foundation

Blocks everything. Sequential. Ends with an advisor checkpoint.

Branch: work on `chore/perf-investigation` (current branch) or a new `feat/vsa-migration` cut from it — record the choice in `orchestration.md`.

### Task 0.1: Capture the pre-migration baseline

Phase 0 changes project layout, namespaces, test projects, the test framework, DI, routing, metrics, localization loading and the User contract at once. "The suite is green" is not sufficient evidence that behavior is unchanged — a suite that was restructured cannot vouch for itself. Capture externally-observable artifacts first, then diff against them.

**Files:**
- Create: `plans/execution/baseline/` (committed)
- Create: `plans/execution/phase-0-foundation.md`

- [ ] **Step 1: Create the checkpoint files**

Create `plans/execution/` and all nine checkpoint files from the template in the Execution Model section. Set every phase to `Not started` except Phase 0, which is `Implementing`.

- [ ] **Step 2: Record the baseline commit**

```bash
git rev-parse HEAD > plans/execution/baseline/base-sha.txt
```

- [ ] **Step 3: Capture build and test baselines**

```bash
dotnet build --configuration Release 2>&1 | tail -5 > plans/execution/baseline/build.txt
dotnet test --configuration Release --logger "trx;LogFileName=baseline.trx" 2>&1 \
  | tail -40 > plans/execution/baseline/test-summary.txt
```

Record total/passed/failed/skipped counts in `test-summary.txt`. This number is the oracle for Task 0.13.

- [ ] **Step 4: Capture the generated OpenAPI document**

`Microsoft.Extensions.ApiDescription.Server` is already referenced, so the document is emitted at build. Locate it and copy it:

```bash
find src/Basil.Web/obj -name '*.json' -path '*ApiDescription*' -o -name 'Basil.Web.json' | head
cp <the emitted document> plans/execution/baseline/openapi.json
```

If the build does not emit one, run the server's `/openapi/v1.json` once and save that instead. Record in the checkpoint which method was used.

- [ ] **Step 5: Capture the database schema**

Run the migration runner against a scratch database and dump the resulting schema:

```bash
dotnet run --project src/Basil.Web -- --Basil:Storage:DataPath=<scratch> &   # stop once migrated
```

Then export `sqlite_master`:

```csharp
// plans/execution/baseline/dump-schema.csx equivalent — a throwaway console under the scratchpad
// SELECT type, name, sql FROM sqlite_master ORDER BY type, name;
```

Save as `plans/execution/baseline/schema.txt`, sorted, one object per line.

- [ ] **Step 6: Capture the metric and route inventories**

```bash
grep -rhoE '"basil\.[a-z_.]+"' src --include=*.cs | sort -u > plans/execution/baseline/metrics.txt
grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Web --include=*.cs \
  | sort -u > plans/execution/baseline/routes.txt
```

- [ ] **Step 7: Capture the localization key inventory**

```bash
python -c "
import json
for f in ('BasilBot','Irc'):
    d=json.load(open(f'src/Basil.Application/Data/Localization/{f}.json'))
    for cat,members in d.items():
        for m in members: print(f'{f}.{cat}.{m}')
" | sort > plans/execution/baseline/locale-keys.txt
```

- [ ] **Step 8: Commit**

```bash
git add plans/execution
git commit -m "chore: capture the pre-migration baseline

Phase 0 restructures projects, namespaces, test projects, the test framework,
DI, routing, metrics, localization loading and the User contract in one pass. A
restructured test suite cannot vouch for itself, so the externally observable
artifacts -- OpenAPI document, database schema, metric names, route table and
localization keys -- are captured here and diffed after the move."
```

---

### Task 0.2: Rename `Basil.Web` to `Basil.Server`

Renaming preserves the csproj's publish target, embedded avatars, content items, app manifest and icon. Creating a new project would mean re-deriving all of it.

**Files:**
- Rename: `src/Basil.Web/` → `src/Basil.Server/`, `Basil.Web.csproj` → `Basil.Server.csproj`
- Modify: `Basil.slnx`, `Dockerfile`, `docker-compose.yml`, every test csproj's `ProjectReference`

**Interfaces:**
- Produces: assembly and root namespace `Basil.Server`.

- [ ] **Step 1: Move the directory and the project file**

```bash
git mv src/Basil.Web src/Basil.Server
git mv src/Basil.Server/Basil.Web.csproj src/Basil.Server/Basil.Server.csproj
```

- [ ] **Step 2: Update the solution**

In `Basil.slnx`, replace `src/Basil.Web/Basil.Web.csproj` with `src/Basil.Server/Basil.Server.csproj`.

- [ ] **Step 3: Update namespaces**

```bash
grep -rl 'Basil\.Web' src tests --include=*.cs | xargs sed -i 's/Basil\.Web/Basil.Server/g'
grep -rl 'Basil\.Web' Dockerfile docker-compose.yml .github 2>/dev/null \
  | xargs -r sed -i 's/Basil\.Web/Basil.Server/g'
```

- [ ] **Step 4: Update project references**

In `tests/Basil.IntegrationTests/*.csproj` and any other csproj referencing `Basil.Web.csproj`, point at `..\..\src\Basil.Server\Basil.Server.csproj`.

- [ ] **Step 5: Build and test**

Run: `dotnet build --configuration Release`
Expected: succeeds.
Run: `dotnet test`
Expected: same pass count as `plans/execution/baseline/test-summary.txt`.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: rename Basil.Web to Basil.Server

The host project becomes the single application assembly that Application and
Infrastructure merge into. Renaming rather than creating a new project keeps the
publish target that strips unused osu! runtime files, the embedded avatars, the
content items, the app manifest and the icon."
```

---

### Task 0.3: Merge `Basil.Application` and `Basil.Infrastructure` into `Basil.Server`

One atomic mechanical move. A partial move leaves circular assembly references, so this cannot be split.

**Files:**
- Move: every `.cs` under `src/Basil.Application/` and `src/Basil.Infrastructure/` into `src/Basil.Server/Features/*` or `src/Basil.Server/Shared/*` per the File Structure table
- Move: `src/Basil.Application/Data/Localization/*.json`, `src/Basil.Infrastructure/Persistence/Migrations/*.sql`
- Delete: `src/Basil.Application/`, `src/Basil.Infrastructure/`
- Modify: `Basil.slnx`, `src/Basil.Server/Basil.Server.csproj`

- [ ] **Step 1: Move files with `git mv`, following the File Structure table**

Use `git mv` so history follows. Work slice by slice within the single commit. Example for one slice:

```bash
mkdir -p src/Basil.Server/Features/Multiplayer
git mv src/Basil.Application/Services/Multiplayer/* src/Basil.Server/Features/Multiplayer/
git mv src/Basil.Application/Sessions/Multiplayer/* src/Basil.Server/Features/Multiplayer/
git mv src/Basil.Application/Packets/Multiplayer src/Basil.Server/Features/Multiplayer/Packets
git mv src/Basil.Infrastructure/Sessions/InMemoryMatchRegistry.cs src/Basil.Server/Features/Multiplayer/
git mv src/Basil.Infrastructure/Sessions/MatchLiveEvents.cs src/Basil.Server/Features/Multiplayer/
git mv src/Basil.Infrastructure/Persistence/Repositories/SqliteMatchRepository.cs src/Basil.Server/Features/Multiplayer/
git mv src/Basil.Server/Routing/Api/MatchRoutes.cs src/Basil.Server/Features/Multiplayer/
git mv src/Basil.Server/Routing/Api/MatchSubResourceRoutes.cs src/Basil.Server/Features/Multiplayer/
```

Repeat for every row of the table.

- [ ] **Step 2: Rewrite namespaces**

```bash
grep -rl 'Basil\.Application' src tests --include=*.cs | xargs sed -i 's/Basil\.Application/Basil.Server/g'
grep -rl 'Basil\.Infrastructure' src tests --include=*.cs | xargs sed -i 's/Basil\.Infrastructure/Basil.Server/g'
```

Then set each moved file's `namespace` declaration to match its new folder, e.g. `namespace Basil.Server.Features.Multiplayer;`. Fix the resulting `using` errors by compiling and following the diagnostics — do not add blanket global usings to paper over them.

- [ ] **Step 3: Fold the csproj contents**

Copy every `PackageReference` from `Basil.Application.csproj` and `Basil.Infrastructure.csproj` into `Basil.Server.csproj`, and add the `EmbeddedResource` item for the SQL migrations that `Basil.Infrastructure.csproj` carried (`SqlMigrationRunner` loads them with `Assembly.GetExecutingAssembly()`). Add the localization JSON as content copied to output under `Data/Localization/`.

Remove the two `ProjectReference` entries to the deleted projects; keep `Basil.Domain` and `Basil.Protocol`.

- [ ] **Step 4: Delete the empty projects and update the solution**

```bash
git rm -r src/Basil.Application src/Basil.Infrastructure
```

Remove both `<Project Path=...>` lines from `Basil.slnx`.

- [ ] **Step 5: Point the test projects at `Basil.Server`**

`tests/Basil.Application.Tests` and `tests/Basil.Infrastructure.Tests` reference the deleted projects. Point both at `..\..\src\Basil.Server\Basil.Server.csproj` for now; they merge in Task 0.11.

- [ ] **Step 6: Build**

Run: `dotnet build --configuration Release`
Expected: succeeds. Fix compile errors by correcting namespaces and usings only — no behavior change belongs in this task.

- [ ] **Step 7: Test**

Run: `dotnet test`
Expected: identical pass/fail/skip counts to the baseline. Any difference is a bug introduced by the move; find it before continuing.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: merge Application and Infrastructure into Basil.Server as slices

A vertical slice owns its endpoint, its handler and its persistence. The old
split forced every repository into an interface in Application and one
implementation in Infrastructure purely because of the layer boundary, which is
the abstraction this migration exists to remove.

Mechanical move only: no behavior, no signature and no SQL changes here. The
test suite reports the same counts as the pre-migration baseline."
```

---

### Task 0.4: Rewrite the architecture tests

The nine existing tests assert the old direction and are false the moment Task 0.3 lands. They are replaced, not deleted.

**Files:**
- Modify: `tests/Basil.ArchitectureTests/DependencyDirectionTests.cs`
- Create: `tests/Basil.ArchitectureTests/SliceBoundaryTests.cs`
- Create: `tests/Basil.ArchitectureTests/SliceAdjacency.cs`

**Interfaces:**
- Produces: `SliceAdjacency.Allowed` — the declared cross-slice edge list every later phase must keep honest.

- [ ] **Step 1: Write the adjacency allowlist**

```csharp
namespace Basil.ArchitectureTests;

/// <summary>
///     The cross-slice references this codebase permits, each one a deliberate decision.
/// </summary>
/// <remarks>
///     Slices are not forbidden from referencing each other — Multiplayer genuinely needs to
///     resolve usernames, Scores genuinely needs to resolve beatmaps — because a ban would be
///     unenforceable and blanket permission would make the rule meaningless. Every edge is named
///     here instead, so an undeclared one fails the build and a growing list is a visible signal
///     that a boundary is wrong.
/// </remarks>
internal static class SliceAdjacency
{
	public static readonly (string From, string To)[] Allowed =
	[
		("Multiplayer", "Users"),
		("Multiplayer", "Chat"),
		("Multiplayer", "Beatmaps"),
		("Multiplayer", "Bot"),
		("Bot", "Multiplayer"),
		("Bot", "Users"),
		("Bot", "Chat"),
		("Bot", "Content"),
		("Chat", "Users"),
		("Chat", "Bot"),
		("Irc", "Users"),
		("Irc", "Chat"),
		("Irc", "Auth"),
		("Auth", "Users"),
		("Scores", "Beatmaps"),
		("Scores", "Users"),
		("Scores", "Multiplayer"),
		("Spectating", "Users"),
		("Beatmaps", "Content")
	];
}
```

Trim this list to what the build actually requires after Task 0.3 — start from the real compile errors, not from this draft. Every surviving edge must be one someone would defend.

- [ ] **Step 2: Write the failing slice-isolation test**

```csharp
[Fact]
public void Slices_Should_Only_Reference_Declared_Slices()
{
	var assembly = typeof(Server.Host.Bootstrap).Assembly;
	const string prefix = "Basil.Server.Features.";
	var violations = new List<string>();

	foreach (var type in Types.InAssembly(assembly).GetTypes()
		         .Where(t => t.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true))
	{
		var from = type.Namespace![prefix.Length..].Split('.')[0];
		var result = Types.InAssembly(assembly)
			.That().HaveName(type.Name).And().ResideInNamespace(type.Namespace)
			.Should().NotHaveDependencyOnAny(
				OtherSlices(assembly, from, SliceAdjacency.Allowed))
			.GetResult();

		if (!result.IsSuccessful)
			violations.Add($"{type.FullName} -> {string.Join(", ", result.FailingTypeNames ?? [])}");
	}

	Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
}
```

`OtherSlices` returns `Basil.Server.Features.<X>` for every slice `X` that is neither `from` nor an allowed target of `from`.

- [ ] **Step 3: Write the `Shared` rules**

```csharp
[Fact]
public void Shared_Should_Not_Reference_Features()
{
	var result = Types.InAssembly(typeof(Server.Host.Bootstrap).Assembly)
		.That().ResideInNamespaceStartingWith("Basil.Server.Shared")
		.Should().NotHaveDependencyOn("Basil.Server.Features")
		.GetResult();

	Assert.True(result.IsSuccessful, FailureMessage(result));
}

/// <summary>
///     Shared may only contain these concerns. The cross-slice allowlist creates an obvious escape
///     hatch: when an edge is inconvenient to declare, the temptation is to move the code into
///     Shared instead, which trades dependency spaghetti for a shared god layer. A fixed segment
///     list makes Shared/Users/ or Shared/Matches/ a build failure, so adding a genuinely new
///     cross-cutting concern is a visible decision rather than a drift.
/// </summary>
[Fact]
public void Shared_Segments_Should_Come_From_The_Allowlist()
{
	string[] allowed =
	[
		"Eventing", "Sessions", "Persistence", "Localization",
		"Logging", "Http", "Configuration", "Media", "Storage"
	];

	const string prefix = "Basil.Server.Shared.";
	var offenders = Types.InAssembly(typeof(Server.Host.Bootstrap).Assembly).GetTypes()
		.Select(t => t.Namespace)
		.Where(n => n?.StartsWith(prefix, StringComparison.Ordinal) == true)
		.Select(n => n![prefix.Length..].Split('.')[0])
		.Distinct()
		.Where(segment => !allowed.Contains(segment))
		.ToArray();

	Assert.True(offenders.Length == 0,
		$"Shared gained an unapproved concern: {string.Join(", ", offenders)}");
}
```

- [ ] **Step 4: Keep the kernel-purity tests, delete the layer tests**

Keep `Domain_Should_Not_HaveDependencyOn_Infrastructure`-style assertions rewritten as: `Basil.Domain` and `Basil.Protocol` reference no other Basil assembly and no web/ORM/SQLite assembly. Delete every test naming `Basil.Application` or `Basil.Infrastructure`.

- [ ] **Step 5: Run**

Run: `dotnet test tests/Basil.ArchitectureTests`
Expected: PASS. If the slice test fails, either the edge is legitimate (add it to `SliceAdjacency.Allowed` with a comment saying why) or the file is in the wrong slice (move it) — never widen the rule to silence it.

- [ ] **Step 6: Commit**

```bash
git add tests/Basil.ArchitectureTests
git commit -m "test(arch): enforce slice boundaries instead of layer direction

The old assertions describe a dependency direction that no longer exists. The
replacements enforce what the new architecture actually promises: kernel purity
for Domain and Protocol, Shared never reaching into Features, cross-slice edges
only where declared, and a fixed segment allowlist for Shared so the adjacency
list cannot be escaped by pushing feature code into a shared god layer."
```

---

### Task 0.5: Split `Program.cs` into `Host/`

**Files:**
- Create: `src/Basil.Server/Host/{Bootstrap,SerilogSetup,KestrelSetup,ConfigurationSetup,OpenApiSetup,CorsSetup,ImageSharpSetup,AuthSetup,JsonSetup,StartupData,StartupBanner,SliceRegistration}.cs`
- Delete: `src/Basil.Server/Program.cs`
- Modify: `tests/Basil.IntegrationTests/*` — `WebApplicationFactory<Program>` becomes `WebApplicationFactory<Bootstrap>`

- [ ] **Step 1: Move each `Program` member to its file**

One private static method per file, made `internal static`, keeping its XML documentation verbatim. `BasilArt`, `BasilDescription`, `BasilLicense` and `BasilApiTagGroups` go to `StartupBanner.cs` and `OpenApiSetup.cs` respectively.

- [ ] **Step 2: Write `Bootstrap.cs`**

```csharp
namespace Basil.Server.Host;

public sealed class Bootstrap
{
	public static async Task Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		SerilogSetup.Configure(builder);
		ConfigurationSetup.Configure(builder, args);
		KestrelSetup.Configure(builder);
		SliceRegistration.AddAll(builder);
		JsonSetup.Configure(builder);
		OpenApiSetup.Configure(builder);
		AuthSetup.Configure(builder);
		CorsSetup.Configure(builder);
		ImageSharpSetup.Configure(builder);

		var app = builder.Build();
		StartupBanner.Log(app);

		// Order matters: authentication/authorization must run before EnvelopeMiddleware (which
		// needs the resolved role to decide what a response reveals), which must run before
		// ApiRequestLoggingMiddleware (which logs the final status code). RequestMetricsMiddleware
		// runs first so its measured duration includes every other middleware's cost.
		app.UseMiddleware<RequestMetricsMiddleware>();
		app.UseMiddleware<RequestIdLoggingMiddleware>();
		app.UseMiddleware<ExceptionLoggingMiddleware>();
		app.UseWebSockets();
		app.UseCors(CorsSetup.PolicyName);
		app.UseAuthentication();
		app.UseAuthorization();
		app.UseMiddleware<EnvelopeMiddleware>();
		app.UseMiddleware<ApiRequestLoggingMiddleware>();
		app.UseImageSharp();

		SliceRegistration.MapAll(app);

		var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
		var hostLogger = app.Services.GetRequiredService<ILogger<Bootstrap>>();
		lifetime.ApplicationStopping.Register(() => hostLogger.LogInformation("Server shutting down"));

		await StartupData.InitializeAsync(app);
		await app.RunAsync();
	}
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build --configuration Release && dotnet test`
Expected: baseline counts.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(host): split Program.cs by responsibility

816 lines covering Serilog, Kestrel, configuration, OpenAPI, CORS, ImageSharp,
auth, JSON, startup data and the banner was the clearest instance of the
god-file problem this migration exists to fix. Each concern becomes one file
under Host/, and the pipeline order comment stays with the pipeline."
```

---

### Task 0.6: DI and routing seams

Both `DependencyInjection.cs` files and `BanchoHostGroups.MapAll` are edited by every slice, which is what would make later phases collide. One registration surface per slice removes the collision.

**Files:**
- Create: `src/Basil.Server/Features/<Slice>/<Slice>ServiceCollectionExtensions.cs` (11 files)
- Create: `src/Basil.Server/Host/SliceRegistration.cs`
- Delete: the two former `DependencyInjection.cs` files
- Modify: `src/Basil.Server/Shared/Http/BanchoHostGroups.cs`

**Interfaces:**
- Produces: `IServiceCollection AddMultiplayer(this IServiceCollection, IConfiguration)` and one peer per slice; `void MapMultiplayerRoutes(this IEndpointRouteBuilder)` and one peer per slice.

- [ ] **Step 1: Write one extension class per slice**

```csharp
namespace Basil.Server.Features.Multiplayer;

/// <summary>Registers the multiplayer slice's services and endpoints.</summary>
public static class MultiplayerServiceCollectionExtensions
{
	public static IServiceCollection AddMultiplayer(this IServiceCollection services,
		IConfiguration configuration)
	{
		services.AddSingleton<IMatchRegistry, InMemoryMatchRegistry>();
		services.AddSingleton<IMatchLiveEvents, MatchLiveEvents>();
		services.AddScoped<IMatchRepository, SqliteMatchRepository>();
		services.AddScoped<MatchMembershipService>();
		services.AddScoped<MatchControlService>();
		services.AddScoped<MatchReportService>();
		services.AddScoped<MatchRecoveryService>();
		services.AddHostedService<MatchRoundEndOutbox>();
		return services;
	}
}
```

Move each registration from the deleted `DependencyInjection.cs` files into the slice that owns the type. Registrations for `Shared` types go into `Host/SliceRegistration.cs` under an `AddSharedInfrastructure` method.

- [ ] **Step 2: Write `SliceRegistration.cs`**

```csharp
namespace Basil.Server.Host;

/// <summary>The one place that names every slice, so adding a slice is a two-line change here.</summary>
internal static class SliceRegistration
{
	public static void AddAll(WebApplicationBuilder builder)
	{
		builder.Services.AddSharedInfrastructure(builder.Configuration);
		builder.Services.AddAuth(builder.Configuration);
		builder.Services.AddUsers(builder.Configuration);
		builder.Services.AddChat(builder.Configuration);
		builder.Services.AddBot(builder.Configuration);
		builder.Services.AddIrc(builder.Configuration);
		builder.Services.AddMultiplayer(builder.Configuration);
		builder.Services.AddBeatmaps(builder.Configuration);
		builder.Services.AddScores(builder.Configuration);
		builder.Services.AddSpectating(builder.Configuration);
		builder.Services.AddContent(builder.Configuration);
		builder.Services.AddDiagnostics(builder.Configuration);
	}

	public static void MapAll(WebApplication app)
	{
		var domain = app.Configuration.GetSection(ServerOptions.SectionName)["Domain"] ?? "localhost";
		var hosts = BanchoHostGroups.Create(app, domain);

		hosts.Api.MapUserRoutes();
		hosts.Api.MapMultiplayerRoutes();
		// ... one line per slice per host group
	}
}
```

`BanchoHostGroups` keeps host-group construction and loses the per-slice `MapXxx` calls it currently performs.

- [ ] **Step 3: Build and test**

Run: `dotnet build --configuration Release && dotnet test`
Expected: baseline counts.

- [ ] **Step 4: Verify the route table is unchanged**

```bash
grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Server --include=*.cs | sort -u > /tmp/routes-now.txt
diff plans/execution/baseline/routes.txt /tmp/routes-now.txt
```

Expected: no differences.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(di): give every slice its own registration and routing surface

Two central DependencyInjection files and one MapAll were written by every
slice, which is exactly the shared surface that would have made the later
per-slice phases collide. Each slice now owns AddXxx and MapXxxRoutes, and
Host/SliceRegistration is the single list that names them."
```

---

### Task 0.7: Split `BasilMetrics` per slice

**Files:**
- Create: `src/Basil.Server/Shared/Http/HttpMetrics.cs`, `Shared/Persistence/PersistenceMetrics.cs`, `Shared/Eventing/EventingMetrics.cs`, `Features/Multiplayer/MultiplayerMetrics.cs`
- Create: `src/Basil.Server/Shared/BasilMeter.cs`
- Delete: `src/Basil.Server/Shared/BasilMetrics.cs`

- [ ] **Step 1: Extract the shared meter**

```csharp
namespace Basil.Server.Shared;

/// <summary>The single meter every Basil instrument is published under.</summary>
/// <remarks>
///     Deliberately not a full OpenTelemetry setup: no exporter is wired anywhere. A host that
///     wants the data attaches a listener — <c>dotnet-counters monitor --process-id &lt;pid&gt;
///     Basil</c>, or the diagnostic slice's own listener.
/// </remarks>
public static class BasilMeter
{
	public const string Name = "Basil";
	public static readonly Meter Instance = new(Name);
}
```

- [ ] **Step 2: Move each instrument to its owner, keeping its name string byte-identical**

`RequestDurationMs` → `HttpMetrics`; `DbCommandDurationMs`, `DbBusyCount` → `PersistenceMetrics`; `SseActiveSubscribers`, `SseBacklogDepth`, `StalePublishDropped` → `EventingMetrics`; `MatchLockWaitMs` → `MultiplayerMetrics`.

- [ ] **Step 3: Verify no metric name changed**

```bash
grep -rhoE '"basil\.[a-z_.]+"' src --include=*.cs | sort -u > /tmp/metrics-now.txt
diff plans/execution/baseline/metrics.txt /tmp/metrics-now.txt
```

Expected: no differences. A renamed instrument breaks every existing dashboard and every load-test report.

- [ ] **Step 4: Build, test, commit**

```bash
dotnet build --configuration Release && dotnet test
git add -A
git commit -m "refactor(metrics): move each instrument to the slice that emits it

BasilMetrics was a file every slice appended to. The meter name is unchanged, so
nothing consuming basil.* metrics is affected."
```

---

### Task 0.8: Localization loader and per-slice fragments

Splits the two central JSON files so each slice edits only its own fragment. **No wording changes here** — content migration happens inside the slice phases.

**Files:**
- Create: `src/Basil.Server/Shared/Localization/{LocaleCatalog,LocaleFragment,LocaleKey}.cs`
- Create: `src/Basil.Server/Features/<Slice>/Locale/<slice>.en.json` (one per slice that has text)
- Delete: `src/Basil.Server/Shared/Localization/ReplyLocale.cs`, `Data/Localization/{BasilBot,Irc}.json`
- Modify: `Basil.Server.csproj` — glob `Features/**/Locale/*.json` into `Data/Localization/`

**Interfaces:**
- Produces: `LocaleCatalog.Get(string key)` — resolves a dotted hierarchical key such as `Commands.Mp.In.NotScopedToAnyMatch`; throws at startup if missing.
- Produces: `LocaleCatalog.AllKeys` — every key the merged catalog holds, for the coverage test.
- Produces: `LocaleCatalog.ReferencedKeys` — every key production code asked for, recorded at first resolution.

- [ ] **Step 1: Write the failing coverage test**

```csharp
[Fact]
public void EveryReferencedKeyExistsAndEveryKeyIsReferenced()
{
	// Touching every reply-constant holder forces its static initializer, which is what
	// registers the keys production code actually asks for.
	LocaleTouch.AllReplyHolders();

	var missing = LocaleCatalog.ReferencedKeys.Except(LocaleCatalog.AllKeys).Order().ToArray();
	var orphaned = LocaleCatalog.AllKeys.Except(LocaleCatalog.ReferencedKeys).Order().ToArray();

	Assert.True(missing.Length == 0, $"locale is missing: {string.Join(", ", missing)}");
	Assert.True(orphaned.Length == 0, $"locale has unused keys: {string.Join(", ", orphaned)}");
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Basil.Server.Tests --filter "FullyQualifiedName~EveryReferencedKey"`
Expected: FAIL — `LocaleCatalog` does not exist.

- [ ] **Step 3: Write `LocaleCatalog`**

```csharp
namespace Basil.Server.Shared.Localization;

/// <summary>
///     The merged, resolved locale for the running server: every slice's fragment loaded once and
///     addressed by a dotted key that mirrors the command hierarchy it belongs to.
/// </summary>
/// <remarks>
///     Fragments live beside the slice that owns their text, so a slice's wording and its code
///     change together. A key that no fragment supplies throws the first time it is resolved;
///     startup touches every reply-constant holder so that failure surfaces at boot rather than
///     mid-request.
/// </remarks>
public static class LocaleCatalog
{
	private static readonly Lazy<FrozenDictionary<string, string>> Entries = new(Load);
	private static readonly ConcurrentDictionary<string, byte> Referenced = new();

	public static string Get(string key)
	{
		Referenced[key] = 0;
		if (Entries.Value.TryGetValue(key, out var value)) return value;
		throw new InvalidOperationException($"The active locale is missing '{key}'.");
	}

	public static IReadOnlyCollection<string> AllKeys => Entries.Value.Keys;
	public static IReadOnlyCollection<string> ReferencedKeys => (IReadOnlyCollection<string>)Referenced.Keys;

	private static FrozenDictionary<string, string> Load()
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "Data", "Localization");
		var merged = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
		foreach (var (key, value) in Flatten(JsonDocument.Parse(File.ReadAllBytes(file)).RootElement))
			if (!merged.TryAdd(key, value))
				throw new InvalidOperationException(
					$"Two locale fragments both define '{key}'; the last one found was in {file}.");

		return merged.ToFrozenDictionary(StringComparer.Ordinal);
	}
}
```

`Flatten` walks nested objects, joining property names with `.`, so a fragment is written as a natural nested document rather than as flat dotted strings.

- [ ] **Step 4: Split the existing JSON into fragments, key-for-key**

`BasilBot.json`'s 15 categories map to `Commands.Mp.<Category>` under `Features/Bot/Locale/bot.en.json` (with the `!mp` ones moving to `Features/Multiplayer/Locale/multiplayer.en.json` when Phase 1 takes them), `Dispatch`/`General` to `General.*`. `Irc.json` becomes `Features/Irc/Locale/irc.en.json` under `Irc.*`. Rewrite `MpReplies` and `IrcReplies` to call `LocaleCatalog.Get` with the new keys.

- [ ] **Step 5: Verify no key was lost**

```bash
python -c "<flatten every Features/**/Locale/*.json and print sorted keys>" > /tmp/locale-now.txt
```

Compare the *count* and the *value set* against `plans/execution/baseline/locale-keys.txt`. Key names change shape by design; **no reply text may change**. Diff the values, not the keys.

- [ ] **Step 6: Run the coverage test, build, test, commit**

Run: `dotnet test`
Expected: baseline counts, plus the new coverage test passing.

```bash
git add -A
git commit -m "feat(localization): merge per-slice fragments through one catalog

Two central JSON files were written by every slice. Each slice now ships the
fragment for its own text, keys are hierarchical and mirror the command they
belong to, and a coverage test asserts that every referenced key exists and no
key is unused -- which is what makes 'switch the whole system to a new locale'
a property CI can enforce rather than a claim.

No reply wording changes here; only where the text lives and how it is addressed."
```

---

### Task 0.9: Configuration source chain

**Files:**
- Modify: `src/Basil.Server/Host/ConfigurationSetup.cs`
- Create: `tests/Basil.Server.Tests/Host/ConfigurationSourceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
/// <summary>
///     Application settings come from Data/appsettings.json, and nothing else may quietly
///     override them.
/// </summary>
/// <remarks>
///     WebApplication.CreateBuilder registers the environment-variable provider by default, so
///     before this was fixed a Basil__Server__Port variable silently outranked the file with
///     nothing in the codebase referencing an environment variable at all.
/// </remarks>
[Fact]
public void EnvironmentVariablesDoNotOverrideApplicationSettings()
{
	Environment.SetEnvironmentVariable("Basil__Server__Port", "59999");
	try
	{
		var builder = WebApplication.CreateBuilder([]);
		ConfigurationSetup.Configure(builder, []);

		var port = builder.Configuration.GetSection(ServerOptions.SectionName).GetValue<int?>("Port");

		Assert.NotEqual(59999, port);
	}
	finally
	{
		Environment.SetEnvironmentVariable("Basil__Server__Port", null);
	}
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Basil.Server.Tests --filter "FullyQualifiedName~EnvironmentVariablesDoNotOverride"`
Expected: FAIL — the value is 59999.

- [ ] **Step 3: Clear the inherited sources**

```csharp
internal static void Configure(WebApplicationBuilder builder, string[] args)
{
	// CreateBuilder installs the environment-variable provider before this runs. Application
	// settings have one source of truth, so the inherited set is dropped entirely and only the
	// intended layers are re-added. builder.Environment resolved EnvironmentName from
	// ASPNETCORE_ENVIRONMENT while the builder was being constructed -- that is host
	// configuration, which docker-compose relies on, and clearing the list here does not disturb it.
	builder.Configuration.Sources.Clear();
	builder.Configuration
		.AddJsonFile(Path.Combine("Data", "appsettings.json"), false, true)
		.AddJsonFile(Path.Combine("Data", $"appsettings.{builder.Environment.EnvironmentName}.json"), true, true)
		.AddCommandLine(args);
}
```

- [ ] **Step 4: Run the test**

Expected: PASS. Also assert `builder.Environment.EnvironmentName` still resolves by setting `ASPNETCORE_ENVIRONMENT=Staging` in a second test and checking the staging file is the one probed.

- [ ] **Step 5: Update documentation and commit**

Update `docs/for-technicians/configuration.md`: application settings come from `Data/appsettings.json`, optionally layered with `appsettings.{Environment}.json` and command-line arguments; environment variables configure the host (`ASPNETCORE_ENVIRONMENT`) and nothing else.

```bash
git add -A
git commit -m "fix(config): make appsettings.json the only source of application settings

CreateBuilder installs the environment-variable provider, and the previous code
layered JSON on top of it without ever removing it, so Basil__Server__Port
silently outranked the file. Clearing the source list and re-adding only the
intended layers gives configuration one source of truth; ASPNETCORE_ENVIRONMENT
still works because it is read while the builder is constructed."
```

---

### Task 0.10: `LiveEventHub` and `StateStream`

Introduces the eventing seam. **No slice adopts it in this task** — Phase 1 and Phase 5 do.

**Files:**
- Create: `src/Basil.Server/Shared/Eventing/{ILiveEventHub,LiveEventHub,LiveSubscription,StreamKey}.cs`
- Rename: `Shared/Eventing/SnapshotChannel.cs` → `StateStream.cs`
- Create: `tests/Basil.Server.Tests/Shared/Eventing/LiveEventHubTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  bool HasSubscribers(StreamKey key);
  void MarkStale(StreamKey key, long version);
  void Publish(StreamKey key, long version, ReadOnlyMemory<byte> payload);
  LiveSubscription Open(StreamKey key);          // registers + captures (Latest, Version, IsStale) atomically
  ```
  and on `LiveSubscription`:
  ```csharp
  ReadOnlyMemory<byte>? Snapshot { get; }
  long Version { get; }
  bool IsStale { get; }
  bool SeedIfNotSuperseded(ReadOnlyMemory<byte> snapshot, long fence);
  IAsyncEnumerable<LiveEvent> Events { get; }    // only items with Version > Snapshot.Version
  ```

- [ ] **Step 1: Write the failing ordering test**

```csharp
/// <summary>
///     A subscriber never receives an event the snapshot it opened with already contains, and
///     never receives one out of order. This is the SSE contract, not an implementation detail:
///     clients apply an item only when its version exceeds the last one they applied.
/// </summary>
[Fact]
public async Task SubscriberNeverReceivesAnEventAtOrBelowItsSnapshotVersion()
{
	var hub = new LiveEventHub();
	var key = new StreamKey("match", 1, "main");

	hub.Publish(key, 42, Bytes("v42"));

	await using var sub = hub.Open(key);
	hub.Publish(key, 43, Bytes("v43"));
	hub.Publish(key, 44, Bytes("v44"));

	Assert.Equal(42, sub.Version);
	var received = await TakeAsync(sub.Events, 2);
	Assert.Equal([43L, 44L], received.Select(e => e.Version));
}
```

- [ ] **Step 2: Write the failing zero-subscriber test**

```csharp
[Fact]
public void PublishWithoutSubscribersIsSkippedByTheCallerAndMarksTheStreamStale()
{
	var hub = new LiveEventHub();
	var key = new StreamKey("match", 1, "main");

	Assert.False(hub.HasSubscribers(key));
	hub.MarkStale(key, 7);

	using var sub = hub.Open(key);
	Assert.True(sub.IsStale);
}
```

- [ ] **Step 3: Write the failing seed-race test**

```csharp
/// <summary>
///     A snapshot built while unsubscribed loses to a publish that landed in the meantime, so a
///     freshly built but older snapshot can never roll a client back.
/// </summary>
[Fact]
public void SeedLosesToAPublishThatLandedWhileTheSnapshotWasBeingBuilt()
{
	var hub = new LiveEventHub();
	var key = new StreamKey("match", 1, "main");
	hub.MarkStale(key, 10);

	using var sub = hub.Open(key);
	var fence = sub.Version;

	hub.Publish(key, 11, Bytes("published-11"));
	var seeded = sub.SeedIfNotSuperseded(Bytes("built-from-10"), fence);

	Assert.False(seeded);
	Assert.Equal(Bytes("published-11"), sub.Snapshot);
}
```

- [ ] **Step 4: Run all three and watch them fail**

Run: `dotnet test tests/Basil.Server.Tests --filter "FullyQualifiedName~LiveEventHubTests"`
Expected: FAIL — the types do not exist.

- [ ] **Step 5: Implement**

`LiveEventHub` holds, per `StreamKey`, a lock, a `Latest` payload with its version, a stale flag, and a subscriber list. `Open` takes that lock to register the subscriber and capture `(Latest, Version, IsStale)` in one step, which is what removes today's drain-then-read window in `LiveSseRoutes.SubscribeWithSnapshot`. `Publish` takes the same lock to store `Latest`, clear the stale flag and snapshot the subscriber list, then invokes handlers outside the lock. `SeedIfNotSuperseded` is `SequenceGate.TryAdvance` semantics against the captured fence.

The hub references no repository, no feature DTO and no snapshot builder. It stores opaque bytes and a version.

- [ ] **Step 6: Run the tests**

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(eventing): add a broadcast hub with an explicit subscribe handshake

SSE is a loudspeaker: every subscriber to a stream gets the same immutable
event. The hub owns subscription, routing, broadcast, backpressure and ordering,
and knows nothing about repositories, snapshots or feature DTOs -- the caller
decides whether to build, using HasSubscribers.

Open captures the latest payload and its version under the same lock Publish
takes, so the snapshot a subscriber starts with can never be followed by an
event it already contains, and a snapshot built while the stream was unsubscribed
loses to any publish that landed meanwhile. No slice adopts this yet."
```

---

### Task 0.11: Merge the test projects

**Files:**
- Create: `tests/Basil.Server.Tests/Basil.Server.Tests.csproj`
- Move: everything under `tests/Basil.Application.Tests/` and `tests/Basil.Infrastructure.Tests/` into `tests/Basil.Server.Tests/{Features,Shared}/`
- Delete: both old test projects
- Modify: `Basil.slnx`

- [ ] **Step 1: Create the merged project and move files mirroring the source layout**

A test for `Features/Multiplayer/MatchControlService.cs` lands at `tests/Basil.Server.Tests/Features/Multiplayer/MatchControlServiceTests.cs`.

- [ ] **Step 2: Build and test**

Run: `dotnet test`
Expected: baseline counts. Duplicate class names across the two merged projects are the likely failure; rename by prefixing with the slice, not by deleting a test.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test: merge the Application and Infrastructure test projects

The projects they tested no longer exist. The merged project mirrors the slice
layout, so a slice's tests sit beside the code they pin."
```

---

### Task 0.12: Migrate to xunit v3

Sequenced last in Phase 0 deliberately: it is not a prerequisite for the structural move, and if it goes badly it must be revertable without losing that move. It belongs in this phase rather than in each slice's phase because Phase 0 already rewrites every test csproj, and xunit v2 and v3 cannot coexist inside one test project.

**Files:**
- Modify: `Directory.Packages.props`, every `tests/*/*.csproj`

- [ ] **Step 1: Swap the packages**

In `Directory.Packages.props`, replace `xunit` 2.9.3 and `xunit.runner.visualstudio` with `xunit.v3` and `xunit.runner.visualstudio` at the versions the v3 line requires. Each test csproj replaces `<PackageReference Include="xunit"/>` with `<PackageReference Include="xunit.v3"/>`.

- [ ] **Step 2: Fix the API differences**

v3 moves assembly-level attributes and changes a small number of assertion and fixture APIs. Compile and follow the diagnostics. Do not change any assertion's meaning while porting it.

- [ ] **Step 3: Run everything**

Run: `dotnet test --configuration Release`
Expected: baseline counts. A test that cannot be ported is a finding, not a deletion — record it in the checkpoint.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: migrate from xunit 2.9.3 to xunit.v3

Phase 0 already rewrites every test project file, and v2 and v3 cannot coexist
inside one project, so migrating now is one edit per csproj instead of one now
and another during each slice's phase. Sequenced after the structural move so a
failure here is revertable on its own."
```

---

### Task 0.13: The User contract change and migration 006

The Users *slice* work stays in Phase 3. Only the contract lands here, because `IUserRepository` has twenty consumers across six slices and `User.SilenceEnd` is read by Auth, IRC and Multiplayer — leaving it to Phase 3 would edit files three other phases own.

**Files:**
- Create: `src/Basil.Server/Shared/Persistence/Migrations/006_users_generated_safename_nullable_silence.sql`
- Modify: `src/Basil.Domain/Users/User.cs`, `src/Basil.Server/Features/Users/{IUserRepository,SqliteUserRepository,UserSearchFilters}.cs`, `Shared/Sessions/UserSession.cs`, `Features/Auth/LoginService.cs`, `Features/Irc/IrcAuthenticationService.cs`, `Features/Bot/BotBootstrapService.cs`, `Features/Users/UserRoutes.cs`, `Features/Users/UserView.cs`, `Shared/Persistence/Migrations/001_base.sql`
- Test: `tests/Basil.Server.Tests/Features/Users/SafeNameGenerationTests.cs`

**Interfaces:**
- Produces: `Task UpdateNameAsync(int id, string name, CancellationToken)` — the `safeName` parameter is gone.
- Produces: `Task UpdateSilenceEndAsync(int id, DateTimeOffset? silenceEnd, CancellationToken)`.
- Produces: `User(int Id, string Name, Country Country, UserPrivileges Privilege, DateTimeOffset? SilenceEnd, DateTimeOffset? DeletedAt = null)`.
- Produces: `UserSearchFilters(string? Keywords, IReadOnlyList<Country>? Countries, UserPrivileges? Privilege, bool? Silenced, bool IncludeDeleted)`.

- [ ] **Step 1: Write the failing equivalence test**

```csharp
/// <summary>
///     SQLite generates SafeName now, so the database's expression and the in-memory rule used by
///     the session registries must agree for every username the server can actually store.
/// </summary>
/// <remarks>
///     They diverge only outside ASCII — SQLite's lower() is ASCII-only while ToLowerInvariant is
///     not — which is unreachable because ValidateUsername rejects non-ASCII names. That rejection
///     is the invariant holding these two implementations together, so it is tested alongside.
/// </remarks>
[Theory]
[InlineData("Peppy")]
[InlineData("pe ppy")]
[InlineData("PE_PPY")]
[InlineData("AB-CD")]
[InlineData("[Box] x")]
[InlineData("Z9 _-[]")]
[InlineData("abc")]
[InlineData("A1_-[] b")]
public async Task GeneratedSafeNameMatchesMakeSafeName(string name)
{
	await using var db = await UsersFixture.CreateAsync();
	var id = await db.InsertUserAsync(name);

	var stored = await db.QuerySafeNameAsync(id);

	Assert.Equal(User.MakeSafeName(name), stored);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test tests/Basil.Server.Tests --filter "FullyQualifiedName~GeneratedSafeNameMatches"`
Expected: FAIL — the column is still written by the repository.

- [ ] **Step 3: Write migration 006**

```sql
-- SafeName is derived from Name by a fixed rule, so the database owns it rather than every insert
-- and update path remembering to recompute it. replace(lower(Name), ' ', '_') reproduces
-- User.MakeSafeName exactly for every username ValidateUsername admits, which is ASCII-only;
-- SQLite's lower() is ASCII-only too, so the two agree wherever they can both be reached.
--
-- SilenceEnd becomes nullable in the same rebuild rather than in a second one: "never silenced"
-- is naturally NULL, not a sentinel epoch, and the table can only be rebuilt once cheaply.
--
-- A rebuild is required because SafeName already exists as a real column carrying a UNIQUE index,
-- and SQLite refuses DROP COLUMN on an indexed column.
PRAGMA foreign_keys = off;

create table Users_new
(
	Id         INTEGER PRIMARY KEY AUTOINCREMENT,
	Name       varchar(32)                                                        not null,
	SafeName   varchar(32) generated always as (replace(lower(Name), ' ', '_')) stored,
	Privilege  int      default 1                                                 not null,
	PwBcrypt   char(60)                                                           not null,
	Country    char(2)  default 'xx'                                              not null,
	SilenceEnd datetime                                                           null,
	DeletedAt  datetime                                                           null,
	constraint Users_Name_uindex unique (Name),
	constraint Users_SafeName_uindex unique (SafeName)
);

insert into Users_new (Id, Name, Privilege, PwBcrypt, Country, SilenceEnd, DeletedAt)
select Id, Name, Privilege, PwBcrypt, Country,
       case when SilenceEnd is null or SilenceEnd <= '1970-01-01 00:00:00' then null else SilenceEnd end,
       DeletedAt
from Users;

drop table Users;
alter table Users_new rename to Users;
create index Users_Privilege_index on Users (Privilege);

PRAGMA foreign_keys = on;
```

- [ ] **Step 4: Stop writing `SafeName`**

`SqliteUserRepository`'s `INSERT` drops the `SafeName` column and its parameter; the rename `UPDATE` becomes `UPDATE Users SET Name = @Name WHERE Id = @Id`. `IUserRepository.UpdateNameAsync` drops `safeName`. Update the three callers (`BotBootstrapService`, `UserRoutes` twice). Remove `SafeName` from the seed insert in `001_base.sql` and from the two `SqliteScoreRepositoryTests` fixtures.

- [ ] **Step 5: Make `SilenceEnd` nullable**

`User.SilenceEnd` and `UserSession.SilenceEnd` become `DateTimeOffset?`. `UserSession`:

```csharp
/// <summary>Gets the time this user's silence expires, or null when they are not silenced.</summary>
public DateTimeOffset? SilenceEnd { get; set; }

/// <summary>Gets a value indicating whether the user is currently silenced.</summary>
public bool Silenced => SilenceEnd > DateTimeOffset.UtcNow;

/// <summary>Gets how long the current silence still has to run, or zero when not silenced.</summary>
public TimeSpan RemainingSilence =>
	SilenceEnd is { } end && end > DateTimeOffset.UtcNow ? end - DateTimeOffset.UtcNow : TimeSpan.Zero;
```

`LoginService` still writes `ServerPacketWriter.SilenceEnd((int)session.RemainingSilence.TotalSeconds)`, which is `0` for an unsilenced user exactly as the epoch sentinel produced — **the wire is unchanged and no protocol test may need editing**. `UserRow.SilenceEnd` becomes `DateTime?`. `UserView.SilenceEnd` becomes nullable.

- [ ] **Step 6: Reshape `UserSearchFilters` and add `UpdateSilenceEndAsync`**

Change `PrivilegeMask` (`ushort?`) to `Privilege` (`UserPrivileges?`) — the SQL `(Privilege & @Privilege) = @Privilege` is already correct. Add `Silenced` and `IncludeDeleted` to the record and make `DeletedAt IS NULL` conditional in `BuildSearchWhereClause`. **Do not add the authorization gate or the strict parsing here** — that is Task 3.2, and putting it here would split the Users slice's own work across two phases.

- [ ] **Step 7: Run the tests**

Run: `dotnet test`
Expected: the equivalence test passes; the rest match baseline. `tests/Basil.Protocol.Tests` must be untouched and green.

- [ ] **Step 8: Verify the schema diff is exactly what was intended**

Re-run the Task 0.1 Step 5 schema dump and diff. Expected changes: `Users.SafeName` gains `generated always as (...) stored`, `Users.SilenceEnd` loses `not null` and its default. Nothing else.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(users): let SQLite own SafeName, make SilenceEnd nullable

SafeName is derived data the runtime had to remember to recompute on every
insert and rename. A stored generated column makes the database responsible for
it: replace(lower(Name), ' ', '_') reproduces User.MakeSafeName exactly across
the character set ValidateUsername admits, verified against SQLite 3.53.3.
MakeSafeName stays -- the in-memory session registries still key on it.

SilenceEnd becomes nullable in the same table rebuild, since 'never silenced' is
NULL rather than a sentinel epoch. The wire is unchanged: an unsilenced user
still sends a zero-second SilenceEnd packet.

The contract changes land here rather than in the Users slice phase because
IUserRepository has twenty consumers across six slices; leaving them until then
would edit files three other phases own."
```

---

### Task 0.14: Baseline verification and advisor checkpoint

- [ ] **Step 1: Diff every baseline artifact**

```bash
dotnet build --configuration Release
dotnet test --configuration Release
# routes, metrics, locale values, schema — as captured in Task 0.1
```

Expected differences, and only these: the schema changes from Task 0.13; the `UserView.SilenceEnd` nullability in the OpenAPI document; locale key *names* (values unchanged). Everything else must be identical. Investigate any other difference before proceeding — it is an unintended behavior change.

- [ ] **Step 2: Update the checkpoint to `Ready for review` and commit**

- [ ] **Step 3: Advisor review**

Ask specifically: is this a real restructure or a folder move? Are the slice boundaries real? Is `SliceAdjacency.Allowed` small and defensible? Did anything land in `Shared` that is really feature-owned business logic? Does the baseline diff show only the intended changes?

---

# Phase 1 — Multiplayer

Largest phase. Settles the patterns Phases 2–4 follow. Runs in parallel with Phase 5.

### Task 1.1: Audit which mutations can leave state invalid on exception

The design says a scope that throws still publishes, because the state change that happened is real and hiding it leaves subscribers silently stale. That is only safe if a throw cannot leave the match in a state that violates its own invariants. **This audit runs before `MatchMutationScope` is written, and its result can change the design.**

**Files:**
- Create: `plans/execution/mutation-invariant-audit.md`

- [ ] **Step 1: Enumerate every read-then-mutate sequence under `MatchSession.Lock`**

```bash
grep -rn "Lock.WaitAsync" src/Basil.Server --include=*.cs
```

For each, record: the file and line; every field it mutates; whether it can throw between the first and last mutation; and if so, whether the intermediate state satisfies the match invariants (a slot has a player if and only if its status is not `Open`; the host id is either `NoHostId` or an occupied slot's player; `InProgress` implies at least one non-`Open` slot).

- [ ] **Step 2: Classify each sequence**

```
A — single-field mutation, or all mutations are infallible after the first
    -> publishing on exception is safe; the state is a real, valid state
B — multi-field mutation with a fallible call in the middle
    -> publishing on exception could expose an invariant-violating state
```

- [ ] **Step 3: Decide**

If class B is empty, the design stands unchanged: record that finding with the evidence and continue.

If class B is non-empty, the scope gains an explicit `mutation.Invalidate()` that callers of those specific sequences use to suppress the publish and mark the stream stale instead — the next subscriber then rebuilds from the real state, and no subscriber is ever shown an invalid one. Record which sequences need it and why. Do **not** make suppression the default: that reintroduces the silently-stale-subscriber problem for the far more common class A.

- [ ] **Step 4: Commit the audit**

```bash
git add plans/execution/mutation-invariant-audit.md
git commit -m "docs: audit which match mutations can throw mid-sequence

MatchMutationScope publishes on exception because a partially applied change is
still a real state, and hiding it leaves every subscriber silently stale. That
reasoning only holds if a throw cannot expose a state that violates the match's
own invariants, so every read-then-mutate sequence under the match lock is
classified here before the scope is written."
```

---

### Task 1.2: `MatchMutationScope`

**Files:**
- Create: `src/Basil.Server/Features/Multiplayer/MatchMutationScope.cs`
- Modify: `src/Basil.Server/Features/Multiplayer/MatchSession.cs`
- Test: `tests/Basil.Server.Tests/Features/Multiplayer/MatchMutationScopeTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  ValueTask<MatchMutationScope> BeginMutationAsync(CancellationToken);   // on MatchSession
  MatchSession Session { get; }
  long? AllocatedVersion { get; }
  void PublishState(bool lobby = true);
  void PublishHost(); void PublishRefs(); void PublishBans(); void PublishTimer();
  ValueTask CompleteAsync();
  ```

- [ ] **Step 1: Write the failing semantics tests**

```csharp
[Fact]
public async Task AMutationThatPublishesNothingAllocatesNoVersion()
{
	var match = NewMatch();
	var before = match.CurrentStateVersion;

	await using (var m = await match.BeginMutationAsync(default))
	{
		_ = m.Session.Slots[0].Status;
	}

	Assert.Equal(before, match.CurrentStateVersion);
}

[Fact]
public async Task RepeatedPublishRequestsCoalesceToOneVersion()
{
	var match = NewMatch();
	long? allocated;

	await using (var m = await match.BeginMutationAsync(default))
	{
		m.Session.Slots[0].Status = SlotStatus.Ready;
		m.PublishState();
		m.PublishState();
		m.PublishState(lobby: false);
		allocated = m.AllocatedVersion;
	}

	Assert.Equal(1, VersionsAllocatedDuring(match));
	Assert.NotNull(allocated);
}

[Fact]
public async Task NestingOnTheSameMatchThrowsInsteadOfDeadlocking()
{
	var match = NewMatch();
	await using var outer = await match.BeginMutationAsync(default);

	await Assert.ThrowsAsync<InvalidOperationException>(
		async () => await match.BeginMutationAsync(default));
}

[Fact]
public async Task AnExceptionInsideTheScopeStillPublishesWhatChanged()
{
	var match = NewMatch();
	var published = new List<long>();

	await Assert.ThrowsAsync<InvalidOperationException>(async () =>
	{
		await using var m = await match.BeginMutationAsync(default);
		m.Session.Slots[0].Status = SlotStatus.Ready;
		m.PublishState();
		throw new InvalidOperationException("boom");
	});

	Assert.Single(published);
}

[Fact]
public async Task APublishFailureDoesNotSurfaceFromDisposal()
{
	var match = NewMatchWhosePublishThrows();

	await using var m = await match.BeginMutationAsync(default);
	m.Session.Slots[0].Status = SlotStatus.Ready;
	m.PublishState();
	// no exception escapes disposal
}

[Fact]
public async Task TheLockIsReleasedEvenWhenTheBodyThrows()
{
	var match = NewMatch();

	try
	{
		await using var m = await match.BeginMutationAsync(default);
		throw new InvalidOperationException("boom");
	}
	catch (InvalidOperationException) { }

	await using var second = await match.BeginMutationAsync(default);   // would hang if leaked
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/Basil.Server.Tests --filter "FullyQualifiedName~MatchMutationScopeTests"`
Expected: FAIL — `BeginMutationAsync` does not exist.

- [ ] **Step 3: Implement**

```csharp
/// <summary>
///     One read-mutate-broadcast sequence on a match, holding the match's lock for the mutation and
///     owning the state version the broadcast carries.
/// </summary>
/// <remarks>
///     Callers used to allocate a version by hand inside the lock and thread it through every
///     publish call, which meant every call site could forget to increment, could pass a stale
///     value, or could hold the lock across the broadcast. This scope owns that lifecycle instead.
///
///     Disposal does exactly three things, in this order: allocate the version if any publish was
///     requested, release the lock, then run the requested publishes unlocked. Building and
///     broadcasting outside the lock is what keeps a slow build from serializing the match, and the
///     version allocated inside it is what lets a build that finishes out of order be dropped
///     rather than reverting live state.
///
///     A scope that requests no publish allocates no version. Repeated publish requests for the
///     same stream coalesce into one publish at one version. An exception inside the scope still
///     publishes what was requested before it: the state change that happened is real, and hiding
///     it would leave every subscriber silently stale. A publish that fails is logged and counted,
///     never rethrown -- the mutation has already committed to memory and the live stream is a
///     projection of it, not a participant in it.
///
///     The match lock is not reentrant, so a nested scope on the same match would deadlock. It
///     throws instead.
/// </remarks>
public sealed class MatchMutationScope : IAsyncDisposable
```

Add to `MatchSession`: `CurrentStateVersion` (read-only view of `_stateVersion`) and an owner marker used to detect nesting.

- [ ] **Step 4: Run the tests**

Expected: PASS.

- [ ] **Step 5: Convert every call site**

```bash
grep -rn "Lock.WaitAsync" src/Basil.Server --include=*.cs
```

Convert each to the scope. Delete `MatchSession.NextStateVersion` once nothing calls it.

- [ ] **Step 6: Run the full suite, especially the race tests**

Run: `dotnet test --filter "FullyQualifiedName~MatchSessionRace"`
Expected: PASS.
Run: `dotnet test`
Expected: baseline counts.

- [ ] **Step 7: Commit**

---

### Task 1.3: Adopt the hub, add the zero-subscriber guard

**Files:**
- Modify: `Features/Multiplayer/MatchMembershipService.cs` (the `Enqueue*`/`Publish*` family), `MatchLiveEvents.cs` (deleted), `IMatchLiveEvents.cs` (deleted), `LiveSseRoutes.cs`
- Test: `tests/Basil.Server.Tests/Features/Multiplayer/MatchBroadcastTests.cs`

- [ ] **Step 1: Write the failing guard test**

```csharp
/// <summary>
///     With nobody subscribed, a mutation must not build the snapshot at all — the build hits the
///     user and beatmap repositories, and it used to run on every mutation regardless of whether
///     anyone was listening.
/// </summary>
[Fact]
public async Task WithNoSubscribersAMutationPerformsNoSnapshotBuild()
{
	var userRepo = Substitute.For<IUserRepository>();
	var service = NewMembershipService(userRepo);
	var match = NewMatch();

	await using (var m = await match.BeginMutationAsync(default))
	{
		m.Session.Slots[0].Status = SlotStatus.Ready;
		m.PublishState();
	}

	await userRepo.DidNotReceiveWithAnyArgs().FetchByIdAsync(default, default);
}
```

This is one of the few legitimate uses of a mock-interaction assertion: the observable behavior under test *is* "the repository is not called".

- [ ] **Step 2: Run it and watch it fail**

Expected: FAIL — the repository is called.

- [ ] **Step 3: Replace the publish family with hub calls**

```csharp
// Multiplayer owns how a snapshot is built; the hub only knows whether anyone is listening.
private async Task PublishMainAsync(MatchSession match, long version, CancellationToken ct)
{
	var key = StreamKey.Match(match.DbId, "main");
	if (!hub.HasSubscribers(key))
	{
		hub.MarkStale(key, version);
		return;
	}

	var snapshot = await MatchLiveSnapshotBuilder.BuildMain(
		match, gameRegistry, ircRegistry, userRepo, beatmapRepo, ct);
	if (match.MainState.Publish(snapshot, version) is { } delta)
		hub.Publish(key, version, delta);
}
```

Repeat for `settings`, `slots`, per-slot, `host`, `refs`, `bans`, `timer`, `chat`, `playerScore`. Delete `IMatchLiveEvents` and `MatchLiveEvents`.

- [ ] **Step 4: Rewrite the SSE endpoints onto the subscribe handshake**

Replace `SubscribeWithSnapshot`'s drain-then-read with `hub.Open`, the stale rebuild, and `SeedIfNotSuperseded`, exactly as the design's section 4.2 code shows. Emit the version as the SSE `id:` on state streams.

- [ ] **Step 5: Add the failure-semantics tests**

One test per case in the design's section 4.3: a subscriber disconnecting between the guard and the publish is a no-op; a build failure after a committed mutation does not roll the mutation back, does not reuse the version, marks the stream stale and increments `basil.sse.build_failed`; a full channel yields a `gap` for that subscriber only.

- [ ] **Step 6: Run, verify, commit**

Run: `dotnet test`
Expected: baseline counts plus the new tests. Measure and record in the checkpoint: number of repository calls per mutation with zero subscribers, before and after.

---

### Task 1.4: `MatchSession` model encapsulation

**Files:**
- Create: `Features/Multiplayer/{SelectedBeatmap,MatchCountdown,EmptyRoomWatch,MatchAuthority}.cs`
- Modify: `MatchSession.cs`, `MatchPacketDataMapper.cs`, `MatchLiveSnapshotBuilder.cs`, `MpCommandService.cs`, `MatchControlService.cs`

- [ ] **Step 1: Write the failing packet-equivalence test**

```csharp
/// <summary>
///     A room with no beatmap selected produces the same MatchData bytes it did when "no beatmap"
///     was spelled as three separate default fields.
/// </summary>
[Fact]
public void AMatchWithNoSelectedBeatmapSerializesIdentically()
{
	var match = NewMatch();
	match.Map = null;

	Assert.Equal(ExpectedNoBeatmapBytes, ServerPacketWriter.UpdateMatch(match.ToPacket()));
}
```

- [ ] **Step 2: Run, fail, implement each group**

`SelectedBeatmap` replaces `MapId`/`MapMd5`/`MapName` with `Map` and `PreviousMap`, `UnresolvedMd5` staying separate because it is warning-deduplication state rather than part of the selection. `MatchCountdown` replaces the four timer fields. `EmptyRoomWatch` replaces `EmptyRoomTimer` and `EmptyRoomWarningSent`, and adds jitter:

```csharp
// Jitter the empty-room close so rooms created together do not all expire together. This is an
// independent resilience improvement: a fixed interval synchronizes cleanup across rooms, which is
// worth desynchronizing on its own merits. It closes no investigation item -- the soak failure's
// root cause was never confirmed.
private static TimeSpan NextCloseDelay() =>
	EmptyRoomTimeout + TimeSpan.FromSeconds(Random.Shared.Next(0, 90));
```

`MatchAuthority` absorbs `_referees`, `_bannedIds`, `_invitedIds`, `_tourneyClients`, `CreatorId`, `HostId` and the `IsReferee`/`IsCreator`/`HasGameplayHost` predicates, keeping referee, host and creator as the three distinct concepts `docs/for-developers/multiplayer.md` documents.

- [ ] **Step 3: Run the protocol tests**

Run: `dotnet test tests/Basil.Protocol.Tests`
Expected: PASS, unchanged.

- [ ] **Step 4: Commit**

---

### Task 1.5: Decompose `MatchControlService` (1303 lines, 43 members)

**Files:**
- Create: `Features/Multiplayer/Handlers/{Settings,Slots,Authority,Countdown,Lifecycle}/*.cs`
- Delete: `MatchControlService.cs`

- [ ] **Step 1: One handler class per operation, grouped by concern**

`SetLockedHandler`, `SetPrivateHandler`, `SetSizeHandler`, `MoveSlotHandler`, `SetHostHandler`, `ClearHostHandler`, `SetNameHandler`, `SetPasswordHandler`, `InviteHandler`, `AddRefereeHandler`, `SetRefereesHandler`, `RemoveRefereeHandler`, `SetTeamHandler`, `SetMapHandler`, `SetModsHandler`, `StartHandler`, `TimerHandler`, `AbortTimerHandler`, `AbortHandler`, `KickHandler`, `BanHandler`, `UnbanHandler`, `SetBansHandler`, `ForceInviteHandler`, `SetSlotsHandler`, `CloseHandler`. Each keeps its result enum as a nested type.

- [ ] **Step 2: Move each method verbatim, then adapt it to the mutation scope**

Do not rewrite logic in this task beyond the scope conversion. Tests move alongside.

- [ ] **Step 3: Run the full suite, commit**

---

### Task 1.6: Decompose `MatchMembershipService` (903 lines, 27 members)

Split into `MatchMembership` (join, force-join, occupy, leave), `MatchLifecycle` (create, create-empty, close, teardown, start, empty-room watch), and `MatchBroadcast` (the publish family from Task 1.3).

---

### Task 1.7: Decompose `MatchSubResourceRoutes` (1423 lines, 33 members)

One endpoint file per sub-resource under `Features/Multiplayer/Endpoints/`: `MatchChatEndpoints`, `MatchHostEndpoints`, `MatchRefereeEndpoints`, `MatchBanEndpoints`, `MatchSlotEndpoints`, `MatchTimerEndpoints`, `MatchAbortEndpoints`, `MatchCloseEndpoints`. Request/response records move to the file that uses them. Verify the route table diff is empty against the Task 0.6 capture.

---

### Task 1.8: Localize the multiplayer slice, own the `!mp` help

**Files:**
- Modify: `Features/Multiplayer/Locale/multiplayer.en.json`, the 47 literal sites, `MpCommandService.HelpText`

- [ ] **Step 1: Move every literal into the fragment**

```bash
grep -rnE '"[A-Z][a-z][^"]{15,}"' src/Basil.Server/Features/Multiplayer --include=*.cs \
  | grep -viE 'nameof|///|SectionName'
```

Each becomes a `Multiplayer.Announce.*` or `Commands.Mp.<Sub>.*` key. Wording is preserved exactly — this task moves text, it does not rewrite it.

- [ ] **Step 2: Give each `!mp` subcommand its own help**

Each subcommand handler exposes `Usage` and `Description`, both resolved from `Commands.Mp.<Sub>.Usage`/`.Description`. `!mp help` composes its output from the registered handlers. Delete `MpCommandService.HelpText`.

- [ ] **Step 3: Run the coverage test from Task 0.8**

Expected: PASS with no missing and no orphaned keys.

- [ ] **Step 4: Commit**

---

### Task 1.9: Logging pass and test triage for the slice

- [ ] **Step 1: Reclassify every log call in the slice against the taxonomy**

Information: match created, match closed, round started, round completed. Warning: a recoverable failure. Error: an operation failed. Everything currently at Debug that describes ordinary per-packet or per-request flow is deleted, not downgraded — the metric already covers volume.

- [ ] **Step 2: Triage the slice's tests**

Dispatch the test-triage subagent (Sonnet) over `tests/Basil.Server.Tests/Features/Multiplayer/` with the decision tree from the design's section 9.3 and the instruction to classify only, never to edit. Apply its classifications yourself.

- [ ] **Step 3: Update `multiplayer.md`, `sse.md` and the ADR-004 addendum**

`sse.md` gains the ordering contract as a client-facing contract: snapshot at version N, every later item strictly greater, versions monotonic but not contiguous, loss signalled only by a `gap` event.

- [ ] **Step 4: Run everything, commit, update the checkpoint to `Ready for review`**

---

### Task 1.10: Advisor checkpoint

Ask: is the hub genuinely ignorant of business logic, or did it acquire knowledge back? Did the mutation scope remove the footgun or relocate it? Is the ordering contract enforced by a test rather than by a comment? Did the mutation-invariant audit's conclusion actually get honored? Does any Phase 2–4 area now need re-touching?

---

### Task 1.11: Re-run the file-overlap check before Phases 2–4 fan out

- [ ] **Step 1: Compute each phase's file set**

For Phases 2, 3 and 4, list every file the phase's tasks name. Intersect the three sets pairwise.

- [ ] **Step 2: If any intersection is non-empty, resolve it before starting**

Exactly one of: serialize the overlapping work into its own task that runs first; assign the file a single owning phase and have the others consume its result; or move the phase boundary. **Never "we will coordinate."**

- [ ] **Step 3: Record the result in `orchestration.md` and commit**

---

# Phase 2 — Chat, Bot, IRC

Depends on Phase 1 for the patterns. Parallel with Phases 3 and 4.

### Task 2.1: Decompose `CommandDispatcher` and give `!help` to the commands

**Files:**
- Create: `Features/Bot/Commands/{RollCommand,WhereCommand,FaqCommand,HelpCommand}.cs`, `Features/Bot/ICommand.cs`
- Modify: `CommandDispatcher.cs`

**Interfaces:**
- Produces: `ICommand` with `Trigger`, `Usage`, `Description`, `bool Chainable`, `Task<bool> HandleAsync(...)` — `Usage` and `Description` resolved from `Commands.<Name>.*`.

- [ ] **Step 1: Write the failing help test**

```csharp
/// <summary>
///     !help lists exactly the registered commands, so adding a command cannot leave the help text
///     behind and the wording lives in the locale rather than in a C# array.
/// </summary>
[Fact]
public async Task HelpListsEveryRegisteredCommand()
{
	var reply = await Dispatch("!help");

	foreach (var command in AllRegisteredCommands())
		Assert.Contains(command.Usage, reply);
}
```

- [ ] **Step 2: Run, fail, implement `ICommand` and register each command**

Delete `CommandDispatcher.ChatCommands` and `BuildHelpText`. `HelpCommand` composes from the registry.

- [ ] **Step 3: Move `FaqService` construction out of the dispatcher**

`private readonly FaqService _faq = new(storageOptions);` becomes an injected dependency — a service constructing another service by hand is what makes the dispatcher untestable in isolation.

- [ ] **Step 4: Run, commit**

### Task 2.2: Localize Bot, Chat and IRC fragments; keep the chain semantics

Move `NonChainableMpSubcommands` and `LobbyAllowedMpSubcommands` onto the command objects as `Chainable` and `AllowedInLobby`, so the two frozen sets stop being a second place to update when a subcommand is added.

### Task 2.3: Logging pass over Chat, Bot and IRC

### Task 2.4: Test triage for the three slices

### Task 2.5: Update `chat.md` and `irc.md`; commit; checkpoint `Ready for review`

---

# Phase 3 — Users, Auth, Social

The contract changes already landed in Task 0.13. What remains is file-local to the Users and Auth slices.

### Task 3.1: Username validation

**Files:**
- Modify: `src/Basil.Domain/Users/User.cs`
- Test: `tests/Basil.Domain.Tests/UserTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData("pëppy")]
[InlineData("ペッピー")]
[InlineData("peppy x")]
public void NonAsciiUsernamesAreRejectedWithTheirOwnMessage(string name)
{
	Assert.False(User.ValidateUsername(name, out var error));
	Assert.Equal(LocaleCatalog.Get("Users.Validation.NonAscii"), error);
}

[Theory]
[InlineData("pep!py")]
[InlineData("pep@py")]
public void DisallowedAsciiCharactersAreRejectedWithTheCharsetMessage(string name)
{
	Assert.False(User.ValidateUsername(name, out var error));
	Assert.Equal(LocaleCatalog.Get("Users.Validation.Charset"), error);
}
```

- [ ] **Step 2: Run, fail, replace the regex**

```csharp
private static readonly SearchValues<char> AllowedUsernameCharacters = SearchValues.Create(
	"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-[] ");
```

The non-ASCII branch goes before the charset branch so the reported cause is precise. It is also the invariant that keeps SQLite's ASCII-only `lower()` in agreement with `ToLowerInvariant` for the generated `SafeName` column — say so in the remarks. Drop `partial` from the record and the `GeneratedRegex` method. Move all five messages to `Users.Validation.*`.

- [ ] **Step 3: Run, commit**

### Task 3.2: Sensitive search filters and strict parsing

**Files:**
- Modify: `Features/Users/UserSearchQueryParser.cs`, `UserRoutes.cs`
- Test: `tests/Basil.IntegrationTests/UserSearchAuthorizationTests.cs`

- [ ] **Step 1: Write the failing authorization tests**

```csharp
[Fact]
public async Task SearchingBySilencedWithoutCredentialsIsUnauthorized()
{
	var response = await AnonymousClient.GetAsync("/users/search?q=silenced%3Dtrue");
	Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task SearchingByIncludeDeletedAsANonAdminIsForbidden()
{
	var response = await NonAdminClient.GetAsync("/users/search?q=includedeleted%3Dtrue");
	Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

/// <summary>
///     A mistyped sensitive filter must not fall through to free text: silently returning
///     unfiltered results to a caller who asked for a filter is worse than an error.
/// </summary>
[Fact]
public async Task AMalformedSensitiveFilterIsRejected()
{
	var response = await AdminClient.GetAsync("/users/search?q=silenced%3Dyse");
	Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task AdminCanFilterBySilenced()
{
	var response = await AdminClient.GetAsync("/users/search?q=silenced%3Dtrue");
	Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- [ ] **Step 2: Run, fail, implement**

The parser returns the filters plus the set of sensitive keys the query used. The endpoint checks `HttpContext.User.IsInRole(AdminKeyDefaults.Role)` and returns the same shape `RequireAuthorization(AdminKeyDefaults.Policy)` produces. Sensitive keys parse strictly: an unparseable value is a `400`, never free text.

- [ ] **Step 3: Run, commit**

### Task 3.3: Mute API

**Files:**
- Create: `Features/Users/Endpoints/UserMuteEndpoints.cs`, `Features/Users/MuteView.cs`, `Features/Users/SetMuteHandler.cs`
- Test: `tests/Basil.IntegrationTests/UserMuteEndpointTests.cs`

**Interfaces:**
- Produces: `MuteView(bool IsMuted, DateTimeOffset? MutedUntil)`; `SetMuteRequest(DateTimeOffset? Until)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task MutingAUserTakesEffectOnTheirLiveSessionWithoutAReconnect()
{
	var session = await LogInAsync("victim");

	var response = await AdminClient.PutAsJsonAsync($"/users/{session.Id}/mute",
		new { until = DateTimeOffset.UtcNow.AddMinutes(10) });

	Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	Assert.True(SessionRegistry.GetById(session.Id)!.Silenced);
}

[Fact]
public async Task MutingSendsTheSilenceEndPacketToTheOnlineSession()
{
	var session = await LogInAsync("victim");
	await AdminClient.PutAsJsonAsync($"/users/{session.Id}/mute",
		new { until = DateTimeOffset.UtcNow.AddMinutes(10) });

	var packet = await session.NextPacketAsync(ServerPackets.SilenceEnd);
	Assert.InRange(packet.ReadInt32(), 1, 600);
}

[Fact]
public async Task AMuteEndInThePastIsRejected()
{
	var response = await AdminClient.PutAsJsonAsync("/users/2/mute",
		new { until = DateTimeOffset.UtcNow.AddMinutes(-1) });

	Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}

[Fact]
public async Task PuttingNullUnmutes()
{
	await AdminClient.PutAsJsonAsync("/users/2/mute", new { until = (DateTimeOffset?)null });

	var view = await AdminClient.GetFromJsonAsync<Envelope<MuteView>>("/users/2/mute");
	Assert.False(view!.Data!.IsMuted);
	Assert.Null(view.Data.MutedUntil);
}

[Fact]
public async Task MuteRequiresTheAdminKey()
{
	var response = await AnonymousClient.GetAsync("/users/2/mute");
	Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}
```

- [ ] **Step 2: Run, fail, implement**

The handler does four things, in this order, and the XML documentation says why: persist through `UpdateSilenceEndAsync`; update the user's live `GameSession` and `IrcSession` from the registries; send `ServerPacketWriter.SilenceEnd((int)remaining.TotalSeconds)` to the online game session; invalidate `CachingUserRepository` under both the id and the name key. A database-only write would not take effect until the user reconnects, because `SilenceEnd` is copied onto the session at login.

Only `until` is accepted — an absolute instant, so a retried `PUT` cannot push the expiry further out. `null` unmutes. A past instant is a `400`, not a silent unmute.

- [ ] **Step 3: Run, commit**

### Task 3.4: Remove the redundant `AdminKey:LastChanged` runtime stamp

- [ ] **Step 1: Delete `AdminKeyService.StampLastChangedAsync` and both calls**

The `Settings_AdminKeyHash_AfterUpdate` trigger in `001_base.sql:321` already stamps it on every `UPDATE` of the hash, and `SqliteSettingsRepository.SetAsync` is a plain `UPDATE`, so both the set and the clear paths are covered.

- [ ] **Step 2: Delete the two tests that asserted the runtime call**

`SetKeyAsync_AlsoStampsLastChanged` and `ClearAsync_AlsoStampsLastChanged` assert `Received(1).SetAsync("AdminKey:LastChanged", ...)` — an interaction, not a behavior. Replace with one integration test asserting the observable contract:

```csharp
[Fact]
public async Task RotatingTheAdminKeyChangesTheReportedLastChanged()
{
	var before = await GetStatusAsync();
	await RotateAsync();
	var after = await GetStatusAsync();

	Assert.True(after.LastChanged > before.LastChanged);
}
```

- [ ] **Step 3: Run, commit**

### Task 3.5: Logging pass over Users and Auth

### Task 3.6: `UserView`, docs (`privileges.md`, `database.md`), OpenAPI regeneration

Record the intended OpenAPI diff: `UserView.SilenceEnd` nullable, two new mute paths, the new search filter parameters.

### Task 3.7: Test triage for Users and Auth; checkpoint `Ready for review`

---

# Phase 4 — Beatmaps, Scores, Content

### Task 4.1: `ScoreDetailView` grouping

Group hit counts and accuracy onto the existing `Basil.Domain/Scores/HitCounts.cs`, and combo, grade and mods into their own records. A group is extracted only where it is read or written as a unit. If a nested JSON shape would break a client for no gain, keep the flat serialization through explicit mapping and group only the internal model — and say so in the commit.

Capture the OpenAPI diff before and after, and record it in the checkpoint.

### Task 4.2: Enum underlying types

`ComparisonOperator` gains `: byte`. Audit every remaining enum:

```bash
grep -rn "^public enum\|^\[Flags\]" src --include=*.cs | grep -v ": "
```

Any enum whose values fit in a smaller type and which is not on a wire or persistence contract gets the smaller type. **`UserPrivileges` and every enum serialized into a packet or a column keeps its current type** — narrowing those is a contract change.

### Task 4.3: Localization and logging pass over the three slices

### Task 4.4: Test triage, `beatmap-ingestion.md`, checkpoint `Ready for review`

---

# Phase 5 — Diagnostic API

Runs in a worktree, in parallel with Phase 1. Needs only Task 0.10's hub.

**The metric gate is already satisfied.** `plans/diagnostic-metric-inventory-20260907.md` is probe-backed input. Implement against it row by row; do not re-investigate .NET metric availability.

### Task 5.1: The `MeterListener` background service

The one global component this feature needs, and the one most at risk of becoming a memory-growth source — which would be a particularly bad outcome for a feature born out of a memory investigation.

**Files:**
- Create: `Features/Diagnostics/RuntimeMeterListener.cs`
- Test: `tests/Basil.Server.Tests/Features/Diagnostics/RuntimeMeterListenerTests.cs`

**Interfaces:**
- Produces: `long ExceptionsThrown`, `long ActiveRequests`, `long RequestsCompleted`, `long RequestsFailed`, `long ActiveConnections`, `long ConnectionsCompleted`, `DurationAggregate RequestDuration` — all read as plain fields by the snapshot builders.

- [ ] **Step 1: Write the failing lifetime and boundedness tests**

```csharp
/// <summary>
///     Exactly one listener exists for the process, owned by the host, never by a subscriber.
/// </summary>
[Fact]
public void TheListenerIsASingletonHostedService()
{
	using var provider = BuildServiceProvider();

	var a = provider.GetRequiredService<RuntimeMeterListener>();
	var b = provider.GetRequiredService<RuntimeMeterListener>();

	Assert.Same(a, b);
	Assert.Contains(provider.GetServices<IHostedService>(), s => ReferenceEquals(s, a));
}

/// <summary>
///     The accumulator's memory is fixed regardless of how many distinct exception types are
///     thrown. error.type carries arbitrary type names, so keying anything by it is unbounded
///     cardinality -- the totals are aggregated instead.
/// </summary>
[Fact]
public void TheAccumulatorDoesNotGrowWithDistinctTagValues()
{
	var listener = NewStartedListener();

	for (var i = 0; i < 10_000; i++)
		listener.RecordForTest("dotnet.exceptions", 1, [new("error.type", $"Type{i}")]);

	Assert.Equal(FixedFieldCount, listener.TrackedSeriesCount);
}

/// <summary>
///     Subscribers never create listeners. dotnet.exceptions and the ASP.NET Core meters are
///     push-based, so a listener started on demand would have no baseline and could not report a
///     rate for the interval an operator cares about -- and one listener per subscriber would
///     multiply the cost by connection count.
/// </summary>
[Fact]
public async Task OpeningManySubscriptionsCreatesNoAdditionalListener()
{
	var listener = NewStartedListener();
	var before = listener.StartCount;

    await using var a = Hub.Open(StreamKey.Diagnostic("gc"));
    await using var b = Hub.Open(StreamKey.Diagnostic("gc"));
    await using var c = Hub.Open(StreamKey.Diagnostic("process"));

	Assert.Equal(before, listener.StartCount);
}

[Fact]
public async Task StoppingTheHostDisposesTheListener()
{
	var listener = NewStartedListener();
	await listener.StopAsync(default);
	Assert.True(listener.Disposed);
}
```

- [ ] **Step 2: Run, fail, implement**

```csharp
/// <summary>
///     Accumulates the runtime and ASP.NET Core counters that cannot be point-sampled, so the
///     diagnostic snapshots can read them as plain fields.
/// </summary>
/// <remarks>
///     dotnet.exceptions exists only as a push-based counter on the System.Runtime meter -- there
///     is no property anywhere to read it from -- and the Microsoft.AspNetCore.Hosting and Kestrel
///     meters do not exist at all until hosting constructs them. Both were verified by probe. A
///     listener created per tick or per subscriber would therefore have no baseline and could
///     report no rate, so exactly one listener runs for the process lifetime and every subscriber
///     reads its accumulated fields.
///
///     Everything it accumulates is a fixed set of scalar fields. Nothing is keyed by a tag value:
///     error.type carries arbitrary exception type names, which would be unbounded cardinality and
///     a memory leak in a subsystem whose whole purpose is investigating memory growth.
/// </remarks>
public sealed class RuntimeMeterListener : IHostedService, IDisposable
```

Subscribe to `System.Runtime`, `Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`. Aggregate `http.server.request.duration` into a fixed-size `DurationAggregate` (count, sum, min, max, and a fixed bucket array) reset per broadcast interval.

- [ ] **Step 3: Run, commit**

### Task 5.2: The process and memory samplers

**Interfaces:**
- Produces: `ProcessSampler.Sample()` — refreshes one cached `Process` and returns every process and memory field from that single refresh.

- [ ] **Step 1: Write the failing sharing test**

```csharp
/// <summary>
///     One Process refresh serves every process and memory metric in a tick. A fresh
///     GetCurrentProcess measured about 4.0 ms and Refresh about 3.7 ms -- both syscalls -- so
///     refreshing per metric would make the diagnostic loop a measurable load on the server it
///     exists to observe.
/// </summary>
[Fact]
public void ASampleRefreshesTheProcessExactlyOnce()
{
	var sampler = new ProcessSampler(TestClock);

	_ = sampler.Sample();

	Assert.Equal(1, sampler.RefreshCountForTest);
}
```

- [ ] **Step 2: Implement per the inventory**

Hold one `Process`. Use `Environment.WorkingSet` for the always-live working-set value, since `Process.WorkingSet64` caches until `Refresh()`. Exclude `VirtualMemorySize64` — its meaning differs too much between Windows and Linux to put on a shared dashboard. Read `TotalAvailableMemoryBytes` and `HighMemoryLoadThresholdBytes` on a slow cadence, not per tick.

### Task 5.3: The `gc` and `threadpool` snapshots

`ThreadPool.PendingWorkItemCount`, `ThreadPool.CompletedWorkItemCount` and `Monitor.LockContentionCount` are plain static properties on .NET 10 (0.02–0.05 µs) — read them directly, no listener. Exclude completion-port thread values entirely: the probe read fixed legacy 1/1000/1000 on the portable thread pool, so they carry no signal. Present cumulative counters as cumulative, and derive rates by differencing between ticks.

`GCMemoryInfo` is a since-last-GC snapshot that reads zero before the first collection — document that on the response type, and derive a smoothed GC-time figure from `GC.GetTotalPauseDuration()` deltas rather than exposing `PauseTimePercentage` as a rolling percentage.

### Task 5.4: The `runtime` snapshot, read once and cached

Never broadcast. `GC.GetConfigurationVariables()` allocates a dictionary per call, so it is read once at startup.

### Task 5.5: The `application` snapshot

Active users, active sessions, active matches, active SSE subscribers per stream, active timers, eventing counters. Basil semantics only — no runtime metric belongs in this category.

### Task 5.6: Endpoints

`GET /diagnostic/{category}`, `GET /diagnostic/{category}/live`, `GET /diagnostic/live`, `POST /diagnostic/{category}/{action}` — all under `RequireAuthorization(AdminKeyDefaults.Policy)`.

- [ ] **Step 1: Write the failing contract tests**

```csharp
[Fact]
public async Task EveryDiagnosticRouteRequiresTheAdminKey()
{
	foreach (var path in AllDiagnosticPaths())
		Assert.Equal(HttpStatusCode.Unauthorized,
			(await AnonymousClient.GetAsync(path)).StatusCode);
}

[Fact]
public async Task EverySseDiagnosticRouteEndsInLive()
{
	foreach (var path in DiagnosticSseRoutePatterns())
		Assert.EndsWith("/live", path, StringComparison.Ordinal);
}

/// <summary>
///     Static metadata is not re-sent every second; a live payload carries only values that move.
/// </summary>
[Fact]
public async Task LivePayloadsCarryNoStaticRuntimeMetadata()
{
	var payload = await FirstLiveEventAsync("/diagnostic/memory/live");

	Assert.DoesNotContain("frameworkDescription", payload, StringComparison.OrdinalIgnoreCase);
	Assert.DoesNotContain("processorCount", payload, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task LiveEventsArriveAboutOncePerSecond()
{
	var timestamps = await TakeLiveTimestampsAsync("/diagnostic/gc/live", 4);

	foreach (var gap in Gaps(timestamps))
		Assert.InRange(gap, TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(1400));
}

/// <summary>
///     Diagnostics is not a maintenance system: an action invokes a runtime operation and returns
///     its result. It never returns a handle to work that continues afterwards.
/// </summary>
[Fact]
public async Task ADiagnosticActionReturnsItsResultAndCreatesNoFollowUpResource()
{
	var response = await AdminClient.PostAsync("/diagnostic/gc/collect", null);

	Assert.Equal(HttpStatusCode.OK, response.StatusCode);
	Assert.Null(response.Headers.Location);
	var body = await response.Content.ReadAsStringAsync();
	Assert.DoesNotContain("\"id\"", body, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Implement, using the hub for the live streams**

One collection per tick per category, broadcast to every subscriber; with no subscribers, no collection. The `MeterListener` from Task 5.1 keeps running regardless — that guard applies to snapshot building, not to the listener, whose baseline would be destroyed by stopping it.

### Task 5.7: Overhead measurement

- [ ] **Step 1: Measure and record in the checkpoint**

Idle process CPU and allocation rate with no diagnostic subscribers; with one `/diagnostic/live` subscriber; with ten. If the marginal cost of a subscriber is not near zero, the collection is not being shared and the implementation is wrong.

### Task 5.8: Document, commit, checkpoint `Ready for review`

---

# Phase 6 — Load harness

Depends on Phase 5 for the diagnostic client.

### Task 6.1: Benchmark the flush strategy before choosing one

Crash resilience argues for flushing every record; a run that samples several sources for tens of hours argues that per-record flushing could make the load generator's own disk I/O a bottleneck. Measure rather than assume.

**Files:**
- Create: `plans/execution/flush-benchmark.md`

- [ ] **Step 1: Write a benchmark harness**

Append a representative sample record (the same shape `ResourceSample` serializes to) at the real combined sampling rate, for a fixed duration, under three strategies: flush per record; flush per 16-record batch; flush on a 1-second timer. Measure wall-clock cost per record, total bytes written, and the number of records lost when the process is killed with `SIGKILL` mid-run.

- [ ] **Step 2: Choose on the evidence and record it**

The requirement is that a crash at any instant retains data up to approximately that instant. If per-record flushing costs nothing measurable at the real rate, take it — it is the simplest and loses nothing. If it does cost, take the cheapest strategy whose measured loss window stays under one second, and write down the measured numbers that justified it.

- [ ] **Step 3: Commit the benchmark and its conclusion**

### Task 6.2: Stream every sample to disk as it is taken

**Files:**
- Modify: `tests/Basil.LoadTests/Infrastructure/Metrics/ResourceTimeline.cs`, `Infrastructure/Reporting/ReportWriter.cs`, `Program.cs`
- Create: `tests/Basil.LoadTests/Infrastructure/Reporting/JsonlStream.cs`

- [ ] **Step 1: Write the failing crash-resilience test**

```csharp
/// <summary>
///     A run killed partway through keeps every sample it had already taken. The 24h soak died at
///     13h42m and lost all of it, because aggregation only ran at the end.
/// </summary>
[Fact]
public async Task SamplesSurviveAProcessKill()
{
	var directory = NewRunDirectory();
	using (var process = StartSamplingHarness(directory))
	{
		await WaitForRecordsAsync(directory, atLeast: 50);
		process.Kill(entireProcessTree: true);
	}

	var records = ReadJsonl(Path.Combine(directory, "resources.jsonl"));
	Assert.True(records.Count >= 50);
	Assert.All(records, r => Assert.NotEqual(default, r.TimestampUtc));
}
```

- [ ] **Step 2: Implement four append-only streams**

`load.jsonl`, `resources.jsonl`, `diagnostics.jsonl`, `events.jsonl`, all sharing a UTC timestamp field so they correlate. `ResourceTimeline` writes through instead of accumulating; `ReportWriter` aggregates over what is already on disk.

### Task 6.3: Consume the Diagnostic API into `diagnostics.jsonl`

The harness polls `/diagnostic/live` with the admin key at its own cadence and appends each payload with its receipt timestamp. Record in `load-testing.md` how to correlate the four streams.

### Task 6.4: Verify, document, commit, checkpoint `Ready for review`

Kill a real run mid-flight and confirm every stream retains data to that instant. Confirm a load metric and a diagnostic sample taken in the same second line up on one timeline.

---

# Phase 7 — Final sweep

### Task 7.1: Full verification

```bash
dotnet build --configuration Release
dotnet test --configuration Release
dotnet test tests/Basil.ArchitectureTests
```

Diff every Task 0.1 baseline artifact one final time. Every remaining difference must be traceable to a task that intended it — list them in the checkpoint with the task number.

### Task 7.2: Documentation reconciliation

`docs/index.md` matches reality. `known-limitations.md` records that RC5 remains SUPPORTED and that the soak was not rerun, and that the empty-room jitter closed no investigation item.

### Task 7.3: Review the whole diff for unrelated changes

```bash
git diff <phase-0-base-sha>..HEAD --stat
```

Anything with no connection to a task is removed.

### Task 7.4: Final advisor review

Against the actual diff, not this plan.

---

## Self-Review

**Spec coverage.** Walked each spec section against the task list. Section 2 findings — 2.1 Task 0.13; 2.2 Task 4.2; 2.3 Task 3.4; 2.4 Task 1.3; 2.5 Task 0.9; 2.6 Tasks 0.8/1.8/2.2/3.1/4.3; 2.7 Tasks 0.6/0.7/0.8/0.13 and 1.11. Section 3 — 0.2/0.3/0.4/0.5. Section 4 — 4.1 Tasks 0.10/1.3; 4.2 Tasks 0.10/1.3; 4.3 Task 1.3 Step 5; 4.4 Tasks 1.1/1.2; 4.5 Tasks 0.8/1.8/2.2; 4.6 Tasks 1.9/2.3/3.5/4.3; 4.7 Task 0.9. Section 5 — Task 1.4. Section 6 — 0.5/1.5/1.6/1.7/2.1. Section 7 — 0.13/3.1/3.2/3.3. Section 8 — inside each phase, plus 7.2. Section 9 — 0.11/0.12 and the per-slice triage tasks. Section 10 — Phase 5. Section 11 — Phase 6. Sections 12–13 — the Execution Model section and the checkpoint tasks. No gap found.

The four review constraints carried in: partial-mutation invariant is Task 1.1, with a decision branch that can change the design; `MeterListener` lifetime and boundedness is Task 5.1, with four tests; the flush-strategy benchmark is Task 6.1, deciding on measurement; the stronger Phase 0 baseline is Task 0.1 with verification in Tasks 0.6, 0.7, 0.13 and 0.14.

**Placeholder scan.** No "TBD", no "add appropriate error handling", no "similar to Task N". Tasks 1.5–1.7, 2.2–2.5, 3.5–3.7, 4.1–4.4 and 5.2–5.5 are stated as decomposition and sweep work whose per-file content is fully determined by the enumerated member lists and the patterns established in Tasks 1.2–1.4 and 5.1 — where a task needs a novel decision, it has its own test-first steps.

**Type consistency.** `LocaleCatalog.Get`/`AllKeys`/`ReferencedKeys` used consistently in 0.8, 1.8, 3.1. `StreamKey` in 0.10, 1.3, 5.1, 5.6. `hub.HasSubscribers`/`MarkStale`/`Publish`/`Open` and `LiveSubscription.SeedIfNotSuperseded` consistent between 0.10 and 1.3. `BeginMutationAsync`/`PublishState`/`AllocatedVersion`/`CompleteAsync` consistent between 1.2 and 1.3. `UpdateNameAsync` loses `safeName` in 0.13 and is never called with it afterwards. `UpdateSilenceEndAsync` defined in 0.13, used in 3.3. `MuteView` defined once, in 3.3.

---

## Execution Handoff

Plan complete and saved to `plans/vsa-migration-plan-20260907.md`. Two execution options:

**1. Subagent-Driven (recommended)** — a fresh subagent per task, reviewed between tasks, fast iteration. Fits this plan well: tasks are independently testable and the checkpoint files give each fresh subagent its context without the session that spawned it.

**2. Inline Execution** — execute tasks in this session with checkpoints for review. Lower token cost per task, but a five-hour session limit will interrupt it, so the checkpoint discipline in the Execution Model section matters more.

Which approach?
