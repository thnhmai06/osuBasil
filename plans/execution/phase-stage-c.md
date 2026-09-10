# Stage C worker checkpoint

> Read this file first. It is kept current in the same commit as every green step, so a
> successor can resume from here without reconstructing state from `git log` and a build.

## Current task: C4 — move `MpCommandService` and `MpReplies` into Multiplayer

Order is C4 → C3 → C2 → C1 → C5 (see `plans/execution/stage-c-order-decision.md`). This file
tracks C4 only; a later worker doing C3/C2/C1/C5 should create sibling sections or a new file
per the orchestration doc's convention.

### Plan for C4 (three commits)

1. **Commit 1 (done)** — pure move: `MpCommandService`/`MpReplies` relocate to
   `Basil.Server.Features.Multiplayer`, no behavior or signature change.
2. **Commit 2 (done)** — split `MpReplies`: the eight members that back `!roll`/`!where`/`!faq`
   moved to a new `Bot.BotReplies`, wording and locale keys unchanged. `Features/Bot/Locale/
   bot.en.json` now holds only those eight keys; the rest moved with the code to a new
   `Features/Multiplayer/Locale/mp.en.json`.
3. **Commit 3 (not started)** — collapse `Bot -> Multiplayer` to one contract. Design decided (see
   below); not yet implemented.

### Commit 1 — what was done

- `mcp__rider__move_type_to_namespace` moved `MpCommandService` and `MpReplies` from
  `Basil.Server.Features.Bot` to `Basil.Server.Features.Multiplayer` (namespace + all
  references/usings across the solution). Rider does not relocate the physical file, so each was
  followed by `git mv` into `src/Basil.Server/Features/Multiplayer/`.
- `mcp__rider__move_type_to_namespace` also moved the test class `MpCommandServiceTests` into
  `Basil.Server.Tests.Features.Multiplayer`, followed by `git mv` into
  `tests/Basil.Server.Tests/Features/Multiplayer/MpCommandServiceTests.cs`. (The handler-split
  precedent of leaving tests in place does not apply here — those splits kept the type in the same
  slice; this one changes slice.) `CommandDispatcherTests.cs` stays in `Features/Bot/`: it tests
  `CommandDispatcher`, which stays in Bot.
- `services.AddSingleton<MpCommandService>()` moved by hand from
  `BotServiceCollectionExtensions.AddBot` to `MultiplayerServiceCollectionExtensions.AddMultiplayer`
  — the registration belongs with the type now. `CommandDispatcher` still resolves the concrete
  `MpCommandService` from DI exactly as before (interface introduction is Commit 3).
- Removed now-unused `using Basil.Server.Features.Bot;` from `Host/LocaleTouch.cs` and
  `tests/.../Shared/Localization/ReplyLocaleTests.cs` (both only needed it for `MpReplies`, which
  moved; both still use `IrcReplies` from `Features.Irc`, already imported).
- `SliceAdjacency.cs`: removed the `("Bot", "Beatmaps")` row (was carried by `MpCommandService`
  only; confirmed gone by grep before the move). **Did not remove `("Bot", "Irc")`** — see the
  finding below. Updated the comments on `("Bot", "Chat")`, `("Bot", "Multiplayer")`,
  `("Bot", "Users")`, `("Multiplayer", "Beatmaps")`, `("Multiplayer", "Chat")`,
  `("Multiplayer", "Irc")`, `("Multiplayer", "Users")`, `("Multiplayer", "Bot")` to name
  `MpCommandService`/drop it as appropriate, so the file stays accurate. Net: 46 → 45 allowed
  tuples (one row removed).
- Left `Features/Bot/Locale/bot.en.json` physically in place for this commit — the csproj glob
  copies it into `Data/Localization/` regardless of which slice folder it lives under, so its
  location doesn't affect runtime, and splitting its content belongs with Commit 2's `MpReplies`/
  `BotReplies` split, not this pure move.

### Finding: the `Bot -> Irc` prediction was wrong, and the fix is to keep the row

`plans/execution/stage-c-order-decision.md`'s table says `Bot -> Irc` is carried by
`MpCommandService` only and should be gone after C4. That table was built from
`measure-slice-graph.py`, which is a **text scan** for the literal string
`Basil.Server.Features.Irc`. `CommandDispatcher.cs`'s nested `ScopedDmReplySink.Reply()` calls
`sender.IrcConnection.Send(...)` — `IrcConnection` is declared `IIrcConnection`, a
`Basil.Server.Features.Irc` type, but that type name is never spelled in `CommandDispatcher.cs`
(no `using`, no FQN — it's an inferred property type). The text scanner cannot see this
dependency; `NetArchTest` (used by `SliceBoundaryTests`, IL-based) can and does — removing
`("Bot", "Irc")` failed
`SliceBoundaryTests.Slices_Should_Only_Reference_Declared_Slices` with the failing type
`CommandDispatcher.ScopedDmReplySink`. **This is a real, pre-existing dependency, unrelated to the
`MpCommandService` move** — the original row's own comment already said so
("`MpCommandService and CommandDispatcher's ScopedDmReplySink reply over Irc.IIrcConnection`"),
which I misread as fully attributable to `MpCommandService` when reading the prediction table.

Consequence for the two instruments:
- `measure-slice-graph.py` (text-based, what C5 gates on): `Bot -> Irc` reads as **gone** in both
  features-only and solution-wide counts, because `CommandDispatcher.cs` never spells the
  namespace. Confirmed empirically — see the numbers below.
- `SliceAdjacency` (IL-based, what the build enforces): `("Bot", "Irc")` **must stay**, because the
  compiled dependency is real.

This is not a new `Multiplayer -> X` edge (the task's stop condition) — it is a previously-declared
`Bot -> Irc` edge whose real carrier was misattributed. Restoring the row was the correct fix, not
a workaround: removing it would make `SliceBoundaryTests` lie about the actual coupling.
`Bot -> Beatmaps` and `Bot -> Scores` had no such hidden carrier (verified: no property/field on
`UserSession`, `ICommandReplySink`, or anything `CommandDispatcher` touches returns a Beatmaps or
Scores type) and are confirmed genuinely gone by both instruments.

### Commit 1 verification (all green)

- `dotnet build --configuration Debug`: 0 errors.
- `Basil.ArchitectureTests`: 6/6 (was failing 5/6 before restoring `("Bot", "Irc")` — see above).
- `Basil.Domain.Tests`: 114/114.
- `Basil.Protocol.Tests`: 158/158.
- `Basil.Server.Tests`: 1052/1052.
- `Basil.IntegrationTests`: 363/363 (one benign `[Test Class Cleanup Failure]` log line from
  `MotdSettingsManagementEndpointTests` teardown, unrelated to this change — 0 failed reported).
- Route count: `grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Server --include=*.cs | sort -u | wc -l` → **140**, unchanged. No route-shaped file was touched this commit.
- `measure-slice-graph.py`: features-only **45 → 43**, solution-wide **53 → 50**. Both counts fell
  by more than the `SliceAdjacency` row count fell (2 rows' worth of measured edges vs. 1 row
  actually removed from the allowlist) — expected, since the script and the allowlist count
  different things (see finding above).
- `SliceAdjacency.Allowed`: 46 → 45 tuples (`("Bot", "Beatmaps")` removed; `("Bot", "Irc")` kept).

### Commit 3 design (decided, not yet implemented)

Goal: `Bot -> Multiplayer` carried by **one** contract type instead of `MpCommandService`
(concrete, sealed, 27+ internal Multiplayer types reachable through it) plus `CommandDispatcher`'s
own direct use of `MatchSession`/`IMatchRegistry` for scope resolution and referee gating.

- New `IMpCommandService` interface in `Basil.Server.Features.Multiplayer`, implemented by
  `MpCommandService`, with exactly two members:
  - `Task<bool> DispatchAsync(UserSession sender, string subcommand, string[] subArgs, int? channelScopeMatchDbId, string? channelName, ICommandReplySink sink, CancellationToken cancellationToken = default)`
  - `Task<bool> DispatchChainAsync(UserSession sender, IReadOnlyList<(string Text, bool RequiresPreviousSuccess)> segments, int? channelScopeMatchDbId, string? channelName, string prefix, ICommandReplySink sink, CancellationToken cancellationToken = default)`
- `CommandDispatcher.DispatchMpAsync` and `DispatchChainAsync` — including `ResolveScope`,
  `BuildDmRedirectSink`/`ScopedDmReplySink`, and the chain referee gate — move bodily (Move
  Method, same logic, same replies) into `MpCommandService`, which already depends on
  `IMatchRegistry`/`IChannelRegistry`/`ISessionRegistry<GameSession>`; it gains
  `ChannelMembershipService` as a new constructor dependency (already an allowed
  `Multiplayer -> Chat` edge) to build the DM-redirect sink.
  `MakeAsync`/`JoinAsync`/`SetScopeAsync`/`TryHandleAsync`/`HelpText` stay public on the concrete
  class (unchanged signatures — existing `MpCommandServiceTests.cs` coverage needs no edits) but
  drop off the public contract, since `CommandDispatcher` no longer calls them directly.
- `ICommandDispatcher.DispatchAsync`'s `MatchSession? matchScope` parameter becomes
  `int? matchScopeDbId` via `change_api_signature` — this is what actually removes `MatchSession`
  from `Bot`'s own public surface. Two call sites in `ChatDispatchService` pass `matchScope?.DbId`
  instead of the live object (Chat already resolves the real `MatchSession` itself for the
  channel-match check, so this loses no information there — that's an existing, legitimate
  `Chat -> Multiplayer` edge, untouched).
- `(string Text, bool RequiresPreviousSuccess)` tuples (not `ChatCommandChain.Segment`, which is
  `internal` to Bot and would be a C# accessibility violation to expose on a public Multiplayer
  interface) carry the already-split chain segments across the boundary; `ChatCommandChain`
  itself (splitting on `;`/`&&`, quote handling) stays in Bot — it's chat-chaining syntax, not
  `!mp` semantics.
- Test impact assessed as bounded: `CommandDispatcherTests.cs` constructs a **real**
  `MpCommandService` (sealed, so NSubstitute can't mock it) via `MultiplayerTestSupport.Fixture`
  and calls through the public `DispatchAsync`/`ICommandReplySink` API — it does not mock
  `IMatchRegistry` separately from what the fixture already wires up. Expected edits: the
  `MakeDispatcher` factory (drop `IMatchRegistry`/`ChannelMembershipService` from
  `CommandDispatcher`'s constructor call, add `ChannelMembershipService` to the `MpCommandService`
  construction instead) and the `Run`/`RunAll` helpers (extract `.DbId` before calling
  `DispatchAsync`) — individual test bodies that pass a `MatchSession` into `Run`/`RunAll` should
  not need to change, since the helper does the conversion once.

### Commit 2 — what was done

- New `Basil.Server.Features.Bot.BotReplies` (`src/Basil.Server/Features/Bot/BotReplies.cs`) holds
  `RollResult`, `WhereUsage`, `NotRegistered`, `WhereIsIn`, `FaqUsage`, `NoFaqEntryFound`,
  `NoFaqEntriesAvailable`, `AvailableFaqEntries` — moved verbatim (same locale keys, same wording)
  out of `MpReplies`. This is a member-level move, not a single Rider refactoring tool run: found
  every reference with `grep -rln "MpReplies\.$member\b"` per member first (only
  `CommandDispatcher.cs` and `CommandDispatcherTests.cs` had any), then did the move by hand
  (`sed` for the mechanical `MpReplies.X` → `BotReplies.X` rewrite at those two call sites, since
  both files are already in the `Basil.Server.Features.Bot`/`Basil.Server.Tests.Features.Bot`
  namespace and need no new `using`).
- `MpReplies` keeps everything else, including `ChainMustBeMp`/`CannotChainMp`/
  `NotScopedToAnyMatchHint`/`UnknownMpSubcommand`/`CreatorOnlyMp`/`MpNotUsableFromLobby`/
  `MpChainNotUsableFromLobby`/`MpInDmOnly` — these back `!mp`-dispatch logic that Commit 3 moves
  into `MpCommandService`, so they stay with the code that will end up producing them.
- `Features/Bot/Locale/bot.en.json` now holds only the eight relocated keys; the rest (all of
  `Commands.Mp.*` plus the eight `!mp`-dispatch `General.*` keys) moved to a new
  `Features/Multiplayer/Locale/mp.en.json`. No key text changed. The csproj glob
  (`Features\**\Locale\*.json` → `Data/Localization/%(Filename)%(Extension)`) needed no edit.
- `LocaleTouch.AllReplyHolders()` now also touches `BotReplies.RollResult`, and
  `ReplyLocaleTests.cs` gained a `BotReplies_EveryMemberResolvesToNonEmptyText` test mirroring the
  existing `MpReplies`/`IrcReplies` ones. `LocaleCatalogTests.EveryReferencedKeyExistsAndEveryKeyIsReferenced`
  (unmodified) is the real safety net here — it fails if any key went missing or double-defined
  across the split; it passed.

### Commit 2 verification (all green)

- `dotnet build --configuration Debug`: 0 errors.
- `Basil.ArchitectureTests`: 6/6.
- `Basil.Domain.Tests`: 114/114.
- `Basil.Protocol.Tests`: 158/158.
- `Basil.Server.Tests`: **1053**/1053 (+1 from the new `BotReplies` locale test; arithmetic:
  1052 + 1 new test = 1053).
- `Basil.IntegrationTests`: 363/363 (same benign cleanup-teardown log lines as Commit 1, 0 failed).
- Route count: 140, unchanged (no route-shaped file touched).
- `measure-slice-graph.py`: features-only 43, solution-wide 50 — unchanged from Commit 1, as
  expected: this was a string relocation between two files already inside the same
  slice-crossing edge (`Bot -> Multiplayer`, via `using Basil.Server.Features.Multiplayer;` in
  `CommandDispatcher.cs`, which was already there and still is), not a new namespace crossing.
- `SliceAdjacency.Allowed`: unchanged at 45 tuples (no edge added or removed by this commit).

### Next exact step

Start Commit 3: implement the design above. Order of operations:
1. `mcp__rider__extract_interface` on `MpCommandService` is not the right tool here, because the
   two contract methods (`DispatchAsync`/`DispatchChainAsync`) don't exist on the class yet — they
   have to be created (Move Method from `CommandDispatcher`) before there's anything to extract.
   So: first move `DispatchMpAsync`, `ResolveScope`, `BuildDmRedirectSink`, `ScopedDmReplySink`,
   and `DispatchChainAsync`'s body from `CommandDispatcher.cs` into `MpCommandService.cs` by hand
   (same logic, renamed to the two contract method names), adding `ChannelMembershipService` to
   `MpCommandService`'s constructor.
2. Run `mcp__rider__extract_interface` on the now-updated `MpCommandService` for exactly those two
   methods, naming it `IMpCommandService`.
3. `mcp__rider__change_api_signature` on `ICommandDispatcher.DispatchAsync` (and its
   `CommandDispatcher` implementation) to replace `MatchSession? matchScope` with
   `int? matchScopeDbId`; fix the two `ChatDispatchService` call sites to pass `matchScope?.DbId`.
4. Change `CommandDispatcher`'s constructor to depend on `IMpCommandService` instead of the
   concrete `MpCommandService`, dropping `IMatchRegistry`/`ChannelMembershipService` if nothing
   else in the class still needs them directly (check with `mcp__rider__find_references` first).
5. Update `CommandDispatcherTests.MakeDispatcher`/`Run`/`RunAll` per the note above; rebuild, run
   all five test projects (IntegrationTests last), update this checkpoint, commit.
6. Only after Commit 3 is green: re-run `measure-slice-graph.py` one more time (expected
   unchanged again — this commit doesn't cross a new namespace boundary, it removes one type's
   worth of direct references and adds an interface, both already within the existing
   `Bot -> Multiplayer` edge) and write the final report covering both instruments, the
   `Bot -> Irc` finding, and all commit shas.
