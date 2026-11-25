# Layer audit: does each layer stay in its role?

Read-only audit against the roles fixed on 2026-11-25 (`docs/for-developers/architecture.md` "Layers", `AGENTS.md` "Hosts, Gateway and Server").
Every finding below was checked in the source; line numbers are those of the working tree at the time of the audit.

Severity: **high** = a business, privilege or state decision in the wrong layer; **medium** = derived logic or coordination in the wrong layer; **low** = wording or placement nit.

A finding that repeats in several hosts is listed once, under "Hosts (cross-cutting)", with every location.

## Summary

| Layer | High | Medium | Low | Total |
|---|---:|---:|---:|---:|
| Gateway (`Basil.Gateway`) | 0 | 0 | 0 | 0 |
| Host.Shared | 2 | 3 | 1 | 6 |
| Hosts (cross-cutting) | 0 | 8 | 1 | 9 |
| Host.Rest | 2 | 2 | 3 | 7 |
| Host.Stable | 5 | 4 | 4 | 13 |
| Host.Irc | 2 | 3 | 0 | 5 |
| Host.Cli | 0 | 1 | 2 | 3 |
| Protocol (Game.Stable, Irc) | 0 | 0 | 0 | 0 |
| Application | 0 | 1 | 1 | 2 |
| Infrastructure | 1 | 5 | 1 | 7 |
| Domain | 0 | 0 | 0 | 0 |
| Composition root `Basil` | 0 | 1 | 2 | 3 |
| **Total** | **12** | **28** | **15** | **55** |

The pattern: the hosts still hold the system's logic. The worst cases are privilege rules (who may read a channel, who may use osu!direct, which prefix a member shows), authority checks ("only the account itself"), state checks ("the player is in game"), and the direct relay of spectator and score frames. The Gateway, the Protocol projects and the Domain are clean. Application and Infrastructure are mostly in role; their findings are rules that sit one layer too low or too close to the caller.

## Gateway (`Basil.Gateway`)

No findings. `Admission`, `GatewayLimits`, `RateLimiter`, `LoginAttemptGuard` and `LoadSignal` decide Allow/Deny from rate, concurrency and load only. They know no user type and no business fact. `AdmissionSubject` and `AdmissionTier` arrive from the caller. (Gap, not a finding: the Gateway uses no Application service yet, so every per-object or per-user policy below lives in a host. That is the root cause of the X1, X2, X3 and HS1 findings.)

## Host.Shared

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| HS1 | `Basil.Host.Shared/Errors/AdmissionRefusals.cs:20-26` | `SubjectOf` reads the session's effective privileges and gives the `Tournament` privilege the `Elevated` tier (4x allowance). Called by `RestAdmission.cs:20`, `StableAdmission.cs:19`, `IrcListener.cs:217`. | A privilege rule that sets how much a user may do. Per the new decision this is the Gateway's guard, not a host's. | Gateway, fed by an Application capability (for example an `AdmissionSubject` offered by `Session`/`UserSessions`) | high |
| HS2 | `Basil.Host.Shared/Views/RoomCommands.cs:58,134-137`; used at `Basil.Host.Rest/Routes/RoomRoutes.cs:123`, `Basil.Host.Cli/Commands/ChannelRoomCommands.cs:213`; duplicated at `Basil.Infrastructure.Bot/Handlers/Commands/Mp/MpCommands.cs:24`, `MpCommands.Settings.cs:212`, `MpCommands.Lifecycle.cs:22` | A room name is cut to 50 characters before `OpenRoom`. The same constant and cut exist four times. The Stable host does not cut at all (`PacketHandlers.Multiplayer.cs:67` passes `match.Name` through), so the limit differs by entry point. | A business rule (valid room name) enforced by callers, in three layers, and missing from Domain. | `Domain` room settings (reject or truncate there); the hosts and the bot pass the name through | high |
| HS3 | `Basil.Host.Shared/Views/UserViews.cs:23-30` (used at `UserRoutes.cs:112,124,145,165`, `UserCommands.cs`) | `UserView.Of` takes `UserRegistry`, resolves the live user, computes `EffectivePrivileges(now)` and `IsSilenced(now)`. | The host derives effective privileges and silence state from live objects. A view should only reformat what the system returned. | A query/handler in Application that returns these values (the view then maps them) | medium |
| HS4 | `Basil.Host.Shared/Views/SettingsCategories.cs:43-49` with `Basil.Host.Rest/Routes/ContentRoutes.cs:62-85` and `Basil.Host.Cli/Commands/ContentCommands.cs` (settings set) | The host reads the settings, merges a JSON patch into a category, rebuilds `ServerSettings` and sends the whole object back (`GetSettings` then `UpdateSettings`). `LocksView.ToLocks()` maps flags too. | A read-modify-write of a business aggregate in the host; two writers can overwrite each other. | An Application message `PatchSettings(category, patch)` or a typed change record | medium |
| HS5 | `Basil.Host.Shared/Views/RoomCommands.cs:87,108-112` | Chooses `StartRoomCountdown` over `StartRound` when `seconds > 0`; defaults a timer to 30 s (`Seconds ?? 30`, and `DefaultTimerSeconds = 30` again in `MpCommands.cs:26`); defaults mods to `NoMod` and freemods to `false`. | Defaults and the choice between two operations are decisions. | The message (`StartRound` with an optional countdown) and the Domain/Application default | medium |
| HS6 | `Basil.Host.Shared/Views/RoomCommands.cs:131` | `Describe` hard-codes in prose who may give each command, including "and by a user with the Tournament privilege". | The privilege rule is restated as text in the host and can drift from `RoomPrivileges`. | Generate from `RoomPrivileges` or leave to the Application's capability description | low |

## Hosts (cross-cutting)

| ID | Locations | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| X1 | `Basil.Host.Rest/Routes/AuthRoutes.cs:54,65`; `Basil.Host.Stable/Login/StableLogin.cs:50,69`; `Basil.Host.Stable/Web/WebAuthentication.cs:54` (and `attempt.Keep()` at the end); `Basil.Host.Irc/Commands/IrcCommands.cs:156,170` | Each host reserves an attempt in `LoginAttemptGuard`, lowercases the name as the key, and decides that only `UnknownUser` and `WrongPassword` count as failures. | Four copies of one policy. Which failures count is a rule, and enforcement is the Gateway's job. | Gateway guard, asked through an Application login service so the result type decides what counts | medium |
| X2 | `Basil.Host.Rest/Http/RestPipeline.cs:56-61` (non-GET = mutation, `RateRule.RestMutation`); `Basil.Host.Stable/Packets/PacketHandlers.Chat.cs:34` and `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:78` (`RateRule.Chat`); `Basil.Host.Rest/Sse/DiagnosticsRoutes.cs:72` (`RateRule.GcCollect`, 30 s); `Basil.Host.Stable/Web/OsuWebRoutes.cs:497` (`RateRule.Registration`); `Basil.Host.Rest/Sse/SseHub.cs:149,182-190` (`StreamsPerUser`) | Hosts call `RateLimiter.TryAcquire` or count open streams, and choose which requests are charged to which rule. | Rate and concurrency limiting is the Gateway's role. Hosts pick the rule, the key and the refusal. | Gateway policy per entry point (the target already named in `AGENTS.md`) | medium |
| X3 | `Basil.Host.Stable/Support/StableAdmission.cs:19-25` | Looks up the session by `osu-token` in `SessionRegistry` and, for an open Stable client posting to `/`, switches the work class to `Poll`. | The class of work is chosen from session state in the host. | Gateway, with the session passed in by the caller as a subject plus a declared policy | medium |
| X4 | `Basil.Host.Stable/Packets/PacketHandlers.cs:175-181` (`OnlineUsers`, keeps only `HasPresence`); `PacketHandlers.Chat.cs:74` (recipient by `NormalizedName`, silent drop when offline); `Basil.Host.Stable/Login/StableLogin.cs:117`; `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:124` (`FindUser`); `Basil.Host.Stable/Packets/PacketHandlers.Spectating.cs:12` | Hosts scan `SessionRegistry.Online`, decide who counts as visible, and drop requests for users they do not find. | Who is visible or addressable is a state/privacy rule. The host also bypasses the message layer. | Application messages (`FindOnlineUser`, `GetPresence`) that answer with a result | medium |
| X5 | `Basil.Host.Stable/Delivery/RoomDelivery.cs:33,144-153,169` (private rooms hidden from lobby watchers), `:186` (`NoMap` players excluded from a round start); `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:185-199` (who receives `away-notify`); `Basil.Host.Irc/Clients/IrcPresence.cs` with `Delivery/MembershipDelivery.cs:26` (host keeps its own channel membership to decide JOIN/PART/QUIT) | Delivery handlers pick recipients with a game rule, or rebuild membership state. | "Who takes part" and "who may see this room" are rules of the system. Delivery should only translate and send to the audience the event names. | Application events carry the audience (players, watchers) | medium |
| X6 | `Basil.Host.Rest/Routes/UserRoutes.cs:159-160,168-210` and `Basil.Host.Cli/Commands/UserCommands.cs:187-246` | A `PATCH`/`set` becomes up to five separate commands (rename, country, away, pmPrivate, privileges), run in a fixed order, stopping at the first refusal. Earlier changes stay applied. | The host owns the sequence and the partial-failure rule, and two hosts repeat it. | An Application message `UpdateUser` taking the change record | medium |
| X7 | `Basil.Host.Rest/Routes/ContentRoutes.cs:99-111` and `Basil.Host.Cli/Commands/ContentCommands.cs:446-458` | Announce: both hosts de-duplicate ids, look up each user with `GetUser`, stop with 404 at the first miss, then call `NotifyUsers`. | Recipient resolution and the "unknown user" rule, written twice. | `NotifyUsers` resolving and refusing on its own | medium |
| X8 | `Basil.Host.Rest/Routes/ChannelRoutes.cs:55-61` (`Visible ?? true`, `AutoJoin ?? false`, read/write `?? 0`); `Basil.Host.Cli/Commands/ChannelRoomCommands.cs:162-169` (the same defaults, again) | The hosts decide the defaults of a new general channel. | Defaults of a business object belong with the object. | `GeneralChannelData` defaults in Domain | medium |
| X9 | `Basil.Host.Stable/Packets/PacketHandlers.Chat.cs:45-48` (cuts to `StableReplies.MaxMessageLength` = 450); `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:65` (`IrcText.Limit`); Domain `Message.MaxLength` = 2000 | Each host truncates inbound chat to its own wire limit before the message is sent. | The system's own limit (`Message.Truncate`) already exists; hosts add two more, different cuts. Acceptable only if it is a pure wire limit of the client. | Keep as wire limit and document it, or drop and rely on `Message.Truncate` | low |

## Host.Rest

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| R1 | `Basil.Host.Rest/Routes/UserRoutes.cs:341-342` (`RelationAsync`, used by friend and block routes) | Compares the path id with `context.Session().Record.Id` and answers 403 `notSelf`. | An authority rule ("only the account itself") decided in the host. The related `Away`/`PmPrivate` rule is correctly left to the handler, which returns `Denied`. | The `AddFriend`/`BlockUser` handlers via the scoped user (return `Denied`) | high |
| R2 | `Basil.Host.Rest/Sse/SpectateRoutes.cs:42-44,59` | Reads `registry.Of(record).Stable`, refuses with 409 `targetOffline` when the player has no open Stable client, and sends `StopSpectating` itself when the stream did not start. | A state rule and a compensation step in the host, using `SessionRegistry` directly. | `SpectateUser` returning a result such as `TargetOffline`; the stream start/stop pairing in Application | high |
| R3 | `Basil.Host.Rest/Routes/RoomRoutes.cs:94-113` | The room live stream polls the live `Room`, serialises the view, compares text with the last text, and emits `closed` when `room.IsClosed`. | Change detection on runtime state in the host, where the system already publishes room events. | Room events fed to the SSE hub (as the spectate stream already does) | medium |
| R4 | `Basil.Host.Rest/Routes/MenuRoutes.cs:18` (`MaxImageBytes = 10 MiB`) and `:155-156` | The 10 MB upload limit is a host constant while the avatar and osz limits come from settings. The created banner is returned with `Current: true` whatever its `begins` time is. | A limit decided in the host; a derived value asserted instead of computed (a banner that begins later is reported as current). | Settings (`Uploads.MenuImageMaxSize`) and `MenuBanner.IsCurrent(now)` | medium |
| R5 | `Basil.Host.Rest/Routes/RoomRoutes.cs:69`; `Basil.Host.Rest/Routes/ChannelRoutes.cs:36` | Routes read `Lobby` and `GeneralChannelRegistry` directly (`lobby.Find`, `registry.VisibleTo`, `OrderBy`). | The channel visibility rule is in the registry (right), but the host should ask it through a message. | `GetRoom`/`ListChannels` queries | low |
| R6 | `Basil.Host.Rest/Routes/ContentRoutes.cs:129`; `Basil.Host.Rest/Routes/MenuRoutes.cs:170-178,203-210` (delete banner/seasonal by file name); `Basil.Host.Rest/Routes/BeatmapRoutes.cs:225-233` (redirect to `detail.Beatmaps[0]`, "the lowest") | The host lists items and picks the one whose file name matches, or picks the first beatmap and calls it the lowest. | Lookup by name and the meaning of "lowest" belong to the module. | Messages taking the name, or a `GetLowestBeatmap` query | low |
| R7 | `Basil.Host.Rest/Images/ImageProviders.cs:39` | When a user has no avatar the host serves an embedded default picture. | A "no avatar means default avatar" rule in the host. | `OpenUserAvatar` answering the default | low |

## Host.Stable

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| S1 | `Basil.Host.Stable/Support/StableView.cs:28-31` (`Roles`), used at `Basil.Host.Stable/Login/StableLogin.cs:110,131,143-147` | A user with no effective privilege is given `Player` for the wire, and that same changed value then decides which channels the user may read (`roles.Allows(read)` and `Visible`). | A privilege derived in the host, and a channel read rule (`ReadableBy`) re-implemented there. AGENTS bans a derived privilege. | Application: the channel list the user may see comes from `GeneralChannelRegistry.VisibleTo`; the wire mapping stays a pure cast | high |
| S2 | `Basil.Host.Stable/Web/OsuWebRoutes.cs:330,379,392` | `if (!player.MayUseDirect()) return 403` in search, search-set and download. | The host checks a privilege and chooses to refuse. | The handlers return `Denied`; the host maps it to 403 | high |
| S3 | `Basil.Host.Stable/Web/OsuWebRoutes.cs:464-504` | Registration calls `AccountRegistration.CheckAsync` directly (not a message), then rate-limits (`RateRule.Registration`), then sends `RegisterAccount`. `check=1` only checks. | A business check and its ordering in the host; the call bypasses Mediator. | `RegisterAccount` with a check-only flag; limit in Gateway | high |
| S4 | `Basil.Host.Stable/Packets/PacketHandlers.Multiplayer.cs:155-160` (`ForwardScore`); `Basil.Host.Stable/Packets/PacketHandlers.Spectating.cs:30-41` (`ForwardFramesAsync`) | The host decides whether a score or frame is relayed (`room.SlotOf`, `Rounds.InProgress`, `Spectators.Count`), picks the recipients, and sends packets itself, without a message. | State rule and fan-out decided in the host; Application never sees the relay (except the API copy). | Messages `RelayScore`/`RelaySpectatorFrames` and events carrying the audience | high |
| S5 | `Basil.Host.Stable/Web/WebAuthentication.cs:36-38` (open client by IP), `:44-47` (scan `Online` by normalised name, `UserData.TryNormalize`), `:57,61` (`_verified` digest cache) | The web endpoints authenticate in the host: find the online user, remember a passed digest per connection, and treat "an open client at this address" as proof (`HasOpenClientAt`, `OsuWebRoutes.cs:205,282`). | Authentication state and an address-based access rule in the host; the Domain rule `TryNormalize` is called from it. | A `VerifyWebCredentials` message answered by Sessions | high |
| S6 | `Basil.Host.Stable/Packets/PacketHandlers.Multiplayer.cs:165,173` (`actor is not TourneySession`) | A packet is ignored unless the session is a tourney session. | Authority decided by connection kind (rejected in `AGENTS.md`); `ScopedRoomMembers` already checks it. | Application only | medium |
| S7 | `Basil.Host.Stable/Packets/PacketHandlers.Multiplayer.cs:67-75,115-135` | Create match is two commands (`OpenRoom`, then `ConfigureRoom` with the beatmap). Change settings reads the current room settings and sends only the fields that differ. | Sequence and state diffing in the host. | One message with the full settings; the room diffs | medium |
| S8 | `Basil.Host.Stable/Web/OsuWebRoutes.cs:232,320` | The host calls `IAnticheatReporter.Report` itself, and answers `-3` from the returned flags. | Reporting is an Anticheat decision; the host should hand the flags over in `SubmitScore`/a message. | Application message | medium |
| S9 | `Basil.Host.Stable/Routes/StableRoutes.cs:85-92` | Resolves the token with `registry.Find`, checks `IsOpen` and `Type`, sends `MarkSessionActive`, and re-checks `IsOpen` to detect an expired session. | Session validity is decided by the host. | `ResumeSession` (as REST does) returning the result | medium |
| S10 | `Basil.Host.Stable/Web/OsuWebRoutes.cs:260-262` (`ParseGrade` falls back to `Grade.N`), `:250` (`?? GameMode.Standard`) | Unknown wire values are replaced by defaults the system then treats as real. | Silent defaults hide a bad submission. Should refuse. | `WireMap.Known` returning a refusal | low |
| S11 | `Basil.Host.Stable/Support/StableState.cs:8,19-67` | `AcceptingLogins` is host-held state, and `StableShutdown` picks the open clients and waits for their polls. IRC has its own copy (`IrcState`). | The system's "accepting logins" state lives in two hosts. | A server state in Application, read by every host | low |
| S12 | `Basil.Host.Stable/Support/Outbox.cs:17-58` | When a queue passes 4 MiB or 10 000 packets, the host ends the session with `ConnectionLost`. | A transport back-pressure policy that ends a session. Acceptable if kept as transport; note it is a decision. | Keep in host, document | low |
| S13 | `Basil.Host.Stable/Packets/PacketHandlers.cs:37-39` | Caps of 256 ids and 100 file names per request. | A resource limit chosen in the host. | Gateway size limit | low |

## Host.Irc

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| I1 | `Basil.Host.Irc/Support/IrcView.cs:25-37` (`Prefix`) | Computes `@` and `+` from the member's effective privileges: `Allows(Moderator)`, `WritePrivileges`, `Room.IsManagedBy`, `Room.Involves`. Used by NAMES, WHO, WHOIS. | A privilege and authority rule in the host. | A member-role view returned by Application (the host maps role to symbol) | high |
| I2 | `Basil.Host.Irc/Commands/IrcCommands.Queries.cs:13-22` (`VisibleChannels`) and the callers `Names`, `List`, `Who`, `Whois` (`:24-120`) | Builds the list of channels the user may see from `EffectivePrivileges`, `general.VisibleTo`, `lobby.Rooms` and `room.Involves`; reads registries directly. | A visibility rule decided in the host, outside Mediator. | A `ListVisibleChannels` query | high |
| I3 | `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:39,121,140` | Any `#spec_` name is refused as "no such channel" or "cannot join" before the system is asked. | The host decides that spectator channels are out of reach. | `ChatTarget` result from Application | medium |
| I4 | `Basil.Host.Irc/Commands/IrcCommands.Messaging.cs:214,233,258` | KICK, INVITE, TOPIC (set) and MODE always answer `ERR_CHANOPRIVSNEEDED`; `Topic` answers "not on channel" from `channel.Members`. | The refusal is made without asking the system, so a manager is refused too. | Messages with a `Denied` outcome, or an explicit documented non-feature | medium |
| I5 | `Basil.Host.Irc/Clients/IrcPresence.cs`; `Basil.Host.Irc/Delivery/MembershipDelivery.cs:26,50-55` | The host stores which channels each user is in (`first of user`) to decide whether to show a JOIN, PART or QUIT. | A copy of membership state in the host. | Event fields (`FirstOfUser`) from Application | medium |

## Host.Cli

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| C1 | `Basil.Host.Cli/Commands/UserCommands.cs:151-155` | `--admin` is turned into `UserPrivilegesExtensions.All` and sent as the new account's privileges. | The host picks a privilege set. | A named profile in Application, or leave to the caller | medium |
| C2 | `Basil.Host.Cli/Dashboard/Data/DashboardSampler.cs:114,129,142`; `Dashboard/Tabs/ChatTab.cs:522-538`; `Dashboard/Tabs/UsersTab.cs`, `SessionsSource.cs`, `LobbyTab.cs` | The dashboard reads `SessionRegistry`, `Lobby` and `GeneralChannelRegistry` directly; the chat box finds a PM recipient by scanning the registry and joins the channel before posting. | Read access around the message layer; the join-then-post sequence is coordination. | Query messages; `PostChatMessage` joining as needed | low |
| C3 | `Basil.Host.Cli/Commands/ContentCommands.cs:476-477` | A screenshot listing is paged with `Skip/Take` in the host. | Paging belongs to the query. | `ListScreenshots(page)` | low |

## Protocol (`Basil.Protocol.Game.Stable`, `Basil.Protocol.Irc`)

No findings. The projects only decode and encode: field counts, number formats, length prefixes, the score cipher, the client-flag count decoding (`ScoreSubmission.cs`, which undoes the client's own encoding), IRC parsing and PROXY header parsing. No privilege, no user rule, no coordination.

## Application

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| A1 | `Basil.Application.Chat/Objects/Channel.cs:89` (`connection is ApiSession` gives `AccessDenied`); `Basil.Application.Chat/Registry/ChannelDirectory.cs:37-48` (`ChatTarget.OwnRoom`, `OwnSpectators`, `StableSession`); `Basil.Application.Multiplayer/Capabilities/ScopedLobby.cs:41,116,124`; `ScopedRoomMembers.cs:20,29,78,85` | Application decides by connection kind and names osu!-only aliases ("my room", "my spectators"). | The system should not know the caller; authority by connection kind is rejected in `AGENTS.md`. These are in the right layer but carry the client's shape. | A privilege or capability flag on `Session`; targets expressed by ids | medium |
| A2 | `Basil.Application.Sessions/Registry/AccountRegistration.cs:20` | A public `CheckAsync` on a Registry class that a host calls directly (see S3); the folder contract is "live objects" and operations on them. | Gives hosts a way around Mediator; logic via a port in a Registry file. | Make it `internal`, expose through a message | low |

Not found in Application: no third-party package other than `Mediator.Abstractions`, DI abstractions and logging abstractions; no `Handlers/`, `Logic/` or `Workers/` folder; no file, HTTP, EF or timer use.

## Infrastructure

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| N1 | `Basil.Infrastructure.Beatmaps/Logic/BeatmapVisibility.cs:9-21`; used in `GetBeatmapHandler.cs:15`, `GetBeatmapsetHandler.cs:13-19`, `GetBeatmapsetsHandler.cs:13`, `OpenBeatmapFileHandler.cs:13`, `OpenBeatmapsetFileHandler.cs:20-27`, `OpenBeatmapsetArchiveHandler.cs:18` | Decides who may view a hidden beatmap or set: `set.Visible || mayViewHidden`, with `mayViewHidden` from the actor's privileges. The privilege test itself is in `BeatmapQuery.MayViewHidden`, but the view rule lives here. | A privilege-based visibility rule in Infrastructure; reads do not go through a scoped capability. | A Domain/Application rule on the beatmapset (`CanBeViewedBy`) reached by a `Scoped` view | high |
| N2 | `Basil.Infrastructure.Chat/Handlers/RoomPlayerJoinedHandler.cs:14`; `RoomPlayerBannedHandler.cs:12`; `RoomRefereeRemovedHandler.cs:13-17` | Handlers choose who leaves a room channel (`!Room.IsManagedBy(...)`, `!Room.Involves(member, effective privileges)`). | Membership rules in a handler; a handler should only call the channel. | A `RoomChannel` operation that reconciles membership | medium |
| N3 | `Basil.Infrastructure.Sessions/Logic/AccountRules.cs:22,35,64-66` | The accounts lock, the default privileges for a new account, the exclusion of `BasilBot` and deleted accounts from login. | Business rules in Infrastructure. (`AGENTS.md` currently sanctions "account checks" in `Logic/`; the new decision narrows that.) | `Domain`/`Application.Sessions` objects, with the repository as a port | medium |
| N4 | `Basil.Infrastructure.Scores/Logic/ScoreRecorder.cs:20,72-95` | Score validation (version, client hash, uninstaller hash, disk signature, beatmap hash) and the `MinReplayLength = 24` rule for storing a replay. | Rules of score acceptance (the hash formula is the client's wire, the rest is business). Sanctioned today as "score validation and recording" in `Logic/`. | Protocol (formula) and Domain (acceptance rules) | medium |
| N5 | `Basil.Infrastructure.Bot/Workers/RoomCountdowns.cs:48-52` | A worker returns `RoomActionResult.RoomClosed`/`InProgress` and throws `InvalidRoomSettingsException` for a length outside `RoomCountdown.Min/Max`. | The rules of a countdown are in a worker; `MpCommands.Rounds.cs:73` repeats the maximum. | `RoomCountdown` (Application.Bot object); the worker runs the timer | medium |
| N6 | `Basil.Infrastructure.Scores/Handlers/GetLeaderboardHandler.cs:13,26`; `SearchScoresHandler.cs:14-35` | The leaderboard length (50) and the "?" name for a missing user are fixed in the handler; search joins three repositories, intersects hashes and decides `NoBeatmap`/`NoUser`. | Query rules and composition in a handler. | An Application query service/port | medium |
| N7 | `Basil.Infrastructure.Multiplayer/Handlers/ScoreHandler.cs:11` | On `ScoreSubmitted` it loops every room and calls `ReceiveScore`. | Routing a score to its room is decided by scanning all rooms. | `Lobby.ReceiveScore(score, player)` | low |

Checked and in role: thin handlers (scope the object and call), workers (`SessionSweeper`, `SessionRetentionSweeper`, `EmptyRoomCloser`, `CalculationWorker`, `ScorePerformanceBackfill`), the EF repositories, file storages, `Basil.Infrastructure.Persistence`, and the Beatmaps `Logic/` (importer, scanner, planner, matcher).

## Composition root (`Basil`)

| ID | File:line | What it does | Why outside the role | Belongs to | Severity |
|---|---|---|---|---|---|
| B1 | `Basil/Configuration/UpdateChecker.cs:97,117-130` | Calls the GitHub API and compares versions, and derives the rulesets state (`Available`/`UpToDate`). | A third-party HTTP service and a rule in the root; `IUpdateStatus` is a contract sitting in `Host.Shared`. | An Infrastructure module with an Application contract | medium |
| B2 | `Basil/Lifetime/GracefulShutdown.cs:98` | The "close the rooms" step calls `lobby.CloseAll()`. | Closing rooms is a system operation; the root should call one service. | `Server.ShutdownAsync` (like `Server.RecoverAsync` at start) | low |
| B3 | `Basil/Lifetime/ShellRunner.cs:9,46-141` | Parses command-line options, asks "Really shut down? (y/N)", and picks services by string type name (`"EventDispatcher"`, `"StorageStartup"`). | Prompt and argument handling are the CLI host's job; type names break the names rule. | `Basil.Host.Cli`; `nameof`/`typeof` | low |

## Clean areas

* `Basil.Gateway`: only Allow/Deny; no user or business type.
* `Basil.Protocol.Game.Stable`, `Basil.Protocol.Irc`: encoding and decoding only.
* `Basil.Domain`: no async, lock, logger, I/O or repository; references only `Basil.Shared`.
* Application projects: no third-party library beyond the Mediator abstractions, DI and logging abstractions; no handler, logic or worker folder; module graph in the csproj files matches `AGENTS.md`.
* Hosts never call `ScopeTo` or `ScopeCredentials` and never reference a repository or a storage.
* `Basil.Host.Shared` `Http/`, `Json/`, `Errors/OutcomeErrors.cs`, `Errors/OutcomeTexts.cs`: wire shape and outcome-to-error mapping, which the brief excludes.
* `Basil.Host.Rest` `Http/Api.cs`, `OutcomeResults.cs`, `AliasRoutes.cs`, `Routes/ScoreRoutes.cs`, `Routes/RulesetRoutes.cs`, `Sse/MatchRoutes.cs` (translation of the report to events).
* `Basil.Host.Stable` chat, user and room delivery handlers apart from the audience rules in X5, and `WireMap`.
* `Basil.Host.Cli` shutdown controller, command parsing, formatting, and the dashboard drawing code.
* `Basil.Infrastructure.*` handlers and workers not listed above, `Basil.Infrastructure.Persistence`, `Basil.Infrastructure.Shared`.
* `Basil` root: `DependencyInjection`, `SettingsGatewayLimits`, `TrustedProxiesMiddleware`, `ShutdownSequence`, logging and configuration setup.

## Not checked

* Test projects and the generated documentation.
* The inside of the Domain objects (checked for I/O and layering only, not whether each rule is complete).
* `Basil.Host.Rest` `Diagnostics/`, `DocsRoutes.cs`, `OpenApiSetup.cs` and `Basil.Host.Cli/Socket` beyond a skim.
