# W5: the calculation stack in `Data/Rulesets/` (plan P5)

Source of truth: `plans/review/release-1.0.md` decisions D39, D40, D41, plan P5 (contract, build time, start-up,
update, code changes, verify) and findings F-111, F-115, F-107. Plan P5 is the spec: follow its order. Read `AGENTS.md`
("Optional features never stop the server", "pp is display-only"). W1, W3, W4 are committed.

## Step 0: spike, and a hard stop

Before any production change, prove the approach in a throwaway console project under the scratch folder
`/tmp/claude-1000/-mnt-shared-Development-osuBasil/63042d0f-8f87-48ed-b9ed-d5f8368fa49d/scratchpad/spike-rulesets/`
(never inside the repository):

1. It references the four `ppy.osu.Game.Rulesets.*` packages at the version in `Directory.Packages.props` with
   `ExcludeAssets="runtime"`, so nothing of osu! is copied next to it.
2. It registers `AssemblyLoadContext.Default.Resolving` to load assemblies by name from a folder you fill by extracting
   the `lib/<tfm>/` assemblies of the needed packages from the local NuGet cache (`~/.nuget/packages`).
3. It decodes a `.osu` file of each mode (find samples under `tests/Assets` or any `.osz` in the repository; otherwise
   write a minimal `.osu` by hand), calculates difficulty and the pp of a made-up score with `Basil.Infrastructure.Rulesets`'s
   own calls as the model (`OsuBeatmapCalculator`, `OsuBeatmapAnalyser`), and prints every assembly loaded
   (`AppDomain.CurrentDomain.AssemblyLoad`) with its package.

**If any step needs a native library (a `runtimes/<rid>/native/*` file, a `DllImport` that fails) or the calculation
fails for a reason not fixable in the resolver, stop the whole wave: change nothing in the repository and report what
failed.** Otherwise keep the list of loaded assemblies: it is the first manifest.

## Steps 1 to 7 (P5 "Code changes" 2 to 7, in that order)

1. **References.** `Basil.Infrastructure.Rulesets` and `Basil.Infrastructure.Beatmaps` reference the `ppy.*` packages
   with `ExcludeAssets="runtime"`; the publish output and the image carry no `osu.*`/`ppy.*` file (also drop the
   `_UnusedRuntimeFile` list in `Basil/Basil.csproj` that this makes moot, and the `NoWarn`/advisory notes in
   `Directory.Build.props` that only existed for those packages, if none remains).
2. **Manifest.** A small tool project under `tools/` (look at what `tools/` holds and match it) that runs the spike's
   calculation and decoder over sample beatmaps of the four modes with a spread of mods, records every loaded assembly,
   maps it to package id, version and path inside the package, hashes each file (SHA-256), and writes
   `Basil.Infrastructure.Rulesets/rulesets.manifest.json`. The file is committed and embedded as a resource.
3. **`RulesetStore`** (`Basil.Infrastructure.Rulesets/Storage/RulesetStore.cs`): the stack lives in
   `Data/Rulesets/<version>/`. Before any osu! type is touched (today `LoadStaged` is called first thing in
   `HostApplication.Create`; keep that place), register the `Resolving` handler for the manifest's assembly names and
   check the folder against the manifest (presence and hash): `Installed`, `Missing`, `Damaged`. The staged/`current`
   pointer logic for a different version goes (the version is the compiled one, D39; F-113 moot). The state is logged
   once, exposed where the ruleset version is answered today (`GetRulesetVersion`, `GET /v1/version`, the dashboard), and
   anything but `Installed` turns the feature off without stopping the server. Hashes are checked on every start
   (F-115).
4. **`RulesetUpdater`** (`Basil.Infrastructure.Rulesets/Services/RulesetUpdater.cs`): through `IHttpClientFactory` with a
   timeout (F-107; also fix `new HttpClient` at `Basil.Infrastructure.Rulesets/DependencyInjection.cs:23`), for each
   manifest package download the `.nupkg` from nuget.org at the pinned version (flat container), check the package hash
   the feed publishes, extract only the listed files into a staging folder inside `Data/Rulesets/`, check each file's
   SHA-256, then one rename into `<version>/`. Any failure keeps nothing and answers which package and why (an
   unreachable feed maps to 502 on the REST route). After success: if no osu! type was loaded yet, the feature turns on
   at once; if it was (a repair), the answer says to restart the container. Then run the incoming beatmapset scan
   (pending `.osz`, D41) and answer what to do next (reload the beatmapsets, recalculate pp). Route and console texts
   (`RulesetRoutes`, `Basil.Host.Cli/Commands/RulesetCommands.cs` `RulesetReplies`) are rewritten for this flow; keep
   them short and plain (the reviewer runs them through the humanizer).
5. **Feature off.** The analyser, calculator and beatmapset reader ports answer "not available" instead of throwing when
   the stack is not installed: star ratings and pp stay `null`; an import is refused with a reason (an outcome or
   result value, whichever the port already uses) and the `.osz` stays queued in the incoming folder so the scan after
   the update imports it (D41).
6. **`OsuBeatmapAnalyser`** counts hit objects without ruleset types (D39), if the decoder alone gives what it needs.
7. **Docs.** `docs/for-technicians/configuration.md`, `docker.md` (first-run step: run the ruleset update; copying
   `Data/Rulesets/` from another install works offline), `docs/for-developers/architecture.md` or the rulesets page that
   owns this topic per `docs/index.md`.

## Rules for you

Rider MCP (`search_symbol`, `find_usages`, `safe_delete`, `get_file_problems`, `build_solution_*`), OpenCode Go at
`http://localhost:4096` with cheap flash models for mechanical edits only. You may run the spike and the manifest tool;
otherwise build only (`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no
commit, no `git add`, do not touch `Basil/Data/appsettings.json` (local skip-worktree copy). AGENTS.md code rules apply.
A step needing an unwritten decision: skip it, finish the rest, report.

## Report

Spike result and the loaded-assembly list (count, any surprise); per step the files changed; skipped steps and why;
decisions you took; build result; whether the publish output still contains any `osu.*`/`ppy.*` file
(`dotnet publish Basil -c Release -r linux-x64 -o <scratch>` and list).
