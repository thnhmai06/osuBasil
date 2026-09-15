# Stage D — split the transports

Tracks `plans/basil-plan-20260909.md`'s Stage D (D1–D4). Started 2026-09-15, after C1b and Stage G
were both done and C5's post-C1b measurement (recorded in `architecture-progress.md`) was reported to
the user as Stage D's gate. The user's call: proceed into Stage D, with Task C2's deferred `GameSession`
split folded into D2 rather than run as its own unit first — this is what the plan's own C2 write-up
already implies (`Enqueue`/`Dequeue` -> the bancho transport, `IrcConnection` -> the IRC transport,
both landing naturally when `Features/Irc` and the packet handlers move to their host projects in D2).

## Task D1: split `Basil.Protocol` in two — done

Moved (`git mv`, physical, no content change to any `.cs` file):

* `Basil.Protocol.Bancho` — `AssemblyMarker.cs`, `BanchoMessage.cs`, `LoginFailureReason.cs`,
  `Packets/*` (5 files), `Multiplayer/*` (5 files), `Binary/BinaryWriter.cs`.
* `Basil.Protocol.Irc` — `IrcMessage.cs`, `IrcMessageParser.cs`, `IrcMessageWriter.cs`,
  `IrcNumeric.cs`, plus a new `AssemblyMarker.cs` (the old project had exactly one, shared; each half
  now needs its own for `DependencyDirectionTests` to bind an assembly per project).

**Namespace design deviates from what the plan's phrasing (`Basil.Protocol.Tests` needs only
"namespace-only edits") suggested.** Read literally, that implies production namespaces do change to
`Basil.Protocol.Bancho.*` -- consistent with the repo's own convention that `RootNamespace` always
equals the project name (verified: `Basil.IntegrationTests` and `Basil.LoadTests` are the only two
projects that set `RootNamespace` explicitly, and both set it to their own project name). But 138
files reference the bancho-side namespaces (`Basil.Protocol`, `.Packets`, `.Multiplayer`, `.Binary`)
via `using`, all in `Basil.Server` and its tests -- a 138-file rename is a large diff for zero
behavior change, and breaks this migration's own established rhythm of small, individually-verifiable
units. Chose instead: `Basil.Protocol.Bancho.csproj` sets `<RootNamespace>Basil.Protocol</RootNamespace>`
explicitly (with a comment explaining the deviation), so every consumer's `using` list is untouched.
`Basil.Protocol.Irc` needed no override -- its files were already namespaced `Basil.Protocol.Irc`,
which is also its project's default `RootNamespace`. Net effect: zero `.cs` changes outside the two
new `AssemblyMarker.cs` files and `DependencyDirectionTests.cs`.

Updated:

* `Basil.slnx` — nested `/Sources/Protocol/` folder holding both new projects, replacing the single
  `Basil.Protocol` entry.
* `Basil.Server.csproj`, `Basil.Server.Tests.csproj`, `Basil.ArchitectureTests.csproj`,
  `Basil.Protocol.Tests.csproj` — `ProjectReference` swapped from the one old project to both new
  ones (all four consume both halves: `Basil.Server` has both bancho packet handlers and
  `Features/Irc` today, `Basil.Protocol.Tests` covers both, `Basil.ArchitectureTests` needs both
  `AssemblyMarker` types).
* `Basil.LoadTests.csproj` — only `Basil.Protocol.Bancho` (measured: no `Irc`/`IrcMessage`/
  `IrcNumeric` reference anywhere under `tests/Basil.LoadTests`).
* `DependencyDirectionTests.cs` — the one `Protocol_Should_Not_HaveDependencyOn_AnyOtherBanchoProject`
  test split into `ProtocolBancho_...` and `ProtocolIrc_...`, each checked against its own assembly.

Verified: `dotnet build` (Debug, 0 errors) and `--configuration Release` (0 errors, production-code
change per the final-verification checklist), `Basil.ArchitectureTests` 9/9 (was 8; +1 for the split
dependency-direction test), `Basil.Protocol.Tests` 158/158 (unchanged count, confirming the "no `.cs`
change" claim held), `Basil.Domain.Tests` 235/235, `Basil.Server.Tests` 944/944, full
`Basil.IntegrationTests` 363/363 (unchanged; the four `Test Class Cleanup Failure` lines in the run
output are the same pre-existing xUnit `v3` teardown-ordering flake unrelated to this change — pass
count is what the suite asserts).

`src/Basil.Protocol/` still holds an untracked `bin/`/`obj/` from before the split; harmless
(gitignored), left for the next full clean rather than force-deleted.

## Task D2: create the three host projects — investigated, not yet executed

### The missing fifth project

`basil-plan-20260909.md`'s D2/D4 checklist never says where the composition root (`Bootstrap.Main`,
`WebApplication.CreateBuilder`, the single Kestrel process that binds every host today) lands once
`Basil.Server` becomes a library. `architecture-target-20260908.md` §3.1 already names the answer —
a fifth project, `Basil.Host`, referencing everything, with the rule "no host references another
host" — but the operational plan's D2/D4 task text doesn't carry that instruction forward. Read as
written, D2/D4 would have the three new host projects reference `Basil.Infrastructure` for
persistence/session types while `Basil.Infrastructure` (today's `Basil.Server`, still holding
`Bootstrap` and `SliceRegistration.MapAll`) needs a reference *back* to those same host projects to
compose and map them — a circular `ProjectReference`, which the SDK refuses to build, not merely an
architecture-test failure. `Basil.Host` has to exist, and has to hold the composition root, before
D2's first host project can be created without that cycle.

This does not need a user decision — the target doc already names the one correct shape, the plan's
operational checklist just omitted the step that gets there. Documenting it here rather than asking,
same as C6 and the C1a transport-seam finding earlier in this migration.

### Measured blast radius of extracting `Basil.Host`

Investigated 2026-09-15, nothing moved yet. `src/Basil.Server/Host/` is 19 files
(`Bootstrap.cs` and everything `Bootstrap.Main` calls: `SliceRegistration`, the eight `*Setup.cs`
pipeline-configuration files, `CommandLine`, `StartupData`, `StartupBanner`, the Velopack update
probe pair, `BuildVersion`, `DomainAdvertiser`, `LocaleTouch`,
`SharedInfrastructureServiceCollectionExtensions`, `ConfigurationSetup`). Sixty-four files reference
`Basil.Server.Host` or `Bootstrap` by name; the overwhelming majority (~40 `Basil.IntegrationTests`
files using `WebApplicationFactory<Bootstrap>`, 4 `Basil.Server.Tests/Host/*Tests.cs` files, 2
`Basil.ArchitectureTests` files) need no source change if the new project keeps the same
`Basil.Server.Host` namespace — same `RootNamespace`-pin design as D1's `Basil.Protocol.Bancho`, for
the same reason (avoid a wide rename for zero behavior change).

**One real straggler, already root-caused:** `BanchoProtocolRoutes.cs` (today in
`Basil.Server/Shared/Http/`, itself bancho-host material that D2 moves to `Basil.Hosts.Bancho`) pulls
`ILogger<Bootstrap>` purely to get a logger whose `SourceContext` matches `CategoryEnricher`'s
`("Basil.Server.Host.Bootstrap", false, "Host")` rule and gets bucketed into the "Host" log category.
Once `Bootstrap` lives in a project neither `Basil.Server` (soon `.Infrastructure`) nor
`Basil.Hosts.Bancho` may reference, this call site cannot compile. Fix, not yet applied: replace it
with `loggerFactory.CreateLogger("Host")` (no type reference needed) and add a matching exact-match
rule `("Host", false, "Host")` to `CategoryEnricher.Rules`. Same observable category, zero project
coupling. CLAUDE.md rule 8 already excludes log categories from the tested-contract rules, so this
is not a behavior change that needs a regression test, only the existing category-mapping preserved.

**`Basil.Server.csproj` is far larger than the 19-file move implies.** Read in full: it is
`Sdk="Microsoft.NET.Sdk.Web"` with no explicit `OutputType` (the Web SDK defaults it to `Exe`),
carries `ApplicationIcon`/`ApplicationManifest` (Windows-specific), `SelfContained=true`, every
`PackageReference` the whole server needs (including host-only ones — `Velopack` for the updater,
`Serilog.AspNetCore`, `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` for the API docs UI,
`SixLabors.ImageSharp.Web` for the image middleware), Content items that are Host-shaped
(`Data/appsettings.json`, `docs-site/**`, the HTTPS dev cert, `ApplicationIcon`) alongside ones that
are feature-shaped (per-slice `Locale/*.json`, the two embedded avatar PNGs `BotBootstrapService`
reads), and a `RemoveUnusedOsuRulesetRuntimeFiles` MSBuild target hooked to `AfterTargets="Publish"`
— publish-output cleanup that only makes sense on whichever project is the actual executable.
None of this is itemized in D2 or D4's checklist. Extracting `Basil.Host` means deciding, file by
file and package by package, what moves with the entry point and what a library project keeps — this
is not a mechanical `git mv`, it is closer in size to one of C1b's own units, and deserves the same
treatment: scoped and executed as its own dedicated step, not folded silently into "create the three
host projects."

**Also touches deployment, not just source:** `Dockerfile` (`dotnet publish src/Basil.Server`,
`ENTRYPOINT ["./Basil.Server"]`), `.github/workflows/release.yml` (same publish path), and
`tests/Basil.LoadTests/Hosting/DotnetServerHost.cs` (hardcodes `Basil.Server.exe`/`Basil.Server` as
the subprocess binary name, twice) all assume the executable is named `Basil.Server`. Whichever
project becomes the real entry point is what these three need to point at — verify this is `Basil.Host`
once it exists, not assume it silently.

### Next action

Execute "extract `Basil.Host`" as its own unit, in this order: (1) decide the `Basil.Server.csproj`
split — Host-only settings (`Sdk.Web`, `OutputType`/`SelfContained`, icon/manifest, the publish-cleanup
target, `Data/appsettings.json`, `docs-site/**`, the dev cert, `Velopack`) move to the new
`Basil.Host.csproj`; everything else (feature packages, per-slice locale content, the two avatar
embedded resources) stays on `Basil.Server`; (2) `git mv` the 19 `Host/*.cs` files, pin
`Basil.Host.csproj`'s `RootNamespace` to `Basil.Server.Host`; (3) fix the one `BanchoProtocolRoutes.cs`
straggler as designed above; (4) `Basil.Server.Tests/Host/*Tests.cs` (4 files: `CompositionRootTests`,
`CommandLineTests`, `StartupUpdateCheckTests`, `ConfigurationSourceTests`) move to a new
`Basil.Host.Tests` project, mirroring the source split; (5) update `Basil.slnx`,
`Basil.IntegrationTests.csproj`/`Basil.ArchitectureTests.csproj` project references; (6) update
`Dockerfile`, `release.yml`, `DotnetServerHost.cs` to publish/run `Basil.Host`; (7) full verification
per CLAUDE.md (`dotnet build` Debug + Release, all five fast projects, full
`Basil.IntegrationTests`, and a manual `dotnet run --project src/Basil.Host` smoke check since this
is the one change in the whole migration that can silently break the thing that actually serves
traffic).

Once `Basil.Host` exists and the composition root is out of `Basil.Server`, the three D2 host
projects (`Basil.Hosts.Bancho` first, per the `Features/Irc` pinned-list proof already available;
then `Basil.Hosts.Bancho`'s 46 handlers, folding the deferred `GameSession.Enqueue`/`Dequeue`/
`IrcConnection` split in as they move, per the user's 2026-09-15 direction; then `Basil.Hosts.Api`
with D3 immediately after) can each reference `Basil.Infrastructure` without creating a cycle, since
`Basil.Host` — not `Basil.Infrastructure` — is what will reference all three.

## Task D3: `AnnounceRoutes` — not started

## Task D4: rename `Basil.Server` to `Basil.Infrastructure` — not started

Partly entangled with the `Basil.Host` extraction above: once Host-only csproj settings and content
move out, what remains on `Basil.Server` is closer to what D4 describes ("persistence adapters,
storage, media and external services"), so the rename itself becomes a smaller, cleaner step done
after, not a reason to do it first.
