# Basil codebase review (read-only), branch `develop` @ 69de3666

Method: source read and grep only. Nothing was built, tested or run. "verified" means I opened the code (or every caller
was grepped); "likely" means a sample or an inference. Paths are relative to `/mnt/shared/Development/osuBasil`.

Overall: the module graph is clean (see section 2, no violation of any dependency rule), there is no `ConfigureAwait`,
no `async void`, no `NotImplementedException`, no `Priv` abbreviation, no `await` under a room lock (the compiler
forbids it for `lock`, and every `Enter()` scope is in a synchronous method), every `ToListAsync` is bounded, and
every host references no Infrastructure and no other host. The real problems are listed below.

## Top 15 problems, most serious first

### 1. The IRC host writes user-visible text outside `IrcReplies` (AGENTS rule 9) - verified
32 `session.Numeric(..., "literal")` calls in `Basil.Host.Irc` carry literals that are not in `IrcReplies`
(`Basil.Host.Irc/Support/IrcReplies.cs`, 129 lines, which has the right idea for other strings). Examples:
- `Basil.Host.Irc/Commands/IrcCommands.cs:208` `session.Numeric(IrcNumericReply.ErrNoMotd, "MOTD File is missing");`
- `Basil.Host.Irc/Commands/IrcCommands.cs:362` `session.Numeric(IrcNumericReply.ErrSaslFail, "SASL authentication failed");`
  (repeated at 388 and 399), `:266`/`:356` "You have already authenticated", `:350` "SASL authentication aborted",
  `:406` "SASL authentication successful", `:192` `$"Welcome to the {IrcReplies.Network} IRC network, {mask}"`.
- `Basil.Host.Irc/Commands/IrcCommands.Queries.cs:35,49` "End of /NAMES list.", `:70` "End of /WHO list.", `:84`
  "No nickname given" (also `IrcCommands.cs:101`), `:92,110` "End of /WHOIS list.", `:100` "is logged in as".
- `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:21,27,100,180,250,258` ("No recipient given", "No text to send",
  "You are no longer marked as being away", "Cannot change mode for other users", "Unknown MODE flag").
- `Basil.Host.Irc/Delivery/MembershipDelivery.cs:50` `Kick(own, channel, "Removed from the channel")`, `:78`
  "The channel was closed", `:91` "You may no longer read the channel"; `Basil.Host.Irc/Support/IrcChannels.cs:18` "No topic is set";
  `Basil.Host.Irc/Transport/IrcListener.cs:159` `session.Close("Connection closed")`.
Why it matters: rule 9 says each chat surface owns its reply constants and changing wording is a contract change; here
the wording is scattered, and tests cannot reference constants (see 9).

### 2. The Stable host also writes user-visible text inline - verified
- `Basil.Host.Stable/Delivery/RoomDelivery.cs:156` `$"Come join my game: {notification.Room.UrlEmbed}"` (chat text the client shows).
- `Basil.Host.Stable/Web/OsuWebRoutes.cs:517` and `:528`
  `new { form_error = new { user = new { username = new[] { "Too many registrations." } } } }`
  although `Basil.Host.Stable/Web/OsuWebReplies.cs` and `Support/StableReplies.cs` exist for this.
BasilBot/Application.Bot is clean (all text is in `*Replies`); I found no literal reply outside them there.

### 3. Handlers that serve several event types, one of them with state - verified
- `Basil.Infrastructure.Bot/Handlers/RoomAnnouncer.cs:16-24` `internal sealed class RoomAnnouncer(...) :
  INotificationHandler<LobbyRoomClosingAnnounced>, INotificationHandler<LobbyRoomClosed>, ...RoomRoundStarted, ...RoomRoundAborted,
  ...RoomRoundDecided, ...RoomRoundEnded, ...RoomPlayerJoined` (7 types) and it keeps state:
  `private readonly ConcurrentSet<Round> _awaitingScores = [];` / `ConcurrentSet<Room> _closingRooms = [];` (lines 26-27).
- `Basil.Infrastructure.Chat/Handlers/ConnectionChannelHandler.cs:15-16` serves `UserConnectionOpened` and `UserConnectionClosed`.
AGENTS "Final verification" item 7 names exactly this ("a handler that waits or serves several event types") and
`docs/for-developers/architecture.md:92` says "one class per message type". Every other handler class in `src/` has one
type (checked by script over all classes; `LifecycleLog.cs` and the Host `*Delivery.cs` files are separate classes).

### 4. API view/request records use primitives where an enum exists - verified
Rule "User-visible enums: keep API record properties typed as their actual enum types".
- `Basil.Host.Shared/Views/UserViews.cs:22-23` `ulong Privileges, ulong EffectivePrivileges,` while
  `src/Domain/Users/UserPrivileges.cs:9` is `enum UserPrivileges : byte` and the request side is typed
  (`Basil.Host.Rest/Routes/UserRoutes.cs:390` `UserPrivileges? Privileges`): input and output disagree. The casts are at
  `UserViews.cs` `(ulong)record.Value.Privileges, (ulong)user.EffectivePrivileges(now)`.
- `Basil.Host.Shared/Views/ChannelViews.cs:9-10` `byte ReadPrivileges, byte WritePrivileges` and
  `Basil.Host.Rest/Routes/ChannelRoutes.cs:96-97,104-105` `byte? ReadPrivileges, byte? WritePrivileges` (should be `UserPrivileges`).
- `string Mods` in `Basil.Host.Shared/Views/ScoreViews.cs:14`, `RoomViews.cs:13,32` (`GameMods` is an enum; the view
  sends `settings.Mods.ToString()`). Possibly deliberate for a flags enum; the contract should say so. Marked likely.
- `Basil.Host.Cli/Dashboard/Data/DashboardModel.cs:21` `ulong Privileges);` (CLI only, minor).

### 5. Exceptions swallowed with no log and no counter - verified
- `Basil.Infrastructure.Beatmaps/Handlers/GetBeatmapStarsHandler.cs:19-22` `catch (Exception e) when (e is not OperationCanceledException) { return null; }`
- `Basil.Infrastructure.Beatmaps/Logic/DifficultyAnalyser.cs:32-35` `catch (Exception e) when (...) { return EmptyAnalysis(difficulty.Mode); }`
  (a failing analyser silently yields an empty analysis, which then looks like a real result)
- `Basil.Infrastructure.Beatmaps/Adapters/OsuBeatmapsetReader.cs:156-160` `catch (Exception exception) when (exception is not OperationCanceledException) { // Skip malformed .osu files ... return null; }`
  (the variable `exception` is unused; a bug in the reader looks like a malformed file)
- `Basil.Infrastructure.Beatmaps/Storage/IncomingLeftovers.cs:29-31` empty `catch (Exception exception) when (IOException or UnauthorizedAccessException) { }`
- `Basil.Host.Cli/Dashboard/Shell/DetailPanel.cs:149-152` `catch (Exception) { // The stale step stays. }`
- `Basil.Host.Cli/Dashboard/Tabs/CommandActions.cs:51` `if (task.IsCompletedSuccessfully && task.Result) then(); else toasts.Show(otherwise, ...)`:
  a faulted check shows the "cannot" toast as if the answer were no; the exception is never observed.
- `Basil.Host.Irc/Session/IrcSession.cs:210-213` and `Basil.Host.Irc/Transport/IrcListener.cs:128-131` empty IO catches (acceptable for socket teardown; listed for completeness).
The counted/logged ones (Room.Rounds.cs:18 counts `RoomMetrics.TimerFailed`, workers, handlers via `HandlerStep`) are fine.

### 6. Exception-driven validation remains although migration-status says it is done - verified
`docs/for-developers/migration-status.md` row: "Static `IsValidName`/`TryParse` checks instead of catching constructor
exceptions: Done for users, MD5 hashes, channels and content names". The code still catches the constructor:
- `src/Domain/Content/ContentReference.cs:92-101` `TryParse` itself is `try { result = Parse(s, provider); return true; } catch { return false; }` (bare catch).
- MD5: `Md5.TryParse` exists (`src/Domain/Cryptography/Md5.cs:44`) but `new Md5(` appears 27 times and only 1 caller uses
  `TryParse`/`IsValid`; hosts catch instead: `Basil.Host.Stable/Login/StableLogin.cs:67` and `:90`,
  `Basil.Host.Stable/Packets/PacketHandlers.cs:117` (`catch (ArgumentException) { }` around `hash = new Md5(action.MapMd5);`),
  `Basil.Host.Stable/Web/OsuWebRoutes.cs:563`, `Basil.Application.Users/Persistence/CredentialChecks.cs:26`,
  `Basil.Infrastructure.Content/Persistence/EfMenuBannerRepository.cs:84`, `Basil.Protocol.Game.Stable/Models/Client/OsuFingerprintExtensions.cs:45`.
- `src/Domain/Users/Country.cs:813` and `Basil.Infrastructure.Beatmaps/Storage/FileBeatmapsetStorage.cs:331,350` same pattern.
17 sites catch `ArgumentException`/`FormatException` in total.

### 7. `UserRegistry` is a 799-line mixed responsibility class - likely
`Basil.Application.Users/Registry/UserRegistry.cs`: besides indexing online users it does login (`LoginAsync:135`,
`LoginWithTokenAsync:219`), registration checks (`CheckRegistrationAsync:329`), bot bootstrap (`EnsureBotAsync:420`,
`ConnectBotAsync:464`, `OperateAsync:445`), activity flushing (`FlushActivityAsync:594`), shutdown closing
(`CloseStoppedAsync:618`) and retention (`PurgeEndedSessionsAsync:632`). AGENTS: Registry "owns the operations on what it holds";
session retention and credential deletion in `EnsureBotAsync` are not that. This is the "God Actor / service-centric"
smell from item 7. Not a defect today, a maintainability blocker for "clean inside".

### 8. Tests coupled to implementation or to real time - verified
- Reflection into private members: `tests/Hosts/Cli/Dashboard/ListPageTests.cs:37,78,84` (`GetField("_query", NonPublic)`, `GetMethod("Back", NonPublic)`),
  `tests/Hosts/Cli/Dashboard/GroupFoldTests.cs:29,35` (`GetMethod("Fold", NonPublic)`, `GetField("_rows", NonPublic)`),
  `tests/Hosts/Cli/Dashboard/SmallPartsTests.cs:102,115` (`GetMethod("Relative"|"Matches", NonPublic)`).
- Real sleeps: `tests/Hosts/Cli/Dashboard/ListPageTests.cs:44,49` `await Task.Delay(50, ...)` (twice, in a test that already has a fake clock);
  `tests/Basil/EndToEnd/Streams.cs:21` `Task.Delay(50)`; `tests/Hosts/Rest/Sse/StreamHelpers.cs:13` `Task.Delay(100)`;
  `tests/Basil/EndToEnd/ShutdownEndToEndTests.cs:176` `Task.Delay(20)`; `tests/Support/Basil.Testing.Clients/OsuClient.cs:131,148` 25 ms polling;
  `tests/Support/Basil.Testing.Persistence/StoredRows.cs:114` 50 ms. (No `Thread.Sleep` anywhere.)
- Sync-over-async / blocking: `tests/Basil/Configuration/ConfigurationWatcherTests.cs:41` `_watcher.StartAsync(...).GetAwaiter().GetResult();`, `tests/Basil/Logging/LogQueueTests.cs:53` `_release.Wait();`.
- Log assertions (rule 8 says do not): `tests/Infrastructure/Bot/Replies/ReplyCatalogTests.cs:187-188` `Assert.Contains(Joined.Key, entry.Message)`, `:203` `Assert.Equal(LogLevel.Warning, ...)`.

### 9. Tests duplicate reply literals instead of the production constants - verified
Rule 8/9: "Tests reference those constants rather than duplicating literals." IRC tests assert literals that exist as
`IrcReplies` constants: "Password incorrect" (`tests/Hosts/Irc/Users/RegistrationTests.cs`, constant `IrcReplies.BadCredentials`),
"Too many failed login" (same file, `IrcReplies.TooManyAttempts`), "No text to send" (`tests/Hosts/Irc/Chat/ChatTests.cs`),
"End of /NAMES" (`ChannelTests.cs`, `QueryTests.cs`), "SASL authentication" (`tests/Hosts/Irc/Users/CapabilityTests.cs`,
`tests/Basil/EndToEnd/IrcEndToEndTests.cs`, `tests/Support/Basil.Testing.Clients/IrcClient.cs`). CLI tests assert literals
too (`tests/Hosts/Cli/Commands/ContentCommandTests.cs:272,274` "Sent to 0 clients.", "No such user: 999999"; `UserCommandTests.cs:44`).
This is a consequence of problem 1: the strings have no constant to reference.

### 10. Gateway enforcement and rate-limit rules still live in the hosts - verified (documented as target)
`Basil.Gateway` is referenced by the hosts, but the policy keys and numbers are chosen in the hosts:
`Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:67` `limiter.TryAcquire($"chat:{actor.Record.Id}", ...)`,
`Basil.Host.Stable/Packets/PacketHandlers.Chat.cs:30` (same key built in a second host), `Basil.Host.Irc/Commands/IrcCommands.cs:148`
`$"irc-login-fail:{session.Address}"`, `Basil.Host.Rest/Http/RestPipeline.cs:60,68`, `Basil.Host.Stable/Web/OsuWebRoutes.cs:515,525,526`
(`register-check:`, `register:`, `register:server`), `Basil.Host.Rest/Sse/DiagnosticsRoutes.cs:74` `"diagnostics:gc-collect"`.
`RateLimiter` is registered three times: `Basil.Host.Rest/RestHost.cs:20` `services.AddSingleton<RateLimiter>();`,
`Basil.Host.Stable/Routes/StableRoutes.cs:26` `services.AddSingleton<RateLimiter>();`, `Basil.Host.Irc/Transport/IrcHost.cs:18` `TryAddSingleton`.
It works (last registration wins, one instance) but each host believes it owns the limiter. The same `chat:` key being
shared between IRC and Stable is a hidden coupling. `migration-status.md` lists this as target; for a v1.0.0 "clean inside"
goal it is the largest remaining migration item.

### 11. "(target)" status text is out of date for options binding - verified
`docs/for-developers/migration-status.md` says "`Basil` binds configuration and hands it over as `IOptions<T>`; target: the
project that defines an options type binds it". In code three Infrastructure projects already bind themselves
(`Basil.Infrastructure.Beatmaps/DependencyInjection.cs:59`, `Basil.Infrastructure.Bot/DependencyInjection.cs:41`,
`Basil.Infrastructure.Shared/DependencyInjection.cs:67`, `DatabaseSetup.cs:14`), while the hosts still read raw string keys:
`Basil.Host.Irc/Transport/IrcHost.cs:40-41` `configuration.GetValue("Basil:Server:Irc:Port", irc.Port)` / `"Basil:Server:Domain"`,
`Basil.Host.Rest/RestHost.cs:36` takes `IConfiguration`, and `Basil/Lifetime/HostApplication.cs:45,76` still binds
`Basil:Updates`/`Basil:Server`. So both the doc and the code are in a mixed state.

### 12. Whole images are copied into memory on the image hosts - verified location, severity likely
`Basil.Host.Rest/Images/StreamImageProvider.cs:44-46`
`var memory = new MemoryStream(); await stream.CopyToAsync(memory, ...); return new BufferedResolver(memory.ToArray());`
AGENTS Streaming: "Application does not buffer a payload in a MemoryStream or a byte[] copy" and "Large payloads (replays, `.osz`
archives, images) never pass through a cache"; `Basil/Lifetime/HostApplication.cs:60-63` also puts an ImageSharp disk
cache under `<Storage>/Cache`. The copy is bounded by upload limits for avatars/screenshots, so this is a rule violation,
not an OOM risk (likely). Related but bounded and acceptable: `Basil.Host.Stable/Routes/StableRoutes.cs:79-81` buffers the
packet body (Kestrel limit 1 MiB, `KestrelSetup.cs:12`), `Basil.Infrastructure.Beatmaps/Adapters/OsuBeatmapsetReader.cs:132`
buffers one `.osu` capped by `MaxOsuBytes`.

### 13. Dead production members (only tests call them) - verified by grep over `src` and `tests`
- `src/Domain/Mechanics/GameTeamType.cs:33` `public bool IsTagMode()` - no reference anywhere.
- `Basil.Host.Cli/Dashboard/Data/DashboardModel.cs:190` `public void AddOutput(OutputLine line)` - no reference anywhere.
- `Basil.Host.Cli/Dashboard/Graphs/BrailleGraph.cs:56` `public static bool FitsGraphs(int width)` - no reference anywhere.
- `Basil.Protocol.Game.Stable/Wire/Packets/PacketWriter.cs:94` `ChangeUsername` and `:269` `ToggleBlockNonFriendDm`, `Basil.Protocol.Irc/Models/IrcMessage.cs:274`
  `Privmsg` - referenced only by their own protocol tests, never by a host (the server never sends them).
- `src/Domain/Beatmaps/BeatmapsetStatus.cs:66` `FromOsuApi` - only `tests/Domain/Beatmaps/BeatmapsetStatusTests.cs:47-49`.
- Empty test project: `tests/Application/Content/Basil.Application.Content.Tests.csproj` has 0 test files (Application.Content holds only interfaces + `ReadMenu`), still in `Basil.slnx`.
- `src/Application/Common`, `src/Infrastructure/Common`, `tests/Infrastructure/Common` exist as `bin/obj`-only leftovers of the old layout (untracked; harmless, local clutter).

### 14. Packages and suppressions - verified
- AutoMapper 13.0.1: no `*.cs`, `*.csproj` or `Directory.Packages.props` entry uses it. It arrives transitively through
  `ppy.osu.Game.Rulesets.*` (`Directory.Build.props:13-17`, comment says so; restored in `~/.nuget/packages/automapper/13.0.1`).
  It can NOT be "simply removed": the only Basil-side artefact is the `NuGetAuditSuppress GHSA-rvv3-g6hj-g44x` line; removal needs an
  upstream bump of ppy.osu (or an exclusion of the transitive asset, which would break the rulesets if they load it at runtime).
- `Directory.Build.props` carries 10 `NuGetAuditSuppress` entries: 1 AutoMapper, 6 ImageSharp (kept on 3.1.12 for licence reasons, mitigations written there), 3 MessagePack/SignalR transitive.
- `Directory.Packages.props` lists `SharpZipLib` 1.4.2 and `Microsoft.Extensions.Configuration.Binder` 10.0.11 but no csproj references them (verified by script over `src`, `tests`, `tools`): dead version pins.
- `Directory.Packages.props` mixes `Microsoft.Extensions.Configuration*` at 10.0.11 with others at 10.0.14 (cosmetic).
- Docs contradict: `AGENTS.md` describes `Basil.Infrastructure.Persistence` as "Npgsql provider ... all PostgreSQL migrations" while
  `Basil.Infrastructure.Persistence/Providers/MySqlDatabaseProvider.cs` exists and `docs/for-developers/database.md:5` says PostgreSQL or MySQL;
  XML docs `Basil.Infrastructure.Shared/Persistence/DatabaseOptions.cs:3` and `DatabaseSetup.cs:5` say only PostgreSQL. Only PostgreSQL migrations exist (`Migrations/<Module>`), so MySQL support depends on the migrations being engine-neutral (`database.md:131` says so).

### 15. Smaller contract and hygiene items - verified
- `Basil.Application.Scores/Messages/ListScores.cs:20-23` `public enum ScoreListOutcome { Ok = 1, NoBeatmap, NoUser ... }` has no `Denied = 0` (rule: every outcome enum has `Denied = 0`, `Ok = 1`). It is a message-result enum for a public read, so arguably exempt, but it is the only one of 11 `*Outcome` enums that deviates (`ShutdownStepOutcome` in `Basil/Lifetime/ShutdownSequence.cs` is not an object outcome).
- `Basil.Host.Rest/Http/Api.cs:102` `new Envelope<T>(..., DateTimeOffset.UtcNow)`: the only direct clock read in `src`; everything else uses `TimeProvider`, so the envelope timestamp cannot be faked in tests.
- Two `Meter` instances with the same name: `Basil.Application.Multiplayer/Objects/Rooms/RoomMetrics.cs:12` `new Meter("Basil.Multiplayer", "1.0")` and `Basil.Infrastructure.Multiplayer/Metrics/MultiplayerMeter.cs:9`; same for `"Basil.Users"` (`Application/Users/Objects/Connections/ConnectionMetrics.cs:22` vs `Infrastructure/Users/Metrics/UsersMeter.cs:9`). Application owns metrics code here although the layering puts `Metrics/` in Infrastructure.
- `Basil.Host.Cli/Socket/ControlSocket.cs:173` `_ = Task.Run(() => ServeAsync(client, stoppingToken), stoppingToken);` - `ServeAsync` (line 190) only catches IO/Socket/Json/OperationCanceled; any other exception faults an unobserved task with no log (likely).
- XML docs that describe implementation (rule 6): `Basil.Application.Users/Registry/UserRegistry.cs:669` "The lock order is this gate, then <see cref="User" />", `Basil.Infrastructure.Content/Storage/FileFaqStorage.cs:53` "Writes an entry's new text to a temporary file", `Basil.Infrastructure.Shared/Storage/FileUploadSpool.cs:6`, `TempFiles.cs:6`.
- Correlated subquery for `BestPerPlayer` in `Basil.Infrastructure.Scores/Persistence/EfScoreRepository.cs:~97` (`!matching.Any(other => other.UserId == s.UserId && ...)`) is O(n^2) per beatmap leaderboard and every list uses `Skip(offset)`; fine at small scale, a load risk for the 1k-user goal that `migration-status.md` says is untested (likely).

## 1. Leftovers of the migration

Every "(target)" claim checked:
| Claim | Code | Verdict |
|---|---|---|
| Gateway enforcement by the hosts | policies declared on every Stable/Rest/IRC entry (`AdmissionPolicy` in `IrcCommands.cs:22`, `PacketHandlers.cs:23`, `RestHost.cs:85`, `StableRoutes.cs:69`, `OsuWebRoutes.cs:50-111`) but rate rules still inline | partly done, see 10 (verified) |
| Options binding in the owning project | Beatmaps, Bot, Shared bind; hosts and `Basil` do not | partly done, doc stale, see 11 (verified) |
| Rest/Cli/Infrastructure never reference `Protocol.Stable` | csproj of Rest, Cli, Shared have no reference; no `using Basil.Protocol.Stable` in Rest/Cli; no Infrastructure csproj references Protocol | done (verified) |
| Test naming `Condition_Result` | not sampled | unknown |
- `TODO`: 1, `src/Domain/Client/ScoreFrame.cs:17` `int TagByte, //TODO: Can someone reverse-engineer the osu! stable spectating protocol and decode the TagByte bitmask in SPECTATE_FRAMES?`. No `FIXME`/`HACK`/`XXX`, no `NotImplementedException`, no `[Obsolete]`, no `SuppressMessage`.
- `#pragma warning disable`: 1 hand-written, `Basil.Infrastructure.Beatmaps/Adapters/OsuBeatmapCalculator.cs:180` `#pragma warning disable CS0618 // the score is built here, with no score manager to ask` (justified). Generated/migration only: `CA1814` in 4 migrations (`Beatmaps|Chat|Multiplayer|Users/..._InitialCreate.cs`), `612, 618` in the 10 generated designer/snapshot files (20 lines).
- Commented-out code: none found by grep for `//\s*(var|if|return|await)`; not exhaustively read (likely none).
- Unreferenced members: see 13.

## 2. Rule violations from AGENTS items 7 and 8

- `.ConfigureAwait`: 0 in `src`/`tools`; `tests/Architecture/LayeringTests.cs:318` enforces it.
- Host calling `ScopeTo`: 0 hits in `src/Hosts` (verified). Host referencing Infrastructure: 0 (csproj and `using`). Host referencing another host: 0 (script over `using Basil.Host.*`). Rest/Cli referencing `Protocol.Stable`: 0.
- Module Infrastructure referencing another module: 0 (csproj project references and `using Basil.Infrastructure.*`; only `Shared`, which is allowed). Application project references match the documented graph.
- Application holding handlers/logic/workers: none (no `Handlers`/`Logic`/`Workers`/`Adapters` folder and no `INotificationHandler`/`BackgroundService` under `src/Application`). Note `Basil.Application.Bot/Commands/` holds `CommandCatalog`, `CommandContext`, replies (contracts); the command handlers are in `Infrastructure/Bot/Handlers/Commands`. Fine.
- Type names as literals: `grep '"Basil\.'` finds only the `Meter` names (`"Basil.Multiplayer"` etc., 13 of them, and `AddMeter("Basil.*")` in `Basil/Logging/TelemetrySetup.cs:29`). These are telemetry names, not type names; acceptable.
- `Priv` abbreviation: none.
- `await` under a room lock: none. `lock (_sync)` blocks in `Room.*.cs` are synchronous (the compiler forbids await); `Room.Enter()`, `User.Enter()`, `Channel.Enter()`, `Connection.EnterSpectators()` callers (`Lobby.cs:272`, `UserGrants/UserProfile/UserSessions/UserSilence`, `Connection.cs:182,212,229`) never await inside the scope (checked the files). `UserRegistry.AccountScope` is a `ref struct`, so it cannot cross an await.
- Handler that waits: no handler class contains `Task.Delay`/`WaitAsync` (files with delays are workers, lifetime and tests). Handler serving several event types: see 3.
- User-visible strings outside `*Replies`: see 1 and 2.
- Hosts reading `IConfiguration` directly: see 11.
- A second synchronization mechanism for rooms: none (only `Room._sync` `Lock`).
- pp used for ranking/win condition: no `OrderBy*(...Pp)`, no `Pp` comparison in Multiplayer (verified clean).

## 3. Contracts

- Outcome enums: 11 `*Outcome` enums, 10 have `Denied = 0`, `Ok = 1`; `ScoreListOutcome` does not (15).
- API typed as primitive instead of enum: see 4. `BeatmapsetStatus` is typed correctly (`PatchSetRequest(BeatmapsetStatus? Status, ...)`, `BeatmapRoutes.cs:215`).
- Envelope bypasses: `Results.Json` only in `Api.cs:103` (the envelope itself); `Results.Stream` for downloads (`BeatmapRoutes.cs:102,200`, `ScoreRoutes.cs:71`) and `Results.Text/Content` for guide pages (`GuideRoutes.cs:27,39`) are binary/HTML documents, intentional. No other bypass found (6 `Results.*` hits in total, all justified).
- API docs rule (rule 5): grep of `WithSummary/WithDescription` for PostgreSQL/Redis/middleware/Mediator found none (verified clean).

## 4. Error handling and safety

- `catch` blocks: 107 sites. Swallowing ones: see 5. `async void`: 0. `.Result`/`.Wait()` in `src`: only property `.Result` of outcome tuples and `task.Result` after `IsCompletedSuccessfully` (`CommandActions.cs:51`), no blocking.
- Unobserved `Task.Run`/discards in `src`: `ControlSocket.cs:173` (15), `SseHub.cs:293` `_ = TickAsync();` (its body begins with `try`, likely safe), `FileBeatmapsetWatcher.cs:118` `_ = SettleAsync(name);` (likely handles its errors; `:143` catch logs), `BeatmapReloader.cs:34` (RunAsync has catch at `:58`), `ScorePerformanceRecalculator.cs:29` (documented "failure is logged"), eight `_ = Task.Run(async () => ...)` in `Basil.Host.Cli/Dashboard` (each has a try/catch in the sampled ones; `DetailPanel.cs:142-152` swallows, see 5).
- Unbounded collections: `RateLimiter` is capped (`MaxKeys = 100_000`, purge every minute). `IMemoryCache` is used only for `CacheAside` and credentials. No unbounded cache found in hosts; `IrcListener._perAddress` and `Outbox._queues` are per-connection structures (not checked for removal on close: likely fine).
- Database reads: every `ToListAsync` in `src/Infrastructure` has `Take(...)` (`ListLimits.Ceiling`, `page.Fetch`, or `limit`) except `EfSettingsRepository.cs:46` (settings categories, a handful of rows) and the two `Include`-free reads that are single-row. Verified clean otherwise.
- Streaming/memory copies: see 12. Uploads go through `FileUploadSpool` (`Basil.Infrastructure.Shared/Storage/FileUploadSpool.cs`), which has a catch that deletes the temp file; quarantine is in `Quarantine.cs`.
- Clock: see 15.

## 5. Tests

Per project (test attributes: `[Fact]`/`[Theory]` lines; theories count once even with many `InlineData`; source = `.cs` files in the mirrored `src` project):

| Test project | Tests | Test files | Source files |
|---|---:|---:|---:|
| Application/Beatmaps | 80 | 3 | 36 |
| Application/Bot | 25 | 5 | 14 |
| Application/Chat | 77 | 5 | 22 |
| Application/Content | 0 | 0 | 9 |
| Application/Diagnostics | 10 | 2 | 15 |
| Application/Multiplayer | 203 | 15 | 89 |
| Application/Scores | 37 | 2 | 26 |
| Application/Servers | 74 | 8 | 39 |
| Application/Shared | 15 | 3 | 25 |
| Application/Users | 246 | 26 | 76 |
| Hosts/Shared | 7 | 2 | 17 |
| Hosts/Cli | 229 | 28 | 94 |
| Hosts/Irc | 129 | 14 | 19 |
| Hosts/Rest | 166 | 27 | 32 |
| Hosts/Stable | 107 | 16 | 23 |
| Infrastructure/Anticheat | 3 | 1 | 2 |
| Infrastructure/Authentication | 52 | 5 | 8 |
| Infrastructure/Beatmaps | 213 | 25 | 58 |
| Infrastructure/Bot | 302 | 22 | 26 |
| Infrastructure/Chat | 60 | 8 | 26 |
| Infrastructure/Content | 86 | 8 | 17 |
| Infrastructure/Diagnostics | 21 | 5 | 13 |
| Infrastructure/Multiplayer | 105 | 11 | 69 |
| Infrastructure/Scores | 104 | 12 | 26 |
| Infrastructure/Servers | 28 | 2 | 22 |
| Infrastructure/Shared | 81 | 13 | 31 |
| Infrastructure/Users | 135 | 17 | 53 |
| Protocol/Irc | 73 | 4 | 5 |
| Protocol/Stable | 190 | 24 | 26 |
| Domain | 255 | 37 | 63 |
| Gateway | 33 | 4 | 10 |
| Basil | 100 | 29 | 24 |
| Architecture | 87 | 17 | - |
Total about 3,333 `[Fact]/[Theory]`.

Thin relative to source (likely gaps): `Hosts/Shared` (7 tests / 17 files; `OutcomeErrors.cs` 123 lines, `RoomCommands.cs` 203 lines, `AdmissionRefusals.cs`), `Application/Diagnostics` (10 / 15), `Application/Shared` (15 / 25; `ServerSettings.cs` 492 lines), `Application/Scores` (37 / 26 in 2 files), `Infrastructure/Servers` (28 / 22 in 2 files), `Infrastructure/Multiplayer` (105 / 69), `Infrastructure/Anticheat` (3), `Application/Content` (empty). There is no test project for `Basil.Infrastructure.Persistence` itself (migrations are covered by `tests/Basil/ModuleMigrationTests.cs`), none for `Application.Anticheat`/`Application.Authentication` (interfaces only).

Quality findings: see 8 and 9. Also `MemberData`/`Skip` use: no `Skip =` on a test. Known gaps listed by the owner in `migration-status.md` (kick/unban/team/removeref unknown-name tests; no load test) are still open.

## 6. Packages

See 14. Summary: `Directory.Packages.props` central versions, `net10.0`, EF Core 10.0.14, Npgsql 10.0.3, MySql.EntityFrameworkCore 10.0.12, Mediator 3.0.2, ImageSharp 3.1.12 (pinned for licence, 6 suppressed advisories), xunit.v3 4.0.2. AutoMapper 13.0.1 is transitive only (no Basil use); two dead `PackageVersion` entries (SharpZipLib, Microsoft.Extensions.Configuration.Binder).

## Counts

| Item | Count |
|---|---:|
| `TODO` | 1 (`ScoreFrame.cs:17`) |
| `FIXME`/`HACK`/`XXX`/`NotImplementedException`/`[Obsolete]`/`SuppressMessage` | 0 |
| Hand-written `#pragma warning disable` | 1 |
| Migration/generated pragma lines | 4 (CA1814) + 20 (612,618) |
| `NuGetAuditSuppress` | 10 |
| `ConfigureAwait` in `src` | 0 (in `tests`: only the guard test) |
| `async void` | 0 |
| `catch` sites in `src` | 107 (17 catch Argument/FormatException; 6 swallow silently, see 5) |
| Literal IRC numeric reply lines outside `IrcReplies` | 32 |
| Multi-event handler classes | 2 |
| Unobserved `_ = Task.Run/Async` in `src` | 15 |
| Real-time delays in tests | 12 (no `Thread.Sleep`) |
| Reflection into private members in tests | 8 call sites in 3 files |
| Test projects with 0 tests | 1 (`Application/Content`) |
| Outcome enums off the `Denied = 0`/`Ok = 1` rule | 1 of 11 |
| Dead members confirmed | 8 |
| Largest source files | `Country.cs` 819 (data), `UserRegistry.cs` 799, `PacketWriter.cs` 735, `FileBeatmapsetStorage.cs` 670, `SettingsTab.cs` 659, `OsuWebRoutes.cs` 567 |

Not checked (out of the time I spent): commented-out code exhaustively, `Condition_Result` test naming, every XML doc against the
Implementation Test, `Basil.Protocol.*` internals, the BasilBot `!mp` privilege paths, the SSE hub concurrency, and any docs/code drift beyond the items above.
