# Audit: src/Domain (2026-10-21, read-only)

Method: read every file in src/Domain (4659 lines), the identity/code-style/application-design docs, and AGENTS.md. Rider lint on 16 files showed only allocation hints (ignored per rules). Callers in Application/Bot/Hosts checked where a finding depends on them. No source edited. `bug?` means the claim needs a check against real osu! client packets.

## [bug]
- Mechanics/GameMods.cs:200-224, Multiplayer/MatchSettings.cs:30-33, Application/.../ScopedRoomSettings.cs:65, ScopedRoomSlots.cs:231: `IsValid` is `mods == RemoveInvalidMods`. RemoveInvalidMods strips DT from DT|NC and SD from PF|SD. bug?: stable sends Nightcore as DT|NC (576) and Perfect as PF|SD (16416), so a genuine NC or PF room or slot mod would return `InvalidMods`. tests/Domain/ModsTests.cs:9,18 pins the stripping, not acceptance of the client's form.
- Mechanics/GameWinCondition.cs:112: `x.Accuracy >= 1.0` is meant as "perfect accuracy", but `Accuracy` is a percentage (HitCounts.cs:70, 0-100). Any tie at 1% or more is broken by total score, so the "other tie is a draw" rule never applies.
- Mechanics/GameWinCondition.cs:49-56 vs :37-43: `Metric` silently maps an undefined condition to TotalScore, while `ToComparer` throws for it.
- bug?: Mechanics/GameWinCondition.cs:54 and :135: the Combo condition uses `CurrentCombo` (combo at map end), not `MaxCombo`. Check against osu! behaviour.
- Beatmaps/BeatmapDifficulty.cs:35-99: the `= Bpm` and `= Length` initializers write the backing field directly, so the constructor path never runs the validation. `new BeatmapDifficulty(mode, -5, ..)` is accepted. Only `with` and object-initializer syntax validate. `ThrowIfNegative` also lets NaN and Infinity through (Bpm, Star).
- Content/ContentFingerprint.cs:22-30: same bug. `new ContentStamp(-1, x)` is accepted. `ModifiedAt` is UTC-normalized only in the constructor path; `with { ModifiedAt = local }` bypasses it.
- Scores/HitCounts.cs:24-61, Scores/ScoreRecord.cs:81-89: the validation lives in the property initializer, so `with { Num300 = -5 }` or `with { TotalScore = -1 }` bypasses it. HitCounts has auto `init`; ScoreData TotalScore/MaxCombo likewise.
- Scores/ScoreRecord.cs:65-77 (ScoreData): `CurrentCombo` is unvalidated (negative accepted). `UserId` has no >=1 check. `Mode`, `Grade` and `Mods` are not checked for being defined. Mods are not checked with `IsValid(Mode)`. Grade, IsFullCombo and IsPassed are taken as given and not checked against hit counts.
- Users/UserRecord.cs:131: the regex `^...+$` lets `$` match before a trailing `\n`, so the name "abc\n" passes validation. Use `\z`.
- Users/UserRecord.cs:85: a null name throws NullReferenceException, not ArgumentException. The same null-throws-NRE pattern is in GeneralChannelData.Topic (Chat/GeneralChannelRecord.cs:55) and NetworkAdapters.Adapters (Bancho/BanchoFingerprint.cs:38).
- Users/UserRecord.cs:101: `Country` has no `ThrowIfUndefined`; any byte is accepted.
- Bancho/BanchoFingerprint.cs:41: the `IsNullOrWhiteSpace` check is unreachable, since an empty or whitespace string already fails `IsValidAdaptersString`. `"."` and `"a..b."` are accepted (RemoveEmptyEntries), although the doc (:12-27) says empty chunks are forbidden. `default(NetworkAdapters).IsRunningUnderWine` throws NRE.
- Multiplayer/RoundScore.cs:25: the `Team` init reads `Round.Settings`, so it throws NRE if `Team` is assigned before `Round` in an initializer. It also goes stale because `MatchSettings.TeamType` is mutable.
- Multiplayer/RoundResult.cs:35,133,180-187: with a single contender whose metric is 0 (accuracy 0, score 0), `Margin` is 0 so `IsDraw` is true, yet `WinnerId` or `Winner` is set. HeadToHeadResult.WinnerId also becomes null for a non-draw when `UserId` is null.
- Multiplayer/MatchRecord.cs:17: `ThrowIfNegative` accepts Id 0. Every other record requires >=1 (User, Score, Beatmap, Beatmapset).
- Multiplayer/MatchSettings.cs:14-35: Mode and Mods validate against each other, so switching both needs the right order (clear the mods, then change the mode). `Freemods` is not checked against `Mods` (`GameModsGroups.SpeedChanging` exists but Domain never enforces it).
- Mechanics/GameMods.cs:13,17: `KeyCoop` is not in `ManiaSpecific`, so it survives in Standard, Taiko and Catch. Undefined bits (for example bit 31) are not stripped or rejected. Mania keeps HardRock, though mania has no HR mod (bug?).
- Content/MenuBanner.cs:19,25,31: `Url` accepts any URI (relative, `javascript:`). StartsAt and EndsAt are not checked for order and not forced to UTC. `IsCurrent` includes EndsAt (`<=`), while `RestrictionData.IsActive` excludes it (`<`).
- Settings/URLs: ServerSettings.cs:50,68,79 uses `ThrowIfNotAbsolute` only, so `file:` or `ftp:` is accepted for the mirror endpoints and menu-icon URL. `UriExtensions.IsHttp` exists but is not used here.
- Content/ContentReference.cs:29-60: path rules do not cover Windows device names (CON, NUL.txt), trailing dot or space in a segment, or `<>"|?*`. The path comparison is case-sensitive, so `Menu/A.png` and `menu/a.png` are different references to one file. `Parse(" ")` throws ArgumentException, not the documented FormatException. `ToString(format, provider)` ignores format, although its doc says it throws for formats other than "G".
- Beatmaps/BeatmapAssets.cs:10-19: file names are raw strings with no validation (NUL, separators, device names). `PreviewTime` can be negative.
- Beatmaps/BeatmapMetadata.cs:16: `Version` accepts empty or blank (BeatmapsetMetadata rejects it). BeatmapDifficulty.Mode is not checked against the BeatmapObjects subtype, so a Taiko difficulty with OsuObjects is accepted.
- Beatmaps/BeatmapObjects.cs:17-36: `Total` (derivable from the parts) is stored and not checked against the sum.
- Chat/Message.cs:10: Content is not validated (null, empty, NUL, length). GeneralChannelRecord.Name has no max length and no control-character check.
- Users/UserStats.cs:31-37: TotalScore, RankedScore and PlayCount accept negative values; RankedScore > TotalScore is accepted. `UserId` has no check.
- Users/Country.cs:290-309: `Describe()` uses `RegionInfo` (culture and ICU dependent; fails under invariant globalization).

## [rule]
- Auth/Login.cs, Auth/LoginEntry.cs: two types for one concept (a login), not covered by the XRecord/XData pattern.
- Scores/Submission.cs, Scores/ScoreRejection.cs: they are score-submission input and result data. AGENTS.md puts such plain data in Application.Storage. Bancho/* (BanchoFlags, BanchoFingerprint, BanchoVersion, BanchoClient) is client wire data. Beatmaps/BeatmapsetStatus.cs:45-101 `ToOsuApi`, `FromOsuApi` and `FromOsuDirect` are wire and web-API mappings, and a Protocol or Infrastructure project should own them. They also map unknown values to `UpdateAvailable`, so garbage is accepted silently.
- Runtime-only types in Domain: Users/UserActivity.cs and Users/PresenceVisibility.cs (used only by Application PlayerStatus and the hosts) and Common/ConcurrentSet.cs (thread-safe collection for runtime objects).
- Users/UserStats.cs:15-22: fixed fake values (`Accuracy` 100.0, `Pp` 727, `Rank => UserId`) are presentation constants and belong in the host or Protocol. `Pp` also sits oddly with "no pp in gameplay".
- Time from the wall clock: Users/Relationship.cs:38 and Content/MenuBanner.cs:34 default `CreatedAt = DateTimeOffset.UtcNow`, which is not testable. Use a `TimeProvider` in the caller. No DateTimeOffset in Domain is checked for UTC offset (only ContentStamp normalizes).
- Naming: MatchEvent.cs:19-20 uses `Actor` where the rule says `by`. Relationship uses `Actor`/`Target`. `Submission.HashByClient` and `ScoreData.Checksum` name the same concept differently. `RefAdded` and `RefRemoved` abbreviate "Referee". BeatmapDifficulty `Cs/Ar/Od/Hp` are abbreviations. The booleans `Locked`, `Visible`, `Aborted` and `Freemods` do not use an `Is` prefix, unlike IsPrivate and IsPassed.
- Beatmaps/BeatmapRecord.cs and BeatmapsetRecord.cs have mutable `set` members and no XData split (UserRecord, ScoreRecord and MatchRecord use Id plus Value). Multiplayer/Round.cs is named `Round`, not `RoundRecord`. `LocalIdFloor` is duplicated in both beatmap records.
- Round.cs:35 stores `Result`, which `RoundResult.Decide` derives from the scores ("derive what is derivable"). BeatmapObjects.Total is derivable too (see [bug]).
- Multiplayer/MatchRecord.cs:79 makes `Creator` nullable ("unattended room"), contradicting "every room has a creator". EndedAt is not checked against StartedAt (the same applies to Round.EndedAt).
- Mechanics/GameMods.cs:115 names the class `ModsExtensions` (its siblings are GameTeamTypeExtensions and GameWinConditionExtensions). Parsers `FromModString` and `FromNowPlayingString` accept and ignore garbage chunks (documented, but "accept garbage"). `CountSetBits` hand-rolls `BitOperations.PopCount`.
- Multiplayer/RoomPermissionsGroups.cs:24: `GetPermissions(this RoomRoles)` is a role-to-rights extension inside a `*Groups` mask class; it belongs next to RoomRoles.
- Beatmaps/BeatmapRecord.cs:85,92: `IsLocked()` and `IsVisible()` are parameterless derived values written as methods; make them properties.
- Namespace `Basil.Domain.Permissions` contains the enum `Permissions`, forcing `Permissions.Permissions` in UserRecord.cs:106.
- Collections are fine: HitCounts, MatchReport and RoundResult.Scores expose read-only types. RoundResult.cs:165 builds per-team lists with `ToList()` (a snapshot, acceptable).

## [doc]
Docs vs code:
- identity-permissions.md and application-design.md list areas "Chat, Multiplayer, Spectating, Beatmapsets, Users, Server". The code has Chat, Multiplayer, Spectating, Scores, Beatmapsets, Screenshots (PermissionsGroups.cs:23-49). The docs call the named masks `Silence` and `Restrict` on `Restriction`. The code has `RestrictionData.Silence` and `RestrictionData.Restricted` (Restriction.cs:40-47). AGENTS.md names the factory `BeatmapObjects.NewFrom(mode)`; the code has `Empty(mode)`. AGENTS.md says "created from BeatmapsetData", which now lives in Application.Persistence, not Domain.
- Undocumented public members: all 252 `Country` enum members (Users/Country.cs:13-263). Everything else public has `///`.
- Wrong or leaking XML docs:
  - IMatchEntry.cs:6 says "Gets or sets", but the member is get-only.
  - Round.cs:20-25 documents `BeatmapHash` as "currently selected beatmap, null if none", but the type is non-nullable `Md5`.
  - Submission.cs:21 says "Gets or sets"; the member is init-only.
  - BeatmapsetStatus.cs:4 says "of a beatmap"; it is a beatmapset.
  - GameMods.cs:9 says the key mods "only apply to osu!mania"; KeyCoop is not enforced.
  - Grade.cs:34,40 omit Flashlight for Sh and Xh.
  - BanchoFingerprint.cs:96 says GetHashCode is case-insensitive; it is actually the boxed Md5 hash.
  - BeatmapObjects.cs:10 and BeatmapDifficulty.cs:17 describe wire serialization ("mode discriminator", "seconds on the wire"). MenuBanner.cs:4 names an endpoint path. That is implementation, which fails the Implementation Test.
  - Scores/HitCounts.cs:117 uses `nameof(GameMode)` as the parameter name; it should be `mode`.

## [style]
- GameMods.cs:255 allocates an array on every call (hoist it). BanchoFlagsGroups.CheatSigns excludes ExtraThreads, Sdl2Library, OpenSslLibrary, AqnMenuSample, RunWithLdFlag and ConsoleOpen; confirm that is intended. `PermissionsGroups.All` is hand-listed and has no sync test.
- RoundResult.cs:207,213: `TeamScores` and `TeamScore` are easy to confuse.
- Privileges.Permissions (Privileges.cs:51-54): ModeratorPermissions lists UserSilence, UserNote and UserViewLogins that UserRestrict already contains (redundant).
- Privileges derivation (Permissions.cs:184-197) is "any bit held". A user with only `ChatMessageAnyone` or `ScreenshotDelete` is reported as Moderator, and a RoomObserve holder as Player, Supporter and Tournament. This matches the doc, but it is worth a deliberate decision.

## Verified OK
- Permissions enum has no group members and no overlapping bits. `All` covers every member. Larger permissions contain their smaller ones (RoomObserve, RoomManageAny, BeatmapsetManage, UserSilence, UserRestrict, UserManage).
- `Effective` follows the rules: expiry needs no timer, active restrictions are subtracted, and the manager of an area keeps that area's bits. It uses granted permissions for the manager test, per the docs.
- RestrictionData validates a defined, non-empty mask, and StartsAt/EndsAt in either assignment order. `Privilege` is spelled out everywhere (no `Priv`). No ConfigureAwait in Domain. `NormalizedName` is derived from `Name`.
- HitCounts accuracy formulas match osu! for Standard, Taiko, Catch, Mania and mania ScoreV2 (305).
- Domain has no slot-count concept; the 1..16 bound lives in Application (RoomSlots.MaxSlotCount). MatchSettings has no password, beatmap or slot count.

## Behaviours worth testing, per folder (tests/Domain is flat today; mirror these)
- Permissions/: `Allows` with empty and partial masks. Each larger permission implies its smaller ones. `All` equals the OR of all members (reflection). `Effective`: expired, future and open-ended restrictions; a manager of each area keeps its bits; silence keeps spectating. Privileges for each role and the derived privilege mapping (RoomObserve gives Supporter). Default permissions. RestrictionData: reject None and undefined bits; start after end rejected in both assignment orders; `IsActive` at the boundaries. ModerationData: reason at 2048 and 2049.
- Users/: Name rules (length 3..15, spaces, digits only, charset, trailing newline). NormalizeName equivalence ("A B" and "a_b"). DeletedAt cannot be cleared. Id >= 1. Relationship: self-relation rejected in both orders; equality by actor, target and type. UserStats equality. BasilBot.Is.
- Auth/: Login and LoginEntry value semantics (two Logins never equal). Credentials requires a valid Md5.
- Bancho/: NetworkAdapters valid and invalid strings, the wine sentinel, equality by hash only. BanchoFlagsGroups.CheatSigns membership. Unknown bits kept in Submission.
- Common/: Md5 (length, hex, case, default value). ThrowIfUndefined for plain and flags enums. NearlyEqual (zero tolerance defaults). HasNul. ConcurrentSet set operations.
- Content/: ContentReference parse and normalize (backslash, `..`, absolute paths, drive letters, http versus path, default value, round trip). ContentStamp negative size and UTC. MenuBanner window (`IsCurrent` boundaries), the default image rejected, equality by Image. ServerSettings: defaults (Beatmapsets locked), NUL in Motd, non-absolute URLs rejected.
- Mechanics/: RemoveInvalidMods per mode and per conflicting pair (DT/HT, EZ/HR, NF/SD/PF/RX/AP, SO/AP, HD/FI, several keys, Standard-only and mania-only mods). `IsValid` accepts the client's wire form of NC (DT|NC) and PF (PF|SD). Parsers (case, odd length, unknown chunks, `|10K|`). GameTeamType flags. Win-condition comparers: score, accuracy (perfect tie versus non-perfect tie), combo, nulls, ScoreV2.
- Scores/: HitCounts accuracy for each mode, ScoreV2 mania, zero totals, invalid mode, negative counts including `with`. ScoreData validation (negative totals, `with`). Id > 0. Accuracy derived.
- Beatmaps/: BeatmapDifficulty validation through the constructor path (NaN, negative). OsuFileName and NormalizeOsuFileName. BeatmapsetMetadata empty fields and NUL. Local id threshold. IsLocked and IsVisible combine the set and the difficulty. BeatmapsetStatus conversions both ways and unknown values. BeatmapObjects.Empty per mode and negative counts.
- Chat/: GeneralChannelRecord name rules and case-insensitive equality. GeneralChannelData undefined privilege bits and NUL topic.
- Multiplayer/: MatchData name rules. MatchSettings mode and mods interplay. RoomPermissionsGroups per role (Player, Host, Referee, Creator, combined). RoundScore team presence per team type. RoundResult.Decide: no score gives null, head-to-head ordering with failed scores last, draw, single score, team result with an absent team, accuracy averaging, all-failed fallback. Round.Result must belong to the round. Round index > 0.
