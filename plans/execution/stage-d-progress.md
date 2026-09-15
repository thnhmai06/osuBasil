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

## Task D2: create the three host projects — not started

## Task D3: `AnnounceRoutes` — not started

## Task D4: rename `Basil.Server` to `Basil.Infrastructure` — not started
