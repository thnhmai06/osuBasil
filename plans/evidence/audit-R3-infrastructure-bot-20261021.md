COUNTS: bug=23 rule=43 style=79 doc=14 questions=12

Slice: src/Infrastructure/** and src/Bot/**. `ConfigureAwait` not reported (handled separately). Rider lint output was too large to use; findings come from reading every file. Line numbers verified against current content.

# Bot

## src/Bot/Application/Commands/Mp/MpCommands.cs
- [rule] MpCommands.cs:98-141,175-201,300+ (every handler) — AGENTS "Identify by the basis object, not its id"; "Parts are reached through the owning object's scope" — `ResolveRoomId` returns an `int` and each of ~27 handlers takes `roomId` and calls `ViewRoom(by, roomId)` again (lookup twice per command). Ids are for repository lookups only, and room ids are reused (`#mp_5`). — Resolve a `Room` once in `ScopedAsync` (`var room = ResolveRoom(context)`), pass `Room room` to the handlers, delete the `ViewRoom` call at the top of each handler.
- [rule] MpCommands.cs:1269-1279 — AGENTS "Behaviour belongs to the object that owns it", "permission plumbing"; "No authority decided outside the object" — `ViewRoom` re-implements the private-room visibility rule (slot, observer, `IsManagedBy`, `Permissions.RoomViewPrivate` via `by.User.Check(..., time.GetUtcNow())`) inside the bot. The API/IRC need the same rule. — Move it to Application.Objects (e.g. `lobby.ScopeTo(by).Find(id)` returning `Room?` or `NotFound`), delete `time` and the `Permissions`/`Access` usings from this class.
- [rule] MpCommands.cs:1084 — AGENTS "Each object enforces its own rules" — the countdown range (1..3600 s) is decided by the bot ("checking what the server no longer checks"); the API or another entry point has no limit and `MpReplies.CountdownOutOfRange` hard-codes "1 hour". — Put the limit in `ScopedRoomRounds.StartCountdownAsync` (return `RoomResult.InvalidSettings`/new result), drop the check here.
- [rule] MpCommands.cs:978 — same — `mods.RemoveInvalidMods(mode)` is a room rule applied by one entry point only. — Do it inside `Settings.ConfigureAsync` (the room already returns `RoomResult.InvalidMods`).
- [rule] MpCommands.cs:920-921 — "Knowledge about a Domain value lives on that value" — `new BeatmapReference(beatmap.Hash, beatmap.Id, beatmap.Name, beatmap.Metadata.Difficulty.Mode, beatmap)` assembled in the command. — `beatmap.ToReference()` on the Domain/Application type, or `RoomSettingsChange.Beatmap` takes the `BeatmapRecord`.
- [rule] MpCommands.cs:32,35,38,856,472,440 — same — `MaxRoomNameLength=50`, `MaxSlotIndex=16`, `MaxRoomSize=16` and the `0..3` ranges (829,838,897) are room/Domain knowledge and duplicated (`MaxSlotIndex` == `MaxRoomSize`); the usage strings in MpReplies repeat "1-16"/"0-3". — Take them from Domain (`MatchSettings`/slot count constant, `Enum.IsDefined<GameTeamType>` etc.), reference them in the usage strings.
- [rule] MpCommands.cs:160,44,1281 — "A channel's name tells its kind" / naming owned by the channel — `$"#mp_{roomId}"` and regex `^#mp_(\d+)$` duplicate `RoomChannel.Name` (`Application/Objects/Multiplayer/Rooms/RoomChannel.cs:17`) and `Server.cs:96`. — Compare `context.Channel` with `room.Channel.Name`; resolve the room by asking the lobby/server to parse a room channel name (one owner).
- [rule] MpCommands.cs:223,322,337,353-355,398,342,819 — AGENTS rule 9 "User-visible reply strings" — literals in the handler: `" (private)"`, `"Freemod"`, `"Host"`, `"Not Ready"`, `"No Map"`, `"private"/"not private"`, the slot line format, `$"#{u.Id} {u.Value.Name}"` (749,772), `", {sz} slots."` (877), team capitalisation (819). — Move each to a constant/format in `MpReplies`; parse the team with `Enum.TryParse<GameTeam>(ignoreCase)` and use `team.ToString()`.
- [rule] MpCommands.cs:1011-1027 — bug-adjacent — `!mp start abc`, `!mp start 0`, `!mp start -5` fall through and start the match immediately. — If `args.Count > 0` and the value is not a positive int, reply `MpReplies.StartUsage` (new) and return false.
- [bug] MpCommands.cs:1262-1267 — `ResolveUserAsync` treats any name starting with `#` as an id (`#1234` fails for a user whose name starts with `#`), then `GetAsync(id)` returns deleted users too (add/kick/ban of a deleted account). — Try the name when the id lookup finds nothing/deleted; filter `DeletedAt` (the repository `GetByNameAsync` already excludes deleted).
- [style] MpCommands.cs:205-1249 — duplicated block `if (outcome != RoomResult.Ok) { await context.Reply(MpReplies.Describe(outcome), ct); return false; }` ×25 and `room is null` → `NotFound` ×25, `rawTarget`/`ResolveUserAsync`/`UserNotFound` ×10. — Add `private async Task<bool> ReplyAsync(CommandContext c, RoomResult outcome, string successReply, ct)` and `ResolveTargetAsync(...)`; each handler shrinks to its own parsing.
- [style] MpCommands.cs:1-1282 — Organization — one 1282-line `partial` class doing dispatch, argument parsing for 27 subcommands, room resolution and formatting. — Split by area: `MpCommands` (dispatch + resolution), `MpRoomCommands` (make/join/in/settings/lock/private/size/name/password/close), `MpPlayerCommands` (move/host/invite/kick/ban/team/refs), `MpRoundCommands` (map/mods/set/start/timer/abort). Drop `partial` (only for the generated regex).
- [style] MpCommands.cs:214-215 — bool trap: `OpenRoomAsync(name, "", true, isPrivate, ...)` positional literals. — name the arguments.
- [style] MpCommands.cs:66,74,110 — `args.Skip(1).ToArray()` / `Array.Empty<string>()` / `ToLowerInvariant()` per call. — `args.Count > 1 ? [.. args.Skip(1)] : []`; `switch` on `subcommand` with `StringComparer.OrdinalIgnoreCase` via a `FrozenDictionary<string, handler>` (also removes the duplicated list at 47-49).
- [style] MpCommands.cs:108 — reassigning the parameter `context`. — local `var named = NameRoom(...)`.
- [style] MpCommands.cs:337 — `room.Authority.Host is not null && player == room.Authority.Host` — `player == room.Authority.Host` suffices.
- [style] MpCommands.cs:749,772,877,1036 — abbreviations `u`, `sz`; nested `if` without braces in `TimerAsync` (1035-1040), reusing `seconds` as default and out variable. — `referee`, `size`; parse into a new local.
- [style] MpCommands.cs:173,203,229,… — `// ── x ───` banner comments. — `#region`/remove (also MpReplies, BotReplies, AnnouncementReplies, ChatCommands:37,56).
- [style] MpCommands.cs:1084 — `seconds < 1 || seconds > 3600` — `seconds is < 1 or > 3600` (moot if moved, see above).
- [style] MpCommands.cs:957-986 — `before`/`after` are read around an await; a concurrent change mixes into the message. — return the change from `ConfigureAwait` or compute from `outcome`.
- [doc] MpCommands.cs:62-82 — `<returns>` says "true when recognized and server reported success" but help returns true and several usage errors return true/false inconsistently (e.g. 1137 `UserNotRegistered` vs 477 `UserNotFound` for the same condition). — unify replies (`UserNotFound` vs `UserNotRegistered`, `NotFound` vs `NoActiveRoomWithId`/`NoActiveRoomWithHashId`).

## src/Bot/Application/Commands/Mp/MpReplies.cs
- [rule] MpReplies.cs:32,51,58,62,113,249,303 (+AnnouncementReplies.cs:43) — Leftovers: unused constants `PrivateRoomJoinDenied`, `FailedToJoinMatch`, `WasScopedToGoneMatch`, `CurrentlyScopedToMatch`, `UserNotInMatchOrUnregistered`, `MatchStartsInSeconds`, `CreatorOnlyMp`, and `SettingsChangedCountdownCancelled` (verified by grep: no reference). — delete them.
- [style] MpReplies.cs:316-343 — help text built with `+ "\n" +` ×27. — raw string literal.
- [style] MpReplies.cs:312,477 — `string.Format(GenericFailed, outcome)` prints the enum name to the player. — fixed text without the enum, or map every `RoomResult`.
- [doc] MpReplies.cs:103,110,205 — "<1-16>", "0-3" hard-coded; see MpCommands bounds finding.

## src/Bot/Application/Commands/Mp/MpScopes.cs
- [bug] MpScopes.cs:15-38 — AGENTS "a runtime channel is one opening (`#mp_5` is reused when a room id is reused)", "Cleanup that carries an old object can never touch its replacement" — the per-user target is a room **id**: after room 5 closes and a new room 5 opens, `!mp in 5` targeting silently points to the new room; entries of users who go offline are never removed. — Store `Room` (weak) keyed by `User`/connection; validate `room.IsClosed` in `Get`; clear on `UserConnectionClosed`.
- [style] MpScopes.cs:13 — name `MpScopes` collides with the `ScopeTo` vocabulary (it is a remembered target). — `MpTargets`.
- [style] MpScopes.cs:20-38 — three methods only forward to the dictionary (the doc remarks describe the dispatcher's resolution order, which belongs on `MpCommands.ResolveRoom`). — keep, but move the remark.

## src/Bot/Application/Commands/ChatCommands.cs
- [bug] ChatCommands.cs:113-117 — `!roll 2147483647` → `random.Next(0, max + 1)` overflows to `int.MinValue` → `ArgumentOutOfRangeException`. — `random.NextInt64(0, (long)max + 1)` and format as long, or clamp `max`.
- [rule] ChatCommands.cs:38 — `"#lobby"` literal — channel names are owned by the channel. — compare with the lobby channel's name constant from Application.Objects.
- [rule] ChatCommands.cs:129-142 — `!where` answers any user's country with no check (also deleted users, `GetByNameAsync`). — Question (see Questions).
- [style] ChatCommands.cs:48-50 — `tokens.Count != 0 && Equals(tokens[0],"mp") && tokens.Count >= 2 && IsChainable(tokens[1])`. — `tokens is [var first, var second, ..] && first.Equals("mp", OrdinalIgnoreCase) && MpCommands.IsChainable(second)`.
- [style] ChatCommands.cs:44-70 — segments are tokenized twice (validation and execution) and `tokens.Count == 0` (65) can no longer happen after validation (dead). — tokenize once into a list of `(segment, tokens)`.
- [style] ChatCommands.cs:178-182 — `string text; using (reader) { text = await ...}`. — `using (reader) { await context.Reply(await reader.ReadToEndAsync(ct), ct); }` or `using var _ = reader;`.
- [doc] ChatCommands.cs:18 — `<returns>True if a command was executed` is wrong: the value is also the chain "succeeded" flag (usage errors return false although the command was recognized). — "true when the command succeeded; false for unknown commands, usage errors and failures"; use `<see langword>`.
- [doc] ChatCommands.cs:15-17 — `<param>`s "A cancellation token." state nothing.

## src/Bot/Application/Commands/CommandLine.cs
- [bug] CommandLine.cs:59-65,136-169 — `Split` and `Tokenize` disagree on escapes outside quotes: `Tokenize` treats `\"` as a literal quote, `Split` appends the `\` and then the `"` opens a quote, so `!mp name a\"b; !mp close` is not split at `;`. — handle `\\`/`\"` outside quotes identically in both (share one scanner).
- [style] CommandLine.cs:66 — `case ';' when !inQuote:` is always true here (inside the `!inQuote` branch). — remove the `when`.
- [style] CommandLine.cs:31,36,88 — abbreviations `op`, `c`, `i`, `nextOp`. — `chainOperator`, `character`, `index`.
- [style] CommandLine.cs:5-18 — `ChainOperator`, `CommandSegment`, `CommandLine` in one file; the first two are used by `ChatCommands` too. — own files.
- [style] CommandLine.cs:143-153 — braces around `case ' '`/`case '\\'` bodies unnecessary; `Split` fallback (86) `[new CommandSegment(message.Trim(), ...)]` duplicates the trailing flush. — simplify.

## src/Bot/Application/Commands/CommandContext.cs / BotReplies.cs
- [bug] BotReplies.cs:13 — user-visible text "show a userSession's country" (stale vocabulary leaked into a reply). — "show a user's country".
- [rule] CommandContext.cs:13 — AGENTS naming/typed values — `string? Channel` holds a channel *name* compared with literals; type loses what it is. — `ChannelName` (or the `Channel` object) and update `<param>`.
- [style] BotReplies.cs:11-15 — help text with `+` concatenation; banners. — raw string.

## src/Bot/Application/Chat/CommandWorker.cs
- [bug] CommandWorker.cs:29,40,80 — `_pending` gets one entry per user id that ever typed a command and entries are set to 0 but never removed (unbounded growth). — decrement and `TryRemove(KeyValuePair(userId, 0))` when it reaches 0, e.g. `_pending.AddOrUpdate` then `TryRemove`.
- [style] CommandWorker.cs:54 — `using var timeout` is a `CancellationTokenSource`. — `timeoutSource`.

## src/Bot/Application/Chat/ChatHandler.cs
- [style] ChatHandler.cs:29 — `pm` abbreviation. — `privateChannel`.
- [style] ChatHandler.cs:35-39 — the two `Func` lambdas capture the handler's `cancellationToken` parameter (unused) and duplicate `SendSplitAsync` wiring. — build `Reply`/`ReplyPrivately` through two small local functions.
- [style] ChatHandler.cs:67 — `text.Split('\n')` allocates a string per line. — `foreach (var range in text.AsSpan().Split('\n'))` / `EnumerateLines()`.

## src/Bot/Application/Announcements/RoomAnnouncer.cs, AnticheatAnnouncer.cs
- [rule] RoomAnnouncer.cs:16,58,74 — AGENTS "Relations are references in `ConcurrentSet<T>`" and "Identify by object" — `ConcurrentDictionary<int, Room>` keyed by id with `TryRemove(new KeyValuePair<int, Room>(...))` to emulate reference identity. — `ConcurrentSet<Room>` (Domain/Common) with `Remove(room)`.
- [bug] RoomAnnouncer.cs:84-86 — `_closingRooms[...] = e.Room` is set only after two awaits while `RoomPlayerJoined` arrives on another bus (`RoomEvent`): a player joining in between never triggers "room will no longer close". — add the room before the first await.
- [rule] RoomAnnouncer.cs:44-51 — forbidden nullable-result switch expression followed by `if`; and `RoomResult.Ok when !StartsRound` is just `RoomResult.Ok` after the first arm. — convert to a `switch` statement that calls `SayAsync` in each case.
- [style] RoomAnnouncer.cs:30,37 — `(int)TotalSeconds` truncates in one place and `Math.Round` in the other. — one convention.
- [style] RoomAnnouncer.cs:31-33,38-40 — `StartsRound ? string.Format(A, n) : string.Format(B, n)` ×2. — pick the format then format once.
- [rule] AnticheatAnnouncer.cs:30-44 / RoomAnnouncer.cs:89-103 — duplicated `WhisperStaffAsync` + local function. — one internal helper (e.g. `Room.Authority.Staff` + `Server.WhisperAsync`).
- [style] AnticheatAnnouncer.cs:46-51 — flag names listed by the bot with `Enum.GetValues…HasFlag` — knowledge about a Domain value. — `BanchoFlagsGroups`/extension on `BanchoFlags` (`flags.Names()`), the reply text owns only the format.
- [style] AnnouncementReplies.cs:9,13,… — banner comments; em dashes in wire text (43,51,61) — OK to keep but note IRC/ASCII surfaces. 

## src/Bot/Application/DependencyInjection.cs
- [style] DependencyInjection.cs:22 — `services.AddSingleton(Random.Shared)` registers the shared instance as a singleton `Random` (anything may resolve `Random`). — `ChatCommands` should take `Random` through a typed wrapper or keep `Random.Shared` internally (no registration).

# Infrastructure.Runtime

## Runtime/Sessions/IdleConnectionSweeper.cs
- [doc] IdleConnectionSweeper.cs:13 — `protected override ExecuteAsync` has no `/// <inheritdoc />`.
- [style] IdleConnectionSweeper.cs:15 — the 100 s period is a literal and the XML summary repeats it. — const `SweepPeriod` (AGENTS: Infrastructure owns the period); summary "Closes idle connections periodically."

## Runtime/Events/EventBus.cs, HandlerStep.cs
- [doc] EventBus.cs:20 — fine; EventBus.cs:28 `ExecuteAsync` doc ok. HandlerStep.cs:15,36 — `internal` members of an `internal` class (redundant modifier). — drop `internal`.
- [style] HandlerStep.cs:36-48 — `Run` takes a `cancellationToken` only to filter an `OperationCanceledException` that a synchronous `Action` cannot normally throw. — drop the parameter from `Run`.

## Runtime/Sessions/ConnectionHandler.cs, RestrictionHandler.cs
- [style] ConnectionHandler.cs:26-44 — `case UserConnectionClosed closed:` declares `connection` in a switch section scope, 4 steps each repeating `Subject, connection.Record.Id`. — extract `OnClosedAsync(UserConnectionClosed, ct)`.
- [rule] RestrictionHandler.cs:29-30 — AGENTS "Permission checks use the effective permissions" — `restricted.User.Value.Permissions.Allows(RoomManageAny)` reads granted permissions (equal today because a manager's area is never suspended, but the rule says effective). — compute `Effective(restrictions, now)` or add a `UserRecord`-level helper.

## Runtime/Beatmaps/BeatmapsetStorageWatcher.cs
- [bug] BeatmapsetStorageWatcher.cs:31-46 — if the start-up scan is cancelled (or the host stops before `ExecuteAsync`), `_changes` is never enumerated, so the `FileSystemWatcher` created eagerly by `storage.WatchAsync` (see FileBeatmapsetStorage) and its debounce sources are never disposed. — also `_stopping.Cancel()` is not enough; dispose via enumerating `_changes` in `StopAsync` or have `WatchAsync` start lazily.
- [bug] BeatmapsetStorageWatcher.cs:68-82 — an exception thrown by the enumerator itself (e.g. `FileNotFoundException` in `FileBeatmapsetStorage.EntryOf`, line 633-635) is outside the inner `try` and ends `ExecuteAsync`: the watcher dies for the rest of the run. — wrap the whole `await foreach` iteration (restart the watch) or fix `EntryOf`.

## Runtime/Multiplayer/MatchRecorder.cs
- [rule] MatchRecorder.cs:99 — "the caller of an operation is `by`" — parameter `actor`. — `by`.
- [rule] MatchRecorder.cs:29,59,63 — user-visible/report details `"Empty"`, `"Kicked"`, `"Banned"` as scattered literals, and a ban is stored as `MatchEventType.Kicked` + detail. — constants in the owner of the match report text, or a `MatchEventType.Banned`.
- [style] MatchRecorder.cs:57-68 — irregular argument wrapping (formatter churn). 

# Infrastructure.Storage

## Storage/Postgres
- [style] DatabaseReader.cs:1 — stray UTF-8 BOM (only file with one). — remove.
- [style] DatabaseReader.cs:8 (and IdentityMap.cs:25, WeakIndex.cs:21, OsuBeatmapsetReader.cs:14,17,20) — `// ponytail:` comments. — plain `shortcut: <limit>, <when to upgrade>` or delete; the three reader caps deserve XML docs.
- [doc] DatabaseReader.cs:11-13 — `<param>`s missing. DatabaseWriter.cs:81,95,111 — `<param>`/`<returns>` for `root`, `identity`, `command`, `operation` missing. WriteCommand.cs:14 — `ToCommand` has no docs (caller must dispose the command).
- [doc] Database.cs:10-11 — the two sources are undocumented and named "Asynchronous"/"Synchronous" (they differ by `synchronous_commit`, not by API). — rename `RelaxedCommitSource`/`ConfirmedCommitSource` and document; also public fields. 
- [rule] Database.cs:13-16 — a static constructor sets the process-wide Dapper `MatchNamesWithUnderscores`; any Dapper use before the first `Database` is built gets different mapping. — set it in `AddInfrastructureStorage`.
- [style] Root.cs:4-31 — `readonly record struct` with public fields and a private constructor. — `readonly record struct Root(RootKind Kind, int Id)`; `RootKind` is a second type in the file.
- [style] DatabaseWriter.cs:243,328 — `delay = delay + delay > TimeSpan.FromSeconds(5) ? ... : delay + delay` duplicated. — `delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks))`.
- [style] DatabaseWriter.cs:33-60 — five empty lifecycle methods. — implement `IHostedService` + `IHostedLifecycleService` only where needed (inherit default interface members if available) or a base class.
- [style] DatabaseWriter.cs:338-339,350 — public fields in private nested classes; class `PendingTransaction` is also used for single statements. — properties; `PendingWrite`.
- [style] DatabaseWriter.cs:279 — `'30s'` literal. — const.
- [bug] DatabaseWriter.cs:330-333 — `catch (PostgresException) { return false; }` swallows without logging and makes `pg_xact_status` errors look like "not committed" → the caller rethrows and the write is retried, which can store a try-operation twice. — log and treat as unknown (retry the status), not false.
- [rule] DatabaseWriter.cs:148-153 — queue ordering — a replacement of an already queued identity keeps the *old* position, so `Save(A) Save(B) Save(A')` stores A' before B although it was made after B. — document or move the replaced item to the end if cross-identity order matters.
- [style] DatabaseMigrator.cs:29 — `MigrateAsync` reads migration scripts that are already applied (`Migrations()` reads all SQL up front). — read the script lazily after the version check.

## Storage/Memory
- [rule] IdentityMap.cs vs WeakIndex.cs — duplicated weak dictionary (Count, Keys, Values, indexer, ContainsKey, TryGetValue, enumerators, Snapshot, Sweep, SweepEvery). — `IdentityMap` should compose a `WeakIndex<TKey,T>`.
- [rule] IdentityMap.cs:35, WeakIndex.cs:28 — AGENTS "ConcurrentDictionary.Values/Keys are copies" — `Count => _instances.Values.Count(...)` copies under every lock. — enumerate the dictionary (`_instances.Count(pair => pair.Value.TryGetTarget(out _))`).
- [bug] IdentityMap.cs:108-113 — `Lazy<Task>` with `ExecutionAndPublication` caches an exception thrown synchronously by `load(key)`; the `ContinueWith` that removes the entry is never attached, so the key fails forever. — create the task eagerly via `Task.Run`/`async` lambda, or `LazyThreadSafetyMode.PublicationOnly`.
- [style] IdentityMap.cs:108 — lambda parameter `key` hides the outer `key`. — `static (k, l) => ...` with `load` passed as state.
- [style] IdentityMap.cs:148 — unused lambda parameter `completed`. — `_`.
- [rule] OwnedLists.cs:24 / MemoryRepository.cs:35 — AGENTS async rule: `GetOrLoadAsync` and `LoadAsync` take no token, so a caller's wait cannot be cancelled (callers hold one; e.g. PostgresRestrictionRepository.cs:84). — add a token that cancels the caller's wait only (as `IdentityMap.GetOrAddAsync` does).
- [doc] MemoryRepository.cs:15-31,35-51 — protected abstract/virtual members undocumented (`WriteSql`, `KeyOf`, `RootOf`, `WriteParameters`, `EraseCommand`, `FindAsync`, `Track`, `IsBeingRemoved`).
- [style] MemoryRepository.cs:25-28 — `EraseCommand` throws `NotSupportedException` by default. — only repositories that delete should have it (interface or abstract subclass).
- [style] OwnedLists.cs:99 — `Overlap(key)` name says nothing. — `RestartLoads`.

## Storage/Files
- [rule] SafePath.cs:16-42 — AGENTS "Every name written from untrusted input avoids the names Windows keeps for devices … a derived file gets `_`" and the reader "owns the input checks" — `SafePath.Combine` neither rejects invalid file-name characters nor reserved names, trailing dot/space; a name `a.png:stream` creates an NTFS alternate data stream. `FileBeatmapsetStorage.PathOf` (lines 656-677) has its own stricter, duplicate rules. — one shared name validator (invalid chars from `Path.GetInvalidFileNameChars()`, `ReservedNames`, trailing '.'/' ') used by `SafePath`, `FileBeatmapsetStorage.PathOf` and `OsuBeatmapsetReader.IsSafeName`.
- [rule] DataStorage.cs:148, FileFaqStorage.cs:70 — AGENTS "no part of an uploaded name reaches a temporary file name" — the temp name is `path + "." + random + ".tmp"` (contains the target name, can exceed 255 chars). Same code twice (DataStorage.SaveAsync and `EntryWriter`). — `Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp")`, one shared helper.
- [bug] FileFaqStorage.cs:73-85 — `EntryWriter.Dispose` always moves the temp file into place, even if the writer is disposed because the caller failed halfway, replacing the previous entry with partial text (contradicts DataStorage's "a save that does not complete leaves the previous file"). — expose a `Commit()`; dispose without commit deletes the temp file.
- [style] FileMenuBannerStorage.cs / FileMenuSeasonalsStorage.cs / FileScreenshotStorage.cs — duplicated `ListAsync/SaveAsync/OpenAsync/DeleteAsync` (~30 lines ×3). — a base `FileContentStorage(DataStorage)`.
- [style] FileFaqStorage.cs:15,17,46 — ".txt" ×3 and `[..^4]`. — const `Extension`, `^Extension.Length`.
- [style] FileFaqStorage.cs:30,38 — `Files.Resolve(fileName); // comment` as a validation-by-side-effect statement. — `ArgumentNullException`-style `Files.EnsureSafe(name)`.
- [style] FileUserAvatarStorage.cs:15,21,27 — `$"{user.Id}"` (culture-sensitive) ×3; file is in `Content/` but implements a `Users` port. — `user.Id.ToString(CultureInfo.InvariantCulture)` once; move to `Users/`.
- [bug] FileReplayStorage.cs:26-28 — the old replay file is deleted *before* the new one is saved; a failing save loses the replay. — `Files.SaveAsync` first, then delete `old`.
- [rule] FileReplayStorage.cs:47-71 — a file storage looks up users and beatmaps in repositories and owns the osu! mode names (`Osu`, `CatchTheBeat`, …) — AGENTS "Storage only stores", "Knowledge about a Domain value lives on that value". — the name of an exported replay belongs to Domain/Application (`ScoreRecord`/`ReplayName`); the storage takes the name.
- [style] FileReplayStorage.cs:37-45 — `Find` pattern `"{id}*.osr"` matches every id starting with that number (1 → 1x, 1xx…) and scans them all on each read. — search `"{id} *.osr"` and `"{id}.osr"`.
- [rule] DataPaths.cs:17-19, 71-74; StorageStartup.cs:45-48 — documented-as-history migration code (`Songs`, `Beatmaps`, `Cache/Staging`, `Cache/Beatmaps`) with literals outside `DataPaths`. — if still needed, name them in `DataPaths` (`FormerBeatmapsetDirectories`); else delete.
- [doc] DataStorage.cs:94-97 — `pattern` doc says "OS search pattern" (mechanism). — "A name pattern with `*` and `?` wildcards".
- [style] StorageStartup.cs:53-75 — two identical try/catch helpers `DeleteLeftoverFile`/`DeleteLeftover` swallowing silently, no log. — one `TryDelete(string path)`, log a warning.

## Storage/Beatmaps
- [bug] PostgresBeatmapRepository.cs:84-97,100-118,144-150; PostgresBeatmapsetRepository.cs:329 — AGENTS "a row whose deletion is still queued never comes back into memory" — `Track(...)` returns the loaded row unchanged when `IsBeingRemoved` is true, and `GetAsync(Md5)`, both `ListAsync` and the set listing then `Index()` it and return it (revived through `_byHash`). — skip rows where `IsBeingRemoved(row)` before indexing/adding to results.
- [rule] PostgresBeatmapsetRepository.cs:219-257 — AGENTS "services/DI": `IServiceProvider` injected as a service locator and a downcast to the concrete `PostgresBeatmapRepository` (`(PostgresBeatmapRepository)services.GetRequiredService<IBeatmapRepository>()`) to break a cycle; the two repositories call each other's `internal` methods (`StoreAsync`, `Apply`, `Replace`, `Forget`). — extract the shared difficulty store (`BeatmapStore`) both use, or inject `Lazy<PostgresBeatmapRepository>` (deferred port).
- [rule] PostgresBeatmapRepository.cs:42-67 — "No operation waits for the database unless its result comes from the database" — when the beatmap is not in `Items` (weak map released it) even a lock/visible change takes the synchronous try-operation path. — only take it when the hash actually changed (compare with the instance passed in).
- [bug] PostgresBeatmapsetRepository.cs:253,280 / DatabaseWriter — `CommitAsync` runs under `Root.Beatmapset(0)` while queued `UpdateAsync/RetainAsync` copies of the same set sit in `Root.Beatmapset(id)` queues; nothing orders them, so an older queued `update beatmapsets` can run after the commit and put stale `archive_hash/updated_at` back (the XML claim "runs after every other commit" is not implemented by the writer). — wait for the set's root queue (or enqueue the commit on every touched root) before the transaction.
- [rule] PostgresBeatmapRepository.cs:435-483 vs PostgresBeatmapsetRepository.cs:506-525 — duplicated row→`BeatmapsetRecord` mapping including the same comment about fingerprints and the `UnixEpoch` fallback; the join result is converted to a record and then replaced by `LiveSetAsync` (a possible extra DB read). — share `BeatmapsetRow.ToRecord()`; take the live set from `beatmapsets` only when in memory.
- [style] PostgresBeatmapRepository.cs:128,567,575,583 and PostgresBeatmapsetRepository.cs:245,306,356 — the same 30-column select/insert lists ×7. — `const string SelectColumns` / `InsertColumns`.
- [style] PostgresBeatmapsetRepository.cs:544-552 — `StoredBeatmap(BeatmapRecord)` is a one-field wrapper and `StoredSet` exposes `List<StoredBeatmap>`; two types in the repository's file. — drop `StoredBeatmap` (use `BeatmapRecord`), `IReadOnlyList`, own file.
- [style] PostgresBeatmapRepository.cs:25-26 — two contradictory duplicated comments about jsonb. 362 double blank line.
- [rule] BeatmapQueryFilter.cs:152 (file line 111) — the osu file name normalisation exists in Domain (`BeatmapMetadata.NormalizeOsuFileName`) and again as a SQL regex; they must stay in sync and the SQL is not index-friendly. — store the normalized name or add a Domain/SQL pairing test; at least comment the coupling.
- [style] BeatmapQueryFilter.cs:AddInterval/AddDateInterval (file lines 122-163) — duplicate except `.ToUniversalTime()`; parameter names `mode`, `starsMin` lower-camel vs PascalCase elsewhere; `p`, `i` names (line 76-78). — one method with a converter; `PascalCase`.
- [style] FileBeatmapsetEntries.cs:1-41 — file name matches neither type; two unrelated-named types (`FileBeatmapsetArchive`, `FileBeatmapsetFolder`) in one file; `internal string Path => path` shadows `System.IO.Path`. — one file each, `FullPath`.
- [rule] FileBeatmapsetStorage.cs:1-797 — Organization god class: set folders + derived files + packing + FileSystemWatcher debounce + naming/safety rules. — extract `BeatmapsetFolderWatcher` (410-507, 602-624), `BeatmapsetNames` (FolderName, PathOf, LeadingId, IsReservedName, IsHidden), keep storage operations.
- [bug] FileBeatmapsetStorage.cs:68 — `static ConcurrentDictionary<int, SemaphoreSlim> SetLocks` is never trimmed (one semaphore per set id forever) and is process-static for an instance class (shared by tests/instances). — instance field with removal when idle, or a keyed lock.
- [bug] FileBeatmapsetStorage.cs:139-143 — the case-only folder rename goes through `placedParent + ".renaming"`, a visible top-level name: the watcher/listing can see it as a set, and a crash leaves it behind (`StorageStartup` removes only hidden names). — rename through an `.incoming_` name (`Incoming()`).
- [bug] FileBeatmapsetStorage.cs:629-635 — `File.Exists || Directory.Exists` then `File.GetAttributes` races with a deletion and throws `FileNotFoundException` out of the watcher iterator (kills `BeatmapsetStorageWatcher`, see above). — `try`/catch `IOException` → `null`.
- [bug] FileBeatmapsetStorage.cs:410-433 vs 603-624 — the `FileSystemWatcher` and debounce state are created when `WatchAsync` is called but disposed only when the sequence is enumerated to its end; an unenumerated sequence leaks them. — create the watcher inside the iterator.
- [bug] FileBeatmapsetStorage.cs:709-711 — folder names are shortened to 150 *chars* (up to 450 UTF-8 bytes > 255-byte limits on Linux) and may cut a surrogate pair; `FileReplayStorage.Fitted` already does it by bytes. — one shared byte-aware, surrogate-safe fit function.
- [rule] FileBeatmapsetStorage.cs:727-752,340,358,379 — every `Exists/Open/Save/Clear` calls `FindFolder` → `Directory.EnumerateDirectories` of the whole `Beatmapsets` directory (tens of thousands of sets) per asset request. — try `Path.Combine(root, FolderName(set))` first and enumerate only on a miss; `candidates.MinBy(...)` (751) is redundant with the ordered list (`candidates[0]`).
- [style] FileBeatmapsetStorage.cs:120,228,252,289,512 — the five-line "get semaphore, wait, try … finally release" block ×5. — `await using var _ = await LockAsync(set.Id, ct)`.
- [style] FileBeatmapsetStorage.cs:422-424,430 — three identical handlers; `watcher.Error += (sender, args) => _ = ReportEveryEntryAsync(...)` (discarded-task lambda, unused `sender`); 2 s debounce literal (460). — one handler; `(_, args) => ReportEveryEntryAsync(args.GetException())`; const `Debounce`.
- [style] FileBeatmapsetStorage.cs:124-131 — `placedParent is not null && placedGrandparent is not null && …` with later `placedParent!` bangs; 184-189 two-line locals `otherMarked`. — simplify; `FoldersOf` uses query syntax (732) unlike the rest.

## Storage/Users, Auth, Chat, Content (repositories)
- [rule] PostgresUserRepository.cs:219 — AGENTS "Key normalization lives on the Domain value (`UserData.NormalizeName`)" — `text.ToLowerInvariant().Replace(' ', '_')` re-implements it for the LIKE pattern. — expose a Domain function for the search form (e.g. `UserData.NormalizeSearch(text)`).
- [style] PostgresUserRepository.cs:76 — `_ = new UserData { Name = name };` validation by constructing a discarded object. — `UserData.ThrowIfInvalidName(name)`.
- [style] PostgresUserRepository.cs:79,111 — `newSafe`, `safeName` (old "safe name" vocabulary after `NormalizedName`). — `newNormalized`, `normalizedName`.
- [rule] PostgresRelationshipRepository.cs:24,43; PostgresRoundScoreRepository.cs:24 — "No operation waits for the database unless its result comes from the database" — `CreateOrUpdateAsync/DeleteAsync/RecordAsync` await `users.GetAsync`/`rounds.ListAsync` (can hit the DB on a miss) only to find the live owner. — use `relationship.Actor`/`roundScore.Round` (already live objects) or search memory only.
- [rule] MatchReportRepository.cs:17-19 — "A derived value stored again" / "exposing mutable state" — building a report writes `round.Result` onto the live, shared `Round` objects on every call (concurrent reports and stale results between scores). — return the decided results inside `MatchReport` without mutating `Round`.
- [style] PostgresRoundRepository.cs:17,121,144 — key and property `Index` vs column/parameter `Number` (`Number = round.Index`). — one name.
- [style] PostgresMatchRepository.cs:153-176 — `WithCreatorAsync` copies `MatchData` field by field and `Parameters` duplicates `WriteParameters` minus `Id`. — one method with optional id.
- [style] PostgresUserStatsRepository.cs:74-85 — five pointless locals before the anonymous object. — `return new { stats.UserId, Mode = (int)stats.Mode, stats.TotalScore, stats.RankedScore, stats.PlayCount };`.
- [style] PostgresRoundScoreRepository.cs:36 — `x is { } t ? (int)t : (int?)null` → `(int?)roundScore.Team`. PostgresLoginRepository.cs:26 same shape.
- [style] PostgresScoreRepository.cs:58,130 — magic `UserId ?? 0` ×2 (root of anonymous scores). — `Root.User(0)` named const `Root.Anonymous`. 61-66/109-110/137-138: column list ×3.
- [style] PostgresSettingsRepository.cs:32-45,60-81 — setting names as literals twice; `Interlocked.Exchange(ref _settings, settings)` result unused (`Volatile.Write`); `(GetType(), 1)` magic. 
- [bug] PostgresSettingsRepository.cs:34,38,88 / PostgresMenuBannerRepository.cs:110 — `int.Parse`, `new Uri(..., Absolute)` throw on a corrupt stored value and abort start-up, while an invalid image is logged and skipped (97, 65). — be consistent: log and keep the default.
- [style] PostgresLoginRepository.cs:23, MenuBanner Upsert:85-87, others — `.ToUniversalTime()` repeated although `WriteCommand` converts every `DateTimeOffset`. 
- [rule] PostgresLoginRepository.cs:24-26 — `ClientDate` stored as a formatted text and `ClientStream` as int cast — typed values should be `DateOnly`/enum columns (Npgsql supports them). — Question if the schema is fixed.
- [style] PostgresModerationLogRepository.cs:72-96, PostgresRelationshipRepository.cs:63-74 — N+1 awaited user lookups in a loop; entries whose user is missing are silently dropped so `total` no longer equals the page. — batch by id set; keep total consistent.
- [doc] Content/PostgresMenuBannerRepository.cs / PostgresChannelRepository.cs — fine; PostgresCredentialRepository.cs:63 nested `internal` class used in the base type list needs doc on members.

# Infrastructure.Capabilities

- [bug] HttpBeatmapsetMirror.cs:115-118,127 — `Artist`, `Title`, `Creator`, `LastUpdate` (RawSet) and `DiffName` (RawBeatmap) are get-only properties: System.Text.Json never fills get-only properties, so every mirror result has empty names and `LastUpdate` parses to the epoch. — `{ get; init; } = "";` (AGENTS: do not remove a deserialization setter on the IDE's word).
- [bug] HttpBeatmapsetMirror.cs:49 — `catch … or TaskCanceledException` swallows the caller's cancellation and logs a "mirror failed" warning. — `when (… && !cancellationToken.IsCancellationRequested)`; rename `ex` → `exception`.
- [rule] OsuBeatmapsetReader.cs:222-232 — see SafePath finding: the reader "owns the input checks" but accepts names the storage later refuses (`?`, `*`, `CON`, trailing dot) so an import fails late at `PathOf`. — share the validator.
- [rule] OsuBeatmapsetReader.cs:204-220 — entry/total size limits use the *declared* `entry.Length`; `SaveArchiveWithoutVideoAsync` (FileBeatmapsetStorage.cs:287-333) recompresses every entry without a read cap. — enforce the cap while copying (limited stream) — see Questions.
- [rule] OsuBeatmapAnalyser.cs:57 (file line 187-188), BeatmapAssets/others: `$"{hash}.osu"` is repeated in `BeatmapsetFileAudit.cs:30`, `BeatmapsetFileWriter.cs:45` and here — "Knowledge about a value lives on that value". — `BeatmapRecord.VersionFileName`.
- [rule] OsuBeatmapAnalyser.cs:143-181 (file lines 144-181, 261-300) — `o.Total = a + b + c` stores a derived value ("Derive whatever is derivable"); four copy-pasted blocks with `h`/`o` names and a 4-`ref` recursion. — `Total` derived in Domain; per-mode private methods; a small counter object for Catch.
- [rule] BeatmapAssets.cs:327-329 (file lines 89-91 of class: `File.OpenRead(outputPath)`) — a file read asynchronously by `storage.SaveAsync` is opened without `FileOptions.Asynchronous`. — `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous)`.
- [style] BeatmapAssets.cs:274-277 — `catch (OperationCanceledException) { throw; }` before `catch (Exception)`. — `catch (Exception exception) when (exception is not OperationCanceledException)`.
- [style] BeatmapAssets.cs:350,346 — ten-second preview duplicated as `10`, `st=9`, `d=1`; `FfmpegOptions` allocates a new `FFOptions` per access. — consts, field.
- [style] BeatmapAssets.cs class name — the other capabilities carry the technology (`Osu…`, `Http…`, `ImageSharp…`). — `FfmpegBeatmapAssets`.
- [style] OsuBeatmapContent.cs:1-46 — two unrelated types in one file (`OsuBeatmapContent`, `StreamlessWorkingBeatmap`). — one file each.
- [style] OsuBeatmapsetReader.cs:44-46,93-99,195 — manual `'/'` check duplicates `Normalise`; "Unknown" ×3; British `Normalise` vs `Normalize` elsewhere; `meta` is a `BeatmapInfo`. — use `Normalise`/`OrUnknown` helper, one spelling.
- [style] OsuBeatmapsetReader.cs:113-125 — hand-rolled bounded read loop with a per-call `new byte[81920]`. — `ArrayPool` or a limited-stream `CopyToAsync`.
- [style] OsuBeatmapsetReader.cs:84 / HttpBeatmapsetMirror.cs:84 — unchecked `(GameMode)info.Ruleset.OnlineID` / `(GameMode)beatmap.Mode` of external ints. — `Enum.IsDefined` or skip the difficulty.
- [style] HttpBeatmapsetMirror.cs:39-42,64-65 — URLs built by string concatenation; `ms` abbreviation and a magic `1_000_000_000_000`. — `UriBuilder` / named const.
- [doc] ImageSharpScreenshotInspector.cs:6-8 (file lines 8-9 of the class, `/// <inheritdoc />` on the class) — the class has no summary (inheritdoc of an interface on a class gives nothing useful).
- [doc] AssetOptions.cs:6-9 (file lines 6,9) — `FfmpegFolder` doc not in the "Gets or sets" form; `CacheDirectory` is documented as a temp directory but named "Cache" (AGENTS: Cache = the on-disk derived-files directory `DataPaths.Cache`). — rename `TemporaryDirectory` or reuse `DataPaths.Cache`.
- [doc] OsuBeatmapAnalyser.cs:170,180 — `<exception cref="Exception">` documents nothing.

# Questions

- ConnectionHandler.cs:33 / AnticheatAnnouncer.cs:23,27 / RoomAnnouncer.cs: handlers `await` room locks (`lobby.ReleaseAsync`) and `Server.SayAsync/WhisperAsync` — AGENTS "Handlers are quick; slow work goes to its own queue". Does `SayAsync` or `ReleaseAsync` ever wait on a lock/DB? If yes they must queue (like `CommandWorker`).
- RuntimeStartup.cs:25-35 runs `channelRepository.ListAsync` which throws if `StorageStartup` has not loaded the residents — depends on hosted-service registration order in the Host. Is the order guaranteed (Storage before Runtime)?
- ChatCommands.cs:129-142 `!where` and MpCommands `ResolveUserAsync`: any chatter resolves any user (including via `#id`) with no permission check, and the order "user lookup before room lookup" lets a caller probe user existence. Intended public information?
- PostgresCredentialRepository.cs:23-24: unknown user returns before BCrypt (timing oracle for existing names). Accepted?
- DataPaths.cs:17-19: relative `DataDirectory` is resolved against `AppContext.BaseDirectory`, the docs say "the directory the server runs from" (current directory). Which is intended (docs/for-technicians/configuration.md)?
- DataPaths.cs/StorageStartup.cs legacy folders (`Songs`, `Beatmaps`, `Cache/Staging`): still needed, or removable before release?
- MpReplies.cs:350 `RoomResult.Silenced` mapped to "while you are silenced" for any restriction (spectating, upload…); AGENTS names the result `Restricted`. Rename in Application and generalise the text?
- BeatmapAssets temp files (`*.bin`, `*.tmp.mp3` in `AssetOptions.CacheDirectory`) are not cleaned by `StorageStartup.RemoveLeftovers` after a crash — acceptable?
- OsuBeatmapsetReader.cs:204-220: zip bomb defence relies on header `Length` (forgeable). Does .NET 10's entry stream enforce the declared length? If not, enforce while reading.
- AnticheatAnnouncer.cs:23 / RoomAnnouncer.cs: AGENTS says commands run scoped to the typist and the bot only replies; announcements use `Server.SayAsync` (system operation, labelled) — confirmed fine; but `Server.WhisperAsync(online, …)` staff whispers go to creator/referees only, not `RoomManageAny` holders.
- DependencyInjection classes in Infrastructure/Bot use `this IServiceCollection` while the code-principles skill asks for C# 14 extension blocks (Domain uses them). Keep classic form?
- PostgresLoginRepository.cs:24 `client_date` stored as text — is the schema (001_baseline.sql) fixed or may it become `date`?
