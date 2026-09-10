# Stage C worker checkpoint

> Read this file first. It is kept current in the same commit as every green step, so a
> successor can resume from here without reconstructing state from `git log` and a build.

## Current task: C3 — done. Next is C2

Order is C4 → C3 → C2 → C6 → C1 → C5 (see `plans/execution/stage-c-order-decision.md` and
`plans/basil-plan-20260909.md`'s Stage C preamble, which adds C6). This file tracks C4 and C3;
a later worker doing C2/C6/C1/C5 should create sibling sections or a new file per the
orchestration doc's convention.

**C4 is done — all three commits landed.** Historical record below, kept for the reasoning
behind the `SliceAdjacency` state C3 inherits.

**C3 is done — landed in two commits, both described below.** Everything in this section is
committed; nothing is pending or uncommitted. The next task in the order is **C2** (split
`GameSession`) — read `plans/execution/stage-c-order-decision.md`'s "What changes in C2"
section before starting; this file does not track C2's plan.

### Plan for C4 (three commits)

1. **Commit 1 (done)** — pure move: `MpCommandService`/`MpReplies` relocate to
   `Basil.Server.Features.Multiplayer`, no behavior or signature change.
2. **Commit 2 (done)** — split `MpReplies`: the eight members that back `!roll`/`!where`/`!faq`
   moved to a new `Bot.BotReplies`, wording and locale keys unchanged. `Features/Bot/Locale/
   bot.en.json` now holds only those eight keys; the rest moved with the code to a new
   `Features/Multiplayer/Locale/mp.en.json`.
3. **Commit 3 (done)** — collapsed `Bot -> Multiplayer` to one contract, `IMpCommandService`. See
   "Commit 3 — what was done" below.

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

### Commit 3 — what was done

Picked up mid-flight: the tree was received with steps 1-5 of the six-step order already applied
in the working copy (uncommitted) — `IMpCommandService` existed, `MpCommandService.cs` had gained
`DispatchAsync`/`DispatchChainAsync`/`ResolveScope`/`BuildDmRedirectSink`/`ScopedDmReplySink`
verbatim from `CommandDispatcher`, `CommandDispatcher`'s constructor already depended on
`IMpCommandService`, `ICommandDispatcher.DispatchAsync` already took `int? matchScopeDbId`, and
`CommandDispatcherTests` was already updated to match. Build was green (0 errors) before any work
this session. The actual signature landed slightly differently from the design's sketch —
`DispatchAsync(UserSession sender, string[] args, ...)` instead of a separate
`(string subcommand, string[] subArgs)` pair — a harmless simplification (the split into
subcommand/subArgs happens on the first line of the method body instead), not flagged as a
problem.

Two things needed fixing before this was actually correct:

1. **A real behavior bug in the in-progress edit.** `ChatDispatchService.SendChannelMessageAsync`
   computed `matchScope` (the channel-derived `MatchSession`, when the message was sent in that
   match's own channel) but then called `commandDispatcher.DispatchAsync(sender, truncated, null,
   ...)` — passing a hardcoded `null` instead of `matchScope?.DbId`. This silently dropped the
   channel-derived match scope for every `!mp` command sent in a match's own chat channel (the
   single most common case), which the design explicitly calls out as one of the two call sites
   that must pass `matchScope?.DbId`. Fixed by passing `matchScope?.DbId` as designed. No test
   caught this because `CommandDispatcherTests` calls `ICommandDispatcher.DispatchAsync` directly
   with a `MatchSession`/`.DbId` already in hand, bypassing `ChatDispatchService` entirely — this
   codepath has no test coverage at the `ChatDispatchService` level either before or after the fix,
   so nothing regressed, but nothing would have caught the bug either. **Worth a follow-up**: an
   integration or `ChatDispatchService`-level test exercising a channel-scoped `!mp` command would
   have caught this and doesn't exist today.
2. **`SliceAdjacency`: `("Bot", "Irc")` turned out to be genuinely removable**, contradicting
   Commit 1's finding that it "must stay." That finding was correct *at the time* — the row's real
   carrier was `CommandDispatcher`'s own `ScopedDmReplySink`, which called
   `sender.IrcConnection.Send(...)`. Commit 3's Move Method relocated `ScopedDmReplySink` bodily
   into `MpCommandService` (Multiplayer), which already carries `Multiplayer -> Irc`. Verified
   `grep -rln "Irc" src/Basil.Server/Features/Bot/` returns nothing at all post-move. Proved it by
   deleting the row and running `Basil.ArchitectureTests`: still 6/6 (was 5/6 when the same
   deletion was tried in Commit 1). Deleted the row for real this time and updated the now-stale
   `("Bot", "Multiplayer")` comment (it referenced `IMatchRegistry`/`MatchSession`, both gone from
   `CommandDispatcher` since this commit) to name `IMpCommandService` as the sole carrier.
   `("Bot", "Scores")` was checked too: no such row exists in `SliceAdjacency` at `74980d28` or at
   any point in C4 — confirmed by `git show 74980d28:tests/Basil.ArchitectureTests/SliceAdjacency.cs`.
   There is nothing to delete; the script's "gone" report for it was never backed by an allowlist
   entry in the first place.

### Commit 3 verification (all green)

- `dotnet build --configuration Debug`: 0 errors (both before and after the `ChatDispatchService`
  fix, and after the `SliceAdjacency` edit).
- `Basil.ArchitectureTests`: 6/6, including the deliberate `("Bot", "Irc")`-row-deleted probe
  described above (also 6/6).
- `Basil.Domain.Tests`: 114/114.
- `Basil.Protocol.Tests`: 158/158.
- `Basil.Server.Tests`: 1053/1053 (unchanged from Commit 2 — no test added or removed this
  commit).
- `Basil.IntegrationTests`: 363/363 (one benign `[Test Class Cleanup Failure]` from
  `AnnounceEndpointTests` teardown this run, same pattern as the prior two commits' benign
  cleanup-failure log lines from different test classes — 0 failed reported).
- Total: 6 + 114 + 158 + 1053 + 363 = **1694** (oracle 1693 at `74980d28` + 1 new test from
  Commit 2 = 1694, unchanged by Commit 3 as expected).
- Route count: 140, unchanged. No route-shaped file touched.
- `measure-slice-graph.py`: features-only 43, solution-wide 50 — unchanged from Commit 2, as
  predicted (this commit relocates code already inside the existing `Bot -> Multiplayer`
  namespace crossing; it doesn't cross a new one). Confirms `("Bot", "Irc")` still reads as
  "gone" in the text scan (it already did, before this commit — the scanner never saw
  `ScopedDmReplySink`'s dependency either way).
- `SliceAdjacency.Allowed`: 45 → **44** tuples. `("Bot", "Irc")` deleted and proved by the
  ArchitectureTests run above — this is the first commit where deleting that row is actually
  correct, not a workaround.

### Commit 3 follow-up — regression test for the `ChatDispatchService` bug, and one more stale comment

The `ChatDispatchService` bug fixed in Commit 3 (dropped `matchScope?.DbId`, see above) had no
test covering it either before or after the fix — `CommandDispatcherTests` calls
`ICommandDispatcher.DispatchAsync` directly with the id already in hand, never going through
`ChatDispatchService`. Added
`SendPublicMessageHandlerTests.Handle_SenderInMatchsOwnChannel_PassesTheMatchsDbIdAsScope`
(`tests/Basil.Server.Tests/Features/Chat/Packets/SendPublicMessageHandlerTests.cs`), which sends a
`!mp settings` message from a `GameSession` whose `Match.ChatChannelName` matches the channel and
asserts `ICommandDispatcher.DispatchAsync` receives the match's `DbId`, not `null`. Verified the
test actually catches the regression: reintroduced the hardcoded `null` and confirmed this test
fails (`NSubstitute.Exceptions.ReceivedCallsException`, expected `7` got `null`), then restored the
fix and confirmed it passes again.

Also trimmed `("Bot", "Chat")`'s comment, which still said "BotBootstrapService **and
CommandDispatcher**" — `CommandDispatcher` dropped its last `Chat` reference in Commit 3.
`grep -rln "Basil.Server.Features.Chat\|ChannelMembershipService\|IChannelRegistry\|ChannelSession"
src/Basil.Server/Features/Bot/` now returns only `BotBootstrapService.cs`, so the row itself stays
(it's still live), just the comment's attribution was stale.

Verification: `dotnet build` 0 errors; `Basil.ArchitectureTests` 6/6 (SliceAdjacency comment-only
change); `Basil.Server.Tests` **1054**/1054 (1053 + 1 new test). Domain/Protocol/Integration not
rerun — this follow-up touched a test-only file and a comment in an already-green
`SliceAdjacency.cs`; no production code changed relative to the `ea6bd277` commit already verified
against all five projects.

## C3 — stop `PlayerLogoutService` importing five feature slices

Design settled before this task started: `plans/execution/logout-as-event-decision.md`. Short
version — **an ordered handler list, not an event bus.** `Shared/Sessions` gained one new
abstraction:

```csharp
public interface IPlayerLogoutHandler
{
    int Order { get; }
    Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken);
}
```

`PlayerLogoutService` no longer touches any `Basil.Server.Features.*` type. It holds
`IEnumerable<IPlayerLogoutHandler>`, sorts once by `Order`, and runs each in turn inside a
`try/catch (Exception ex) when (ex is not OperationCanceledException)` — catch, log, continue,
per the decision doc. Cancellation is not caught here; it propagates, mirroring
`GhostDisconnectService.RunOnce`'s own cancellation filter.

### The seven handlers, in `Order`

| Order | Handler | Slice | What it does |
|---|---|---|---|
| 10 | `MatchLeaveLogoutHandler` | Multiplayer | `GameSession` leaves its match under the match lock (`BeginMutationAsync`/`MatchMembership.LeaveAsync`/`PublishState`) |
| 20 | `SpectatorTeardownLogoutHandler` | Spectating | Removes the departing `GameSession` as someone's spectator, then tears down BasilBot's own watch of it |
| 30 | `ChannelPartLogoutHandler` | Chat | Parts every joined channel via `ChannelMembershipService.DisconnectFromChannels`, for both session kinds |
| 40 | `GameSessionRegistryRemovalLogoutHandler` | Shared/Sessions | Removes a departing `GameSession` from `ISessionRegistry<GameSession>` |
| 40 | `IrcSessionRemovalLogoutHandler` | Irc | Removes a departing `IrcSession` from `ISessionRegistry<IrcSession>` |
| 50 | `StatusPublishLogoutHandler` | Spectating | Publishes the offline status to `IPlayerStatusEvents` |
| 60 | `LogoutBroadcastHandler` | Shared/Sessions | Broadcasts the bancho Logout packet to every other online, unrestricted `GameSession` |

The two Order-40 handlers never both apply to the same call (a session is either a `GameSession`
or an `IrcSession`, never both), so the tie is inert.

Why registry removal and the final broadcast are handlers owned by **Shared**, not inlined back
into `PlayerLogoutService`: both touch only `GameSession`/`ISessionRegistry<T>`, which are
already Shared types, so giving them their own `Order` slot is what lets the flat sorted list
reproduce the original statement order exactly — channel-part, then registry removal, then
status-publish, then broadcast — with no special-cased code splicing the loop. The alternative
(keep them inline, run before/after the handler loop) would have been provably safe here (see
the commit below) but only after reading `ChannelMembershipService.DisconnectFromChannels`'s
reference-equality check; encoding them as ordered handlers instead means no reader has to
re-derive that proof.

`SpectatorTeardownLogoutHandler` resolves the bot's session with
`Basil.Domain.Users.SystemUserIds.BasilBot` instead of `Basil.Server.Features.Bot
.BotBootstrapService.BotId` (the two are the same value — `BotBootstrapService.BotId` is just
`SystemUserIds.BasilBot` re-exposed). This is the one substitution that keeps Spectating from
needing a new `Spectating -> Bot` `SliceAdjacency` row; reaching for `BotBootstrapService`
instead would have tripped the task's stop condition.

### Login: measured, nothing to invert

`LoginService` and `IrcAuthenticationService` are *Features* types (`Basil.Server.Features.Auth`
/ `Basil.Server.Features.Irc`), not `Shared` types. Their cross-slice references (`Auth -> Bot`,
`Auth -> Chat`, `Auth -> Content`, `Auth -> Spectating`, `Auth -> Users`, `Irc -> Auth`, etc.) are
already declared `SliceAdjacency` rows — the normal, intended mechanism for a Features type,
unlike `PlayerLogoutService`, which lived in `Shared` and imported `Features` directly, breaking
the layering rule `Shared_Should_Not_Reference_Features` exists to catch. Grepped
`src/Basil.Server/Shared` for a login-owning type analogous to `PlayerLogoutService`/
`GhostDisconnectService`: none exists. Login has nothing to invert for C3; recorded here rather
than acted on, per the task's explicit instruction not to build one on the strength of the task
title alone.

### Commit 1 — the handler abstraction, seven handlers, DI wiring, and the pinned-list deletion

- Added `IPlayerLogoutHandler` (`src/Basil.Server/Shared/Sessions/IPlayerLogoutHandler.cs`) and
  rewrote `PlayerLogoutService` down to a sorted-handler runner (93 lines -> 46 lines -- carries
  no Features import at all, first time in the file's history).
- Added the seven handler files listed above, one per file, each in the slice that owns the
  dependency it wraps.
- Wired `services.AddSingleton<IPlayerLogoutHandler, X>()` once per handler, in the slice's own
  `Add<Slice>` extension (`AddSharedInfrastructure` for the two Shared ones, `AddMultiplayer`,
  `AddSpectating` x2, `AddChat`, `AddIrc`).
- `SliceBoundaryTests.Shared_Should_Not_Reference_Features`: removed the
  `"Basil.Server.Shared.Sessions.PlayerLogoutService"` entry from `knownOffenders` (12 -> 11) and
  corrected the leading comment's count and attribution (it no longer holds a live `MatchSession`
  or IRC connection; `GhostDisconnectService` is the remaining offender in that family, and only
  because it still types `ISessionRegistry<IrcSession>` for its own constructor, an unrelated,
  out-of-scope coupling).
- Fixed every other production/test call site the constructor-signature change broke:
  `LoginServiceTests`, `GhostDisconnectServiceTests` (two sites), `LogoutHandlerTests`,
  `TcpIrcConnectionTests` — each rebuilt as the same seven-handler set, argument-for-argument
  mapped from the old seven-parameter constructor, so no test's actual wiring changed, only the
  packaging.
- Rewrote `PlayerLogoutServiceTests` itself around a `MakeService(matchMembership, extraHandlers)`
  factory that builds the full real handler set (not mocks of `PlayerLogoutService`'s own
  internals) — the same shape DI registers — plus two new tests pinning the failure contract:
  `Logout_WhenAHandlerThrows_LaterHandlersStillRunAndLogoutCompletes` and
  `Logout_WhenAHandlerThrowsOperationCanceled_PropagatesAndSkipsLaterHandlers`.
- Added `CompositionRootTests.ResolvesPlayerLogoutServiceWithAllHandlers`, asserting
  `_provider.GetServices<IPlayerLogoutHandler>().Count() == 7` — the same shape as the existing
  `ResolvesBanchoPacketDispatcherWithAllHandlers` test for `IPacketHandler`. This is the guard
  against a silently-missing `AddSingleton<IPlayerLogoutHandler, X>()` line: every hand-wired unit
  test would still pass even if a slice's DI registration were forgotten, exactly the shape of
  bug C4's checkpoint flagged in `ChatDispatchService`.

### Commit 2 — the `GhostDisconnectServiceTests` behavioural update

Building the seven-handler set surfaced one **intentional** behavioural change, not a bug: the
old `PlayerLogoutService` had no internal exception handling, so a single failing step (e.g. a
channel lookup throwing) aborted the *entire* `LogoutAsync` call for that session, and only
`GhostDisconnectService.RunOnce`'s own per-session `try/catch` stopped that from aborting the
whole sweep. The new `PlayerLogoutService` catches per-handler, so a failing step now only skips
itself — every other step for that same session, including registry removal, still runs.

`GhostDisconnectServiceTests.RunOnce_OneSessionReapThrows_StillReapsTheRest` encoded the *old*
whole-session-abort behaviour (`Assert.NotNull` on the poisoned session's registry entry, because
the whole logout used to abort before reaching registry removal). Updated the assertion to
`Assert.Null` for both sessions -- the poisoned session's channel-part step fails and is logged,
but its own registry removal (Order 40) still runs -- and updated the doc comment to describe the
new, more resilient contract. This is exactly what the task's verification line means by "a
logout still completes when one step fails": before this task, it didn't.

### Verification (all green)

- `dotnet build --configuration Debug`: 0 errors.
- `Basil.ArchitectureTests`: 6/6.
- `Basil.Domain.Tests`: 114/114.
- `Basil.Protocol.Tests`: 158/158.
- `Basil.Server.Tests`: **1057**/1057 (1054 baseline + 1 `CompositionRootTests` handler-count test
  + 2 `PlayerLogoutServiceTests` failure-contract tests).
- `Basil.IntegrationTests`: 363/363 (two benign `[Test Class Cleanup Failure]` log lines from
  `ScoreListEndpointTests`/`DirectSearchEndpointTests` teardown, same pre-existing pattern as
  every prior Stage C commit's run against different test classes -- 0 failed reported).
- Route count: unchanged at 140. No route-shaped file touched.
- `measure-slice-graph.py`: **43 / 50, unchanged from C4's end state in both counts.** This is not
  a null result: `PlayerLogoutService` and its new handler files live under `Shared/`, and the
  script's slice list is derived only from `Basil.Server/Features/<Slice>` directories -- `Shared`
  was never a tracked source of edges, in either features-only or solution-wide mode, so removing
  its Features imports was invisible to this script in both directions. The real signal for this
  task is the `Shared_Should_Not_Reference_Features` pinned-list deletion, not this script.
- `SliceAdjacency.Allowed`: **untouched, still 44 tuples.** `PlayerLogoutService` was a `Shared`
  type; none of its five Features imports were ever Features-to-Features edges this allowlist
  tracks, so there was no row to delete. Checked every new handler file for an accidental new
  cross-slice edge (e.g. `SpectatorTeardownLogoutHandler` importing `Bot` would have needed a new
  `Spectating -> Bot` row) -- none exists; each handler only reaches its own slice's
  already-owned services plus `Shared`/`Basil.Domain` types. No row added, no row removed, exactly
  as predicted before running the script.

### Next exact step

C3 is finished and fully committed.

## C2 -- investigated, not started: the task's own currency cannot move

**Nothing in this section is applied to the tree.** The working tree is byte-identical to
`8f318c8f` (confirmed by `git diff` returning empty). One test file was edited twice and reverted
to prove a point empirically; the revert is confirmed clean by `git diff --stat` on that file
returning nothing. No commit was made for C2 -- this write-up is the only output, landing as a
docs-only commit.

### What was measured (re-confirms the task's own table, does not re-derive it)

Write ownership, grepped fresh on this tree, matches the task's table exactly:

| Field | Sites | Files |
|---|---|---|
| `.Match =` | 4 | `MatchLifecycle.cs` (1), `MatchMembership.cs` (3) |
| `.Spectating =` | 2 | `SpectatorService.cs` |
| `.InLobby =` | 2 | `LobbyJoinHandler.cs`, `LobbyPartHandler.cs` |
| `.MpScopeMatchId =` | 4 | `MatchMembership.cs` (1), `MpCommandService.cs` (3) |

Read-site blast radius for `.Match` alone (property reads, not the unrelated `Regex.Match`/method
calls): **45 sites across 6 files' worth of slices** -- `Auth/ClientIntegrityService.cs`,
`Chat/ChatDispatchService.cs`, `Irc/BanchoIrcBridgeConnection.cs`,
`Multiplayer/Endpoints/MatchSlotEndpoints.cs`, `Multiplayer/MatchControlService.cs`,
`Multiplayer/MatchLifecycle.cs`, `Multiplayer/MatchMembership.cs`, `Multiplayer/MpCommandService.cs`,
seventeen files under `Multiplayer/Packets/`, `Scores/ScoreSubmissionService.cs`, and
`Shared/Http/Bancho/PacketDispatcher.cs`. This is why the task called C2 the riskiest task in the
stage; the number is real.

### The blocking finding: the pinned list cannot lose a `Shared.Sessions.*` entry from this split

The task states success as: *"`Shared/Sessions/GameSession.cs` stops naming
`Features.Multiplayer` and `Features.Spectating` types. The proof is the
`Shared_Should_Not_Reference_Features` pinned list losing its `Shared.Sessions.*` entries."*

Two things are wrong with that framing, found by reading the actual file and by running the test,
not by assumption:

1. **`GameSession.cs` never names `Features.Spectating`.** Its `Spectating` property is typed
   `GameSession?`, `Spectators` is `IReadOnlyCollection<GameSession>`, and the backing field is
   `ConcurrentDictionary<int, GameSession>` -- all self-typed `Shared.Sessions` types. The file's
   only `Features` usings are `Basil.Server.Features.Irc` (for `IIrcConnection` /
   `BanchoIrcBridgeConnection`) and `Basil.Server.Features.Multiplayer` (for the `Match` field's
   `MatchSession` type). There is no `Features.Spectating` reference to remove.
2. **Removing `.Match` does not un-pin `GameSession`, and removing `.MpScopeMatchId` does not
   un-pin `UserSession`.** `SliceBoundaryTests.Shared_Should_Not_Reference_Features` is a per-type
   check: any single `Features` dependency keeps a type in `knownOffenders`, checked by exact set
   equality. `GameSession` keeps `override IIrcConnection IrcConnection` and
   `new BanchoIrcBridgeConnection(this)` in its constructor -- both `Features.Irc` -- and the
   task's own destination table defers `IrcConnection` to Stage D, explicitly out of C2's scope.
   `UserSession`'s *only* `Features` reference is the same abstract `IIrcConnection IrcConnection`
   property; `MpScopeMatchId` is `int?` and was never a `Features` dependency at all.

**Empirical probe**, run rather than argued: temporarily deleted only
`"Basil.Server.Shared.Sessions.UserSession"` from `SliceBoundaryTests.knownOffenders`
(`tests/Basil.ArchitectureTests/SliceBoundaryTests.cs`), ran `dotnet build` (0 errors) then
`dotnet test tests/Basil.ArchitectureTests --no-build` in the foreground. Result: **failed**,
naming `UserSession` as an unexpected actual offender -- proof that the `Irc` coupling alone is
sufficient to keep a `Shared.Sessions` type pinned, with zero contribution from `MpScopeMatchId`.
Reverted the one-line deletion; `git diff` on the test file now returns empty (byte-identical to
`8f318c8f`); reran `dotnet test tests/Basil.ArchitectureTests --no-build`: 6/6 passing again.

Since `GameSession` carries a *strictly stronger* `Irc` coupling than `UserSession` (both the
property override and the constructor's concrete instantiation, vs. `UserSession`'s property alone),
the same conclusion applies to it a fortiori: removing `Match` cannot remove `GameSession` from the
list either, because `Irc` alone already pins it.

**Net effect if the four-field split were carried out as specified: `SliceAdjacency` 44 -> 44,
`measure-slice-graph.py` 43/50 -> 43/50 (unchanged; every read/write site above already lives
inside a slice that already has a declared edge to the field's owning slice, so no new crossing is
created, but none is removed either), and the pinned list 11 -> 11.** The one instrument the task
names as its actual currency does not move. This was checked, not assumed: see the probe above.

### A second, independent problem: the `.Match` half of this split may be redundant before C1 runs

`MatchSession` (`src/Basil.Server/Features/Multiplayer/MatchSession.cs`, 584 lines) is today a
single `sealed class` that already carries both halves Task C1 plans to separate: business state
(slots, host, settings, referees, bans, timer, progress) and the SSE projection machinery
(`StateStream<T>` x9, `SseSubscriberRegistry`, `SequenceGate`), confirmed by reading the file.
C1's own plan entry says the business half moves to `Basil.Domain.Multiplayer` and the projection
half stays behind in `Basil.Server`.

If `GameSession.Match` ends up, post-C1, pointing at the *business* half (the natural read of "a
session's current match" once the split happens), the field's type becomes a `Basil.Domain` type,
and the `Shared -> Features.Multiplayer` edge from `.Match` disappears as a side effect of C1 --
for free, with none of the 45-site read-site churn this task would otherwise spend on it. Whether
that is actually how C1 will split `GameSession.Match`'s reference is C1's design decision, not
verified here (it depends on whether callers of `.Match` need slot/settings/host state -- Domain
side -- or SSE snapshot state -- Server side -- and today's 45 call sites are a mix; a worker
doing C1 needs to check this before assuming it resolves cleanly). Flagged here because, if true,
doing the `.Match` quarter of C2 now is work C1 either redoes or invalidates.

### Cost the split would add for zero measured benefit

Today all four fields live on the connection object (`GameSession`/`UserSession`) and are
discarded for free when the object is discarded at logout -- no field-specific cleanup exists or
is needed. Moving a field to a player-id-keyed map owned by a slice makes that map's entries
outlive the session object; an entry not explicitly removed at logout leaks and can resurface
incorrectly if the same player id logs back in. Checked what already clears each field today:

- `.Match`: fully covered by the existing `MatchLeaveLogoutHandler` (Order 10), which already
  calls `MatchMembership.LeaveAsync` under the match lock -- that method is one of the four writers
  above, so a map-backed rewrite stays covered by the handler that exists.
- `.Spectating`: fully covered by the existing `SpectatorTeardownLogoutHandler` (Order 20), which
  reads `game.Spectating` and calls `SpectatorService.RemoveSpectator` -- also already covered.
- `.InLobby`: **not covered by anything today.** `ChannelPartLogoutHandler` (Order 30) parts
  channels; it does not touch `InLobby`. A map-backed rewrite would need a *new* cleanup step that
  has no reason to exist today (the field just dies with the object).
- `.MpScopeMatchId`: **not covered by anything today**, same reason -- nothing in the logout path
  touches it now, and nothing needs to. A map-backed rewrite needs a new handler or an extension of
  an existing one.

So the four-field split, run as specified, adds two new lifetime obligations to buy zero movement
on the instrument the task names as its proof.

### Recommendation -- not acted on, orchestrator decision needed

1. **Fold the `.Match` question into C1's `MatchSession` split design**, rather than deciding it
   here under C2's name (which the stage-C order doc explicitly forbids -- "doing half of C1 early
   is how the two tasks blur together"). Check whether `GameSession.Match`'s post-split reference
   naturally lands on the Domain half before spending the 45-site churn under C2.
2. **`.Spectating`, `.InLobby`, `.MpScopeMatchId` have no such shortcut** -- they were never really
   pinned-list contributors (Spectating never was; InLobby and MpScopeMatchId are scalar/self-typed
   and never triggered the rule either). Splitting them into per-slice maps is a legitimate
   "own your state" cleanup per the plan's write-ownership principle, but it is architecture for
   its own sake against this task's stated proof, not a step that moves any of the four measured
   instruments, and it is the source of the two new cleanup obligations above.
3. **The real, and only, way to move the pinned list for `GameSession`/`UserSession` is the `Irc`
   coupling** -- `IIrcConnection`/`BanchoIrcBridgeConnection` -- which the task's own destination
   table places in Stage D, not C2. Moving it now would be taking Stage D's work under C2's name,
   the same ambiguity the stage-C order doc warns against for C1.

None of the four stop conditions listed in the task's instructions name this situation literally
(no new `SliceAdjacency` row is needed, no field has more than one writer, no new lock is needed,
the route count does not move), but the underlying instruction -- *"Stop conditions: report rather
than work around"* -- applies to the deeper problem: proceeding would produce a compiling four-
commit split whose own stated proof does not appear, while adding cleanup obligations that do not
exist today. Reported rather than run.

### Next exact step

Orchestrator decision needed before any C2 commit lands:
- Confirm whether `.Match` should move under C2 at all, or wait for C1's `MatchSession` split to
  settle where `GameSession.Match` points.
- Confirm whether `.Spectating` / `.InLobby` / `.MpScopeMatchId` should still be split into
  per-slice maps despite moving no instrument, given the new logout-cleanup obligations they would
  introduce.
- If the answer to both is "proceed anyway," re-open this section and execute the four-field split
  as originally specified, including new `IPlayerLogoutHandler` entries for `.InLobby` (a Chat
  handler) and `.MpScopeMatchId` (a Multiplayer handler, since neither is covered by an existing
  handler today).

Until that decision lands, treat C2 as **investigated and blocked**, not started. C6 and C1 remain
next in the order per `plans/execution/stage-c-order-decision.md`; C1 in particular should read the
"`.Match` half may be redundant" finding above before starting.
