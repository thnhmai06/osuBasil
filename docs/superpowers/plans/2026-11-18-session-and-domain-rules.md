# Session merge and Domain rules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two concepts Connection and Session with one Session (one token, one connection, one row), and move each object's self-only rules from Application into Domain.

**Architecture:** Part A merges `Connection` + `SessionRecord` + `ConnectionRecord` into a Domain `Session` (state and rules: heartbeat, status, end) and an Application `Session` (the live actor). Part B splits every live object the same way: Domain holds the object's own state and the rules that touch only itself; Application keeps the lock-holding operation, the links to other objects, persistence, events, timers and `ScopeTo`.

**Tech Stack:** .NET 10, EF Core (PostgreSQL), Mediator, xUnit.

**Spec:** the two statements of intent confirmed in the session of 2026-11-18, reproduced here.

> **Session.** One concept, Session: one token, one connection, one row in `sessions` holding both the authentication data (token hash, user, type) and the connection data (IP, client, StartedAt, LastActiveAt, end). The `connections` table, `IConnectionRepository`, `ConnectionRecord` and `ListSessionConnections` go. Several clients sharing one API token are one session. A session survives a server restart. One active session per user per type; Tourney skips the check. Heartbeat: osu! and IRC as bancho.py, API = every request and every open SSE stream. Thresholds A / B: osu!, Tourney, IRC = 30 s / 300 s; API = 30 s / 8 h. Silent < A: a new login is refused. A ≤ silent < B: `Replaceable`, a new login ends it at once (`Replaced`). Silent ≥ B: `Expired`, the sweeper ends it. IRC `PASS` is the account password; `OpenIrcSession`, keys and labels go. Thresholds are fixed in Domain, not settings. `MaxPerUser`, `ApiToken.Lifetime`, `ExpiresAt`, `OutlivesConnection`/`EndsToken`, `ReopenWithin` go.
>
> **Domain rules.** Domain holds the rules of one object on its own (state, invariants, transitions that touch only itself), synchronous, no repository, no event publishing, no live object of another module; a seat holds a user identity, not a live session. Application holds capabilities (`Scoped*`), contracts (repositories, ports, messages, events) and the coordination between objects. Every module. One Domain project, split by folder. The live half and the rule half share the type name, told apart by namespace. Outcome enums move with the object that returns them. Out of scope: observable contracts (packets, API, BasilBot replies), moving Metrics / `EventPublisher` / `CappedStream`, splitting Domain into projects, anything that involves two objects.

## Global Constraints

- Thresholds, exact: `ReplaceableAfter = 30 s` for every type; `ExpiresAfter = 300 s` for Stable, StableTourney, Irc; `8 h` for Api. Boundaries: silence `< 30 s` → Active; `≥ 30 s` → Replaceable; `≥ ExpiresAfter` → Expired.
- One active session per user per `SessionType`, except `StableTourney` (no duplicate check, no count limit).
- Domain (`src/Domain/Domain`, project `Basil.Domain`) references no `Basil.Application.*`, no `Basil.Infrastructure.*`, no `Mediator*`, no repository, no `IEventPublisher`. Synchronous only.
- Room invariants stay: one `Lock` per room held across the whole transition, events published inside it, never `await` under it, a closed room answers `RoomClosed`.
- Every outcome enum keeps `Denied = 0`, `Ok = 1`.
- AGENTS.md rules apply: no `.ConfigureAwait(false)`, no reflowed lines, `nameof`/`typeof` instead of type-name strings, internal handlers.
- Owner rule (memory `delegated-agents-test-once`): agents run `dotnet build` only; tests are written in each task but run **once**, in Task 12. At most two agents at once, each in its own worktree.
- New EF migration per module touched; do not edit `InitialCreate`.

## Review Focus

1. **Restart, then old token:** an API client presenting its token after a restart within 8 h keeps working without login; an osu! client presenting its `osu-token` within 300 s keeps working. → test `ResumeAfterRestart_WithinExpiry_Opens` (Task 4).
2. **Restart, then a fresh login while the old session exists only in the database:** the A/B rule must apply against the stored session, not be skipped because RAM is empty. → test `Login_StoredSessionSilentUnderA_Refused` (Task 4).
3. **Two API clients on one token:** concurrent first requests after restart resume one session, not two. → test `Resume_ConcurrentSameToken_OneSession` (Task 4).
4. **Exactly at the boundary:** silence of exactly 30 s is Replaceable, exactly 300 s is Expired. → `StatusAt_*` tests (Task 2).
5. **IRC after restart:** IRC has no token, so a stored open IRC session cannot be resumed; it must be ended at startup (`ServerStopped`), not left open forever. → test `Startup_EndsOpenIrcSessions` (Task 4).

---

## Part A — Session

### Task 1: Make the tree build

**Files:**
- Modify: `Basil.slnx` (remove the 10 `tests/Application/*` entries whose `.csproj` files are deleted in the working tree)

- [ ] **Step 1:** Remove the 10 `<Project Path="tests/Application/…"/>` lines from `Basil.slnx`.
- [ ] **Step 2:** Run `dotnet build Basil.slnx -c Release --nologo -v q`. Expected: `Build succeeded`. If other errors remain from the in-progress Domain restructure, stop and report them to the owner; do not fix them silently.
- [ ] **Step 3:** Commit the owner's in-progress restructure and the slnx fix as one checkpoint commit `chore: checkpoint Domain restructure before session merge` (ask the owner first: it commits their uncommitted work).

### Task 2: Domain `Session`

**Files:**
- Create: `src/Domain/Domain/Authentication/Session.cs` (merges `SessionRecord` + `SessionData`)
- Create: `src/Domain/Domain/Authentication/SessionStatus.cs`
- Create: `src/Domain/Domain/Authentication/SessionRules.cs`
- Delete: `src/Domain/Domain/Authentication/SessionRecord.cs` (keep `SessionEnded`, `SessionEndReason` by moving them into `Session.cs`)
- Test: `tests/Domain/Authentication/SessionTests.cs`

**Interfaces:**
- Produces:
  - `public enum SessionStatus : byte { Active, Replaceable, Expired, Ended }`
  - `public static class SessionRules { extension(SessionType type) { TimeSpan ReplaceableAfter { get; } TimeSpan ExpiresAfter { get; } bool IsExclusive { get; } } }` — values per Global Constraints; `IsExclusive` false only for `StableTourney`.
  - `public sealed class Session : IEquatable<Session>` with `long Id`, `Sha256 TokenHash`, `SessionType Type`, `UserRecord User`, `SessionFingerprint Fingerprint`, `DateTimeOffset StartedAt`, `DateTimeOffset LastActiveAt` (read with `Volatile`), `SessionEnded? Ended`; methods `void Heartbeat(DateTimeOffset at)` (ignored once ended or when `at` is earlier), `SessionStatus StatusAt(DateTimeOffset now)`, `bool End(DateTimeOffset at, SessionEndReason reason)` (first call wins, returns whether it ended it). Equality by `Id`. Validation kept from `SessionData` (osu! session must report its client, `LastActiveAt ≥ StartedAt`).
  - `SessionEndReason` loses nothing; values kept.

- [ ] **Step 1: Write tests** in `SessionTests`:
  - `StatusAt_SilentUnderThirtySeconds_Active`: heartbeat at t, `StatusAt(t + 29.999 s) == Active`.
  - `StatusAt_SilentThirtySeconds_Replaceable`: `StatusAt(t + 30 s) == Replaceable`.
  - `StatusAt_OsuSilentJustUnder300_Replaceable` / `StatusAt_OsuSilent300_Expired`; same pair for `Irc`.
  - `StatusAt_ApiSilentJustUnderEightHours_Replaceable` / `StatusAt_ApiSilentEightHours_Expired`.
  - `StatusAt_Ended_Ended`; `Heartbeat_AfterEnd_Ignored`; `Heartbeat_Earlier_Ignored`; `End_Twice_FirstReasonKept`.
  - `IsExclusive_Tourney_False`, `IsExclusive_Others_True` (theory over the other three types).
- [ ] **Step 2:** Implement the three files.
- [ ] **Step 3:** `dotnet build src/Domain/Domain -c Release --nologo -v q` → succeeds. (Callers of `SessionRecord` break; Task 3 fixes them.)
- [ ] **Step 4:** Commit `feat(domain): Session with heartbeat status rules`.

### Task 3: Persistence — one `sessions` table

**Files:**
- Modify: `src/Infrastructure/Authentication/Persistence/SessionRow.cs` (drop `ExpiresAt`; nothing else needed, the row already holds IP and client)
- Modify: `src/Infrastructure/Authentication/Persistence/EfSessionRepository.cs`, `AuthenticationDbContext.cs`
- Delete: `ConnectionRow.cs`, `EfConnectionRepository.cs`, `src/Application/Authentication/Persistence/IConnectionRepository.cs`, `ConnectionRecord` (wherever defined), the in-memory fake in `tests/Support`
- Modify: `src/Application/Authentication/Persistence/ISessionRepository.cs`
- Create: migration `src/Infrastructure/Persistence/Migrations/Authentication/<timestamp>_MergeConnectionsIntoSessions.cs` (drop `connections`, drop `sessions.ExpiresAt`)
- Test: `tests/Infrastructure/Authentication/Persistence/EfSessionRepositoryTests.cs` (update), delete `EfConnectionRepositoryTests.cs`

**Interfaces:**
- Produces, on `ISessionRepository`:
  - `Task CreateAsync(Session session)`
  - `Task<Session?> FindOpenByTokenAsync(SessionType type, Sha256 tokenHash, CancellationToken ct = default)` (replaces `FindByTokenAsync` + `StoredSession`; returns the session with its `UserRecord`)
  - `Task<Session?> FindOpenAsync(UserRecord user, SessionType type, CancellationToken ct = default)` (the stored open session of that type, for the A/B check after a restart)
  - `Task EndAsync(long id, SessionEnded ended)`; `Task EndAsync(UserRecord user, long? only, long? except, SessionEndReason reason, DateTimeOffset at)` (kept for revoke)
  - `Task<int> EndOpenAsync(SessionType type, SessionEndReason reason, DateTimeOffset at)`
  - `Task<int> EndSilentAsync(DateTimeOffset now)` (ends every open stored session whose `LastActiveAt ≤ now − type.ExpiresAfter`, reason `TimedOut`)
  - `Task WriteActivityAsync(IReadOnlyCollection<SessionActivity> activity)` with `SessionActivity(long Id, DateTimeOffset LastActiveAt)`
  - `ListAsync`, `GetAsync`, `DeleteEndedBeforeAsync` unchanged except `SessionRecord` → `Session`. Rename the query enum `SessionState` → `SessionFilter` (it clashes in meaning with `SessionStatus`).

- [ ] **Step 1: Tests** in `EfSessionRepositoryTests`: `FindOpenByToken_Ended_Null`, `FindOpenAsync_OtherType_Null`, `EndSilent_OsuPast300_Ended`, `EndSilent_ApiUnderEightHours_Open`, `WriteActivity_UpdatesLastActive`.
- [ ] **Step 2:** Implement; generate the migration with `dotnet ef migrations add MergeConnectionsIntoSessions` against `AuthenticationDbContext` (design-time factory in `src/Infrastructure/Persistence/DesignTime`).
- [ ] **Step 3:** Build `src/Infrastructure/Authentication` and `src/Infrastructure/Persistence` → succeed.
- [ ] **Step 4:** Commit `feat(auth): one sessions table, drop connections`.

### Task 4: Application `Session` and `UserRegistry` login rules

**Files:**
- Move/rename: `src/Application/Users/Objects/Connections/` → `src/Application/Users/Objects/Sessions/`; `Connection` → `Session` (abstract, `IActor`), `StableConnection` → `StableSession`, `TourneyConnection` → `TourneySession`, `IrcConnection` → `IrcSession`, `ApiConnection` → `ApiSession`, `OperatorConnection` → `OperatorSession`; `ConnectionContext` → `SessionContext`; `ConnectionOutcome` → `SessionOutcome`; `ConnectionMetrics` → `SessionMetrics` (same counters, renamed); `ConnectionRules` deleted (folded into Domain `SessionRules`; `ReplacesOnJoin` goes, the A/B rule replaces it).
- Modify: `src/Application/Users/Registry/UserRegistry.cs`, `src/Application/Users/Objects/UserSessions.cs`, `User.cs`, `LoginResult.cs`, `Users/Objects/Events/UserEvents.cs` (`UserConnectionOpened/Closed` → `UserSessionOpened/Closed`), `Users/Persistence/LoginFailure.cs` (`TooManySessions` → `AlreadyOnline`)
- Modify: `src/Application/Shared/Settings/ServerSettings.cs` (remove `MaxPerUser`, `MaxSessionsSettings`, `ApiToken`, `ApiTokenSettings`; keep `RetentionPeriod`) and the settings labels/descriptions in `src/Hosts/Shared/Views/Settings*.cs`
- Delete: `src/Application/Users/Messages/OpenIrcSession.cs`, `ListSessionConnections.cs` and their handlers in `src/Infrastructure/Users/Handlers/`
- Modify: `src/Infrastructure/Users/Workers/IdleConnectionSweeper.cs` → `SessionSweeper.cs`, the startup that calls `EndOpenAsync`
- Test: `tests/Infrastructure/Users/Handlers/SessionHandlerTests.cs` (extend), new `tests/Infrastructure/Users/Handlers/SessionLoginTests.cs`, rename `IdleConnectionSweeperTests` → `SessionSweeperTests`

**Interfaces:**
- Consumes: Task 2 `Session` (Domain), `SessionStatus`, `SessionRules`; Task 3 repository.
- Produces:
  - Application `abstract class Session : IActor` exposing `Basil.Domain.Authentication.Session State`, `string Token`, `User User`, `SessionType Type`, `bool IsOpen`, `UserRecord Record`, `bool IsServerOwned`, `UserPrivileges EffectivePrivileges(DateTimeOffset now)`; everything else it had as `Connection` keeps its name.
  - `UserRegistry.LoginAsync(LoginAttempt attempt, SessionType type, IPAddress ip, OsuClient? client, int utcOffset, CancellationToken ct = default)` — now accepts `Irc`.
  - `UserRegistry.ResumeAsync(SessionType type, string token, IPAddress ip, Func<UserRecord, bool>? accepts = null, CancellationToken ct = default)` — replaces `LoginWithTokenAsync`; accepts `Stable`, `StableTourney`, `Api`; finds the open session in RAM, else `FindOpenByTokenAsync`; refuses with `InvalidToken` when not found or when `StatusAt(now) == Expired` (and ends it, `TimedOut`).
  - `UserRegistry.MarkActive(Session session)` → `session.State.Heartbeat(now)`.
  - `UserRegistry.CloseSilent()` (replaces `CloseIdle`): ends in-RAM sessions with `StatusAt(now) == Expired`, reason `TimedOut`.
- Login decision, in `Open` under the account gate, when `type.IsExclusive`: take the in-RAM open session of that type, else `FindOpenAsync(user, type)` read **before** entering the gate; by its `StatusAt(now)`: `Active` → `LoginFailure.AlreadyOnline`; `Replaceable` → end it `Replaced`, then open; `Expired` → end it `TimedOut`, then open. Bot sessions keep their single-session rule.
- Startup: `EndOpenAsync(SessionType.Irc, ServerStopped)` only; Stable, Tourney and Api sessions stay open in the database. The sweeper also calls `EndSilentAsync(now)` for sessions that were never resumed.
- An ended session ends its token: every reason. `OutlivesConnection`, `EndsToken`, `ReopenWithin`, `ExtendExpiry`, `TokenLifetime` are deleted.

- [ ] **Step 1: Tests** in `SessionLoginTests` (use `TestApplication` and advance its fake clock):
  - `Login_ExistingSilentUnderA_Refused` → `LoginFailure.AlreadyOnline`, first session still open.
  - `Login_ExistingSilentA_ReplacesIt` → old session `Ended.Reason == Replaced`, new open.
  - `Login_ExistingExpired_EndsItTimedOut`.
  - `Login_Tourney_SecondSessionOpens` (both stay open).
  - `Login_StoredSessionSilentUnderA_Refused` (session only in the repository, as after restart).
  - `ResumeAfterRestart_WithinExpiry_Opens` (Stable at 299 s, Api at 7 h 59 min).
  - `Resume_PastExpiry_InvalidToken`.
  - `Resume_ConcurrentSameToken_OneSession` (two `ResumeAsync` started together return the same instance).
  - `Startup_EndsOpenIrcSessions`, `Startup_KeepsOpenApiSessions`.
  - `Login_Irc_WithPassword_Opens`.
  - In `SessionSweeperTests`: `Sweep_SilentPastExpiry_Ended`, `Sweep_Replaceable_KeptOpen`.
- [ ] **Step 2:** Do the type renames with Rider (`mcp__mcpm_rider__rename_refactoring`) one type at a time, then the logic changes.
- [ ] **Step 3:** Build `src/Application/Users`, `src/Infrastructure/Users` → succeed.
- [ ] **Step 4:** Commit `feat(users): one Session per connection with A/B login rule`.

### Task 5: Hosts follow the Session

**Files:**
- Modify: `src/Hosts/Stable/Routes/StableRoutes.cs`, `src/Hosts/Stable/Login/StableLogin.cs` (unknown `osu-token` → `ResumeAsync(Stable…)`; a resumed session receives the same login bundle a password login sends; `AlreadyOnline` maps to the wire reply bancho.py sends for "already logged in")
- Modify: `src/Hosts/Rest/Http/Caller.cs`, `src/Hosts/Rest/Sse/SseHub.cs` (`ResumeAsync(Api…)`; SSE keepalive calls `MarkActive`); `src/Hosts/Rest/Routes/UserRoutes.cs` (drop the IRC-key route and the session-connections route; their OpenAPI docs go with them)
- Modify: `src/Hosts/Irc/Commands/IrcCommands.cs`, `src/Hosts/Irc/Session/IrcClients.cs`; rename host class `src/Hosts/Irc/Session/IrcSession.cs` → `IrcSocket.cs` (it clashes with Application `IrcSession`); `PASS` = account password → `LoginAsync(LoginAttempt, SessionType.Irc, …)`; keyed by session, not by token
- Modify: `src/Hosts/Cli/Commands/UserCommands.cs`, `src/Hosts/Cli/Dashboard/Tabs/UserDetails.cs` (drop session-connections views); `src/Hosts/Shared/Errors/OutcomeErrors.cs` and the IRC/BasilBot reply constants for `AlreadyOnline` (wording through the humanizer skill, per memory `client-text-through-humanizer`)
- Test: `tests/Hosts/Irc/…` (login with `PASS <password>`), `tests/Hosts/Stable/…` (unknown token resumes), `tests/Hosts/Rest/…` (token after restart), and update every test that used `Connection` names (mechanical)

- [ ] **Step 1: Tests:** `Irc_PassPassword_Welcomes`, `Irc_PassWrongPassword_464`, `Stable_KnownStoredToken_ResumesWithLoginBundle`, `Rest_StoredToken_Authorized`, `Rest_IrcKeyRoute_Gone` (404).
- [ ] **Step 2:** Implement.
- [ ] **Step 3:** `dotnet build Basil.slnx -c Release --nologo -v q` → succeeds.
- [ ] **Step 4:** Commit `feat(hosts): sessions resume by token, IRC signs in with the password`.

### Task 6: Session documentation

**Files:**
- Modify: `docs/for-agents/identity-privileges.md` (sessions section), `AGENTS.md` (every `Connection` → `Session`), `docs/for-developers/sse.md` if it names `Connection`, `docs/for-technicians/configuration.md` (removed settings), `docs/for-client/*` pages that describe the IRC key.

- [ ] **Step 1:** Rewrite the sessions section: one Session per connection, token, A/B table, restart behaviour, Tourney exception, IRC password. Diagram the login decision.
- [ ] **Step 2:** Commit `docs: one Session per connection`.

## Part B — Domain rules

The split, for every object below: Domain type with the same name in `Basil.Domain.<Module>` holds the state and the synchronous rule methods, returning the outcome (and any data the event needs, as a tuple). The Application type keeps `State` (the Domain object), holds the lock across `State.X(...)` + publish, and keeps every member listed as "stays". A Domain rule never receives a live `Session`; it receives `UserRecord`.

### Task 7: Multiplayer — `Room`

**Files:**
- Create in `src/Domain/Domain/Multiplayer/Room/`: `Room.cs` (Id, Match, Slots, Settings, Authority, Rounds, IsClosed, `Lock.Scope Enter()`), `RoomSlot.cs`, `RoomSlots.cs`, `RoomSettings.cs`, `RoomAuthority.cs` (Creator, Referees, `MaxReferees`, `UserRecord? Host`, `IsManagedBy`), `RoomRounds.cs`, `RoundSettlement.cs` (without the timer), `RoomOutcome.cs`, `RoomSettingsChange.cs`, `SlotArrangement.cs`, `BeatmapReference.cs`; keep `RoomRules.cs`
- Modify: `src/Application/Multiplayer/Objects/Rooms/Room*.cs` (operations call `State`), delete the moved files there
- Test: `tests/Domain/Multiplayer/RoomTests.cs` and siblings per partial

**Domain rule methods** (all `RoomOutcome` or `(RoomOutcome, …)`, all synchronous, players as `UserRecord`): `ChangeSlot`, `MovePlayer`, `ArrangeSlots`, `SetReady`, `SetHasMap`, `ToggleTeam`, `SetTeam`, `SetMods`, `SetLocked`, `LockSlot` (the slot-only part of `ToggleSlotLock`), `Configure` with `AreValid` and the `Apply*` helpers, `AddReferee`, `RemoveReferee`, `Kick`, `Ban`, `Unban`, `IsWrongPassword`, `StartRound`, `AbortRound`, `MarkLoaded`, `SkipIntro`, `ReportFailed`, `ReportCompleted`, `BeginRound`, `EndCurrentRound`, `AdvanceRound`, `ReceiveScore`, `AwaitsScore`, `Decide`.

**Stays in Application `Room`:** `Context`, `Emit`, `ScopeTo`, `EmptySince`, `ClosingWarned`, the map from seated `UserRecord` to live `StableSession`, `Join`, `JoinClaiming`, `Leave`, `Seat`, `TakeSeat`, `ReplaceSeat`, `Vacate`, `SetHost`, `PassHostFrom`, `Invite`, `ObserverJoin`, `ObserverLeave`, `Close`, `MarkOccupied`, `ReportIfEmpty`, `RoomOf`, the score-wait timer (`Fire`, `BeginSettlement`, `SettleRound`, `DecideAfterWait`, `DecideForClosing`), the eviction half of `ToggleSlotLock`. Also stays: `RoomMembers` (observers), `RoomContext`, `IRoomLobbyPort`, `RoomUsers`, `Lobby`, `RoomQuery`, `RoomStatus`, `RoomSource`, Events, `RoomMetrics`.

- [ ] **Step 1: Tests** (Domain, no fakes): one test per rule method's outcomes, deriving expected values from the osu! rules and current behaviour docs, e.g. `ChangeSlot_LockedTarget_SlotNotOpen`, `ChangeSlot_RoundInProgress_InProgress`, `Configure_SizeSeventeen_InvalidSettings`, `Configure_FreemodsOn_RoomKeepsOnlySpeedMods`, `ApplyTeamType_TeamVs_AlternatesRedBlue`, `AddReferee_NinthReferee_Refused`, `Decide_AccuracyWin_HighestAverageTeamWins`, `AnyRule_ClosedRoom_RoomClosed`.
- [ ] **Step 2:** Move, then rewire the Application partials.
- [ ] **Step 3:** Build `src/Application/Multiplayer` and dependants (`Chat`, `Bot`, `Servers`, hosts) → succeed.
- [ ] **Step 4:** Commit `refactor(multiplayer): Room rules into Domain`.

### Task 8: Users — `User` and `Session` rules

**Files:**
- Create: `src/Domain/Domain/Users/User.cs` (state: record, grants, profile flags, silence), `UserGrants.cs` (`Effective`, `Replace`, `MarkDeleted`), `UserProfile.cs` (`SetCountry`, `SetAway`, `SetPmPrivate`), `UserSilence.cs` (`IsSilencedAt`, `EndsAt`), `UserOutcome.cs`, `UserReference.cs`; `src/Domain/Domain/Authentication/PlayerStatus.cs`, `SessionOutcome.cs`
- Modify: `src/Application/Users/Objects/*` — every `*Async`, `Add/Remove(Session)`, spectating, `UserRegistry`, `UserContext`, `LoginResult` stay
- Test: `tests/Domain/Users/UserGrantsTests.cs`, `UserProfileTests.cs`, `UserSilenceTests.cs`

- [ ] **Step 1: Tests:** `Effective_Deleted_None`, `SetAway_Null_ClearsMessage`, `IsSilencedAt_AtEnd_False`.
- [ ] **Step 2:** Move and rewire. **Step 3:** Build → succeed. **Step 4:** Commit `refactor(users): User rules into Domain`.

### Task 9: Chat and Bot rules

**Files:**
- Create: `src/Domain/Domain/Chat/GeneralChannelChange.cs` (with `Validate` and `ApplyTo(GeneralChannelData)`), `ChannelOutcome.cs`, `RoomChannelName.cs` (`NameFor(int)`, `TryParse(string, out int)`); `src/Domain/Domain/Multiplayer/Room/CountdownMarks.cs` (`For(TimeSpan length, bool startsRound)` from `RoomCountdown.MarksFor`)
- Modify: `src/Application/Chat/Objects/GeneralChannel.cs`, `RoomChannel.cs`, `src/Application/Bot/Objects/RoomCountdown.cs`
- Test: `tests/Domain/Chat/GeneralChannelChangeTests.cs`, `RoomChannelNameTests.cs`, `tests/Domain/Multiplayer/CountdownMarksTests.cs` (move the existing countdown cases from the deleted `tests/Application/Bot/Countdowns/RoomCountdownTests.cs` if the owner wants them kept)

- [ ] **Step 1: Tests:** `TryParse_NameFor_RoundTrips`, `TryParse_NotARoomName_False`, `Validate_EmptyName_Throws`, `Marks_ThirtySeconds_IncludesTenAndFive` (values per BasilBot countdown docs).
- [ ] **Step 2:** Move and rewire. **Step 3:** Build. **Step 4:** Commit `refactor(chat,bot): self-only rules into Domain`.

### Task 10: Architecture test for Domain purity

**Files:**
- Modify: `tests/Architecture/LayeringTests.cs`

- [ ] **Step 1: Tests:** `Domain_ReferencesNoApplicationOrInfrastructure` (assembly references of `typeof(Basil.Domain.AssemblyMarker).Assembly` contain no name starting with `Basil.Application`, `Basil.Infrastructure`, `Mediator`); `Domain_HasNoAsyncMethods` (no public or internal method returning `Task`/`ValueTask`); `Domain_UsesNoRepository` (no type whose name ends in `Repository`, `Storage`, `Publisher` is referenced).
- [ ] **Step 2:** Commit `test(architecture): Domain stays pure`.

### Task 11: Domain-rule documentation

**Files:**
- Modify: `AGENTS.md` (Architecture: Domain = one object's rules; Application = capabilities, contracts, coordination; folder table), `docs/for-agents/application-design.md`, `docs/for-agents/module-layout.md`, `docs/for-developers/architecture.md`, `docs/for-developers/multiplayer.md` (Room rule half / live half).

- [ ] **Step 1:** Write the rule with the Implementation Test from AGENTS.md and the Room example table.
- [ ] **Step 2:** Commit `docs: Domain holds one object's rules`.

### Task 12: Final verification (orchestrator only)

- [ ] **Step 1:** `dotnet build Basil.slnx -c Release --nologo -v q` → `Build succeeded`, 0 warnings new.
- [ ] **Step 2:** Run every test project in `Basil.slnx` one at a time: `dotnet test <project> -c Release --no-build --nologo -v q`. Expected: all pass. A red test means the code is wrong: report it, do not edit the test to green.
- [ ] **Step 3:** `tests/Architecture` explicitly, including `CompositionTests` (constructors and `DependencyInjection.cs` changed).
- [ ] **Step 4:** Review the whole diff against AGENTS.md "Final verification" items 7 and 8.
