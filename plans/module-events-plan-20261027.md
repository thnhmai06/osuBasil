# Module and event redesign plan (2026-10-27)

This plan comes from the Infrastructure review of 2026-10-26 and the owner's answers that followed. It covers
every decision taken in that conversation and every finding of the review. Nothing is implemented yet.

## 1. Decisions

### Organization

- **D1. Modules, not techniques.** Application and Infrastructure are no longer split into
  Persistence/Capabilities/Runtime. They are split into **one project per module and per layer**
  (`Basil.Scores.Application`, `Basil.Scores.Infrastructure`, ...). Inside a project, folders may still group by
  technique (`Persistence/`, `Capabilities/`, `Events/`). The existing rules still hold (`internal` types,
  `DependencyInjection.cs`, no SQL text, and so on).
- **D2. Modules talk only through events.** Holding a reference that says "who" (a slot knows its player) is
  allowed. An action on another module is an event. Atomicity across modules is given up on purpose: a room
  seats a player, and Chat then lets the player into the room's channel.
- **D3. An event belongs to the module that publishes it.** A consumer references the publisher, so the module
  graph has to stay acyclic.
- **D4. Application may reference `Mediator.Abstractions`** (martinothamar/Mediator v3).

### Events

- **D5. Publishing and handling live in Application; dispatch lives in Infrastructure.** Application is the
  buyer and the seller; Infrastructure is the shipper. This reverses the AGENTS rule "Application publishes
  events; it does not consume them" and the rejection of "event handlers inside Application".
- **D6. One shared queue for every event, handlers never wait.** An event is a `sealed record`, immutable, with
  its `Timestamp` set when it is made (no `with` copy). The queue keeps the order across event types. A handler
  only works in memory. Persistence hands its copy to `DatabaseWriter` and returns. A round write therefore no
  longer waits for its commit, and `persistence.md` changes accordingly. There is no queue per handler and no
  queue per type.
- **D7. Mediator discovers the handlers; a custom `INotificationPublisher` writes into the shared channel.** One
  reader loop delivers the events. A failing handler is logged and the others still run.
- **D8. Each handler serves one event type**, with no category hierarchy.

### Objects and Scoped

- **D9. Real behaviour lives on the root object; control of behaviour lives on Scoped.** Scoped checks
  permissions (including room authority, read from the current `Authority` snapshot) and carries the actor
  `By`. It holds no state, no lock and no business rule. A denial is returned as `Result.Denied(missing)`, with
  no event and no log of its own.
- **D10. Room (and Lobby, Channel) writes are synchronous methods of the root object.** Each write takes the
  object's own `System.Threading.Lock`, checks the state preconditions, builds new immutable snapshots, and
  publishes, all inside the lock. A publish only enqueues, so it cannot deadlock, and the event order equals the
  order of changes. Nothing is awaited under the lock, so `EnterAsync` and the `SemaphoreSlim` go away. A write
  that takes an actor is `internal` and reached only through Scoped. The server's own work (timers, release on
  disconnect, startup) goes through methods without an actor, and its events carry `By = null`.
- **D11. Reads need no lock.** Slots, authority, settings, members and rounds are published as immutable
  snapshots (records and immutable collections) and replaced whole (copy-on-write). The Domain records `Match`
  and `Round` keep their current mutability.
- **D12. Cross-object invariant.** "A player is in at most one room" belongs to Lobby, through
  `Lobby.TryClaim(player, room)` then `room.Join`, with `Lobby.Release(player)` on failure. Locks are taken
  Lobby first, then Room, and never both at once.
- **D13.** A concurrent authority change is treated as an ordering: the action counts as having happened just
  before the change. This is written down as an invariant in `multiplayer.md`.

### Score and Round

- **D14. Score and Round are two businesses.** The Score module handles and stores every score, multiplayer
  scores included, and knows nothing about Round. It publishes `ScoreSubmitted(player, score)`. The client
  submits solo and multiplayer scores the same way and names no room or round.
- **D15. Round keeps a reference to its scores**: the `round_scores` relation stays in the database as it is
  now. Multiplayer writes it through an event.
- **D16. Matching a score uses the round's own data.** At `BeginRound`, the round snapshots its participants
  with each slot's team (team is a slot property, not a score property). A player who leaves before completing
  is struck off. A player who completed and then left still counts. Multiplayer keeps a table "player → round
  awaiting their score". A score matches on player, beatmap hash, and a round not yet decided; otherwise it is
  ignored.
- **D17. `RoundResult.Decide` runs once, automatically**, when the round has ended and every remaining
  participant has a score. Otherwise it runs **10 s** after the round ended (bancho.py `await_submissions`,
  total rather than per player, on a `TimeProvider` timer), with the scores that came in. Missing players are
  excluded and the bot posts `"{player} didn't submit a score (timeout: 10s)."`. An aborted round is never
  decided.
- **D18. The result is stored when it is decided** (`RoomRoundDecided` → result column on `rounds`), so a later
  rule change leaves old results as they were. Viewing a report is read-only and never decides or writes.
- **D19.** A round keeps its row written when it starts (`EndedAt = null`).
- **D20.** `Lobby.CloseUnfinishedAsync` stores its changes through events, by publishing
  `LobbyMatchRecovered(Match, AbortedRounds)`.

### Persistence and others

- **D21.** Remove the legacy code: the `Songs`/`Beatmaps` move, the `Cache/Beatmaps` and `Cache/Staging`
  cleanup, and the empty-fingerprint handling.
- **D22.** Remove the BasilBot `HasData` seed (a new migration); `EnsureBotAsync` alone creates the bot.
- **D23.** `archive-novideo.osz` drops exactly the video files that the `.osu` files declare.
- **D24.** `MemoryRepository` (no removal) and `RemovableMemoryRepository` (removal, `EraseCommand` abstract).
- **D25.** The bot stays in `Basil.Bot` and reacts through events.

## 2. Target structure

```text
src/
  Domain/                           Basil.Domain                 (stays one project, see A1)
  Common/Application/               Basil.Common.Application     Result, Scoped base, Paging, PageRequest,
                                                                 event base, IEventPublisher
  Common/Infrastructure/            Basil.Common.Infrastructure  Database, DatabaseWriter, DatabaseReader,
                                                                 MemoryRepository, IdentityMap, WeakIndex,
                                                                 OwnedLists, Files, event dispatcher
  Modules/Users/{Application,Infrastructure}       users, credentials, logins, restrictions, relationships,
                                                   moderation log, sessions and connections, spectating,
                                                   anticheat reports
  Modules/Beatmaps/{Application,Infrastructure}    beatmapsets, beatmaps, storage, reader, analyser, assets,
                                                   mirror
  Modules/Scores/{Application,Infrastructure}      score submission and validation, scores, replays, stats
  Modules/Multiplayer/{Application,Infrastructure} lobby, rooms, matches, rounds, round scores, match events,
                                                   report, match recorder
  Modules/Chat/{Application,Infrastructure}        channels, messages; room channels driven by events
  Modules/Content/{Application,Infrastructure}     settings, menu, FAQ, screenshots, avatars
  Migrations/                       Basil.Migrations             the one DbContext's migrations (see A3)
  Bot/                              Basil.Bot
  Hosts/, Protocol/                 unchanged by this plan (see A5)
```

Module references, all pointing down and acyclic:

```text
Users      → Common
Beatmaps   → Common
Content    → Common
Scores     → Users, Beatmaps
Multiplayer→ Users, Beatmaps, Scores
Chat       → Users, Multiplayer
Bot        → every module
```

Cross-module reactions in the target:

| Publisher event | Consumer (module) | Reaction |
|---|---|---|
| `UserConnectionOpened` | Chat | join the auto-join channels |
| `UserConnectionClosed` | Multiplayer, Chat, Users | release the room and lobby watch; part the channels; stop spectating |
| `UserRestricted` | Multiplayer, Users | remove from rooms and referee lists; stop spectating |
| `ScoreSubmitted` | Multiplayer | match the score to a round, record the relation, decide |
| `LobbyRoomOpened` / `LobbyRoomClosed` | Chat | create / close the room channel |
| `RoomPlayerJoined` / `RoomPlayerLeft` | Chat | let the player into / out of the room channel |
| `Room*`, `Lobby*`, `RoomRoundDecided` | Bot | announcements, including the missing-score line |
| `Room*`, `Lobby*`, `RoomRoundDecided`, `LobbyMatchRecovered` | Multiplayer (recorder) | store matches, rounds, round scores, match events |

## 3. Phases

Five phases (A to E), each ending green:
- build the projects that build;
- run every test project that exists;
- review every changed line against AGENTS "Final verification" items 7–8;
- run the container check when constructors or `DependencyInjection.cs` change.

Each phase is one commit, made with the owner's go-ahead. The phases are ordered so that cheap mechanical work
runs on the free agents while the orchestrator does the design work.

### Phase A: fixes and Persistence cleanup on the current layout (cheap, mechanical)

Independent edits on disjoint files. They land before anything moves, so the move carries them along.

1. Regressions and security:
   - `RawSet`/`RawBeatmap`: keep `init;` (already restored in the working tree), and add a one-line comment that
     the serializer needs the setters.
   - `BeatmapQueryFilter` file-name search: use `EF.Functions.ILike` with a new `LikePattern.Literal`, covered by
     hand-written input/output tests and a mixed-case search test.
   - `IdleConnectionSweeperTests`: wait for one tick signal after each clock advance.
   - ImageSharp: raise it to the patched 3.1.x.
2. Persistence decisions:
   - Remove the legacy code (D21).
   - A migration that drops the BasilBot seed, then check that id 1 is never handed out again (D22).
   - `archive-novideo.osz` from the declared videos (D23).
   - `MemoryRepository` / `RemovableMemoryRepository` (D24).
3. Review findings that do not touch the event or room design:
   - Duplication:
     - `IdentityMap` built on `WeakIndex`.
     - `ReadUntilStableAsync`.
     - One shared beatmapset row mapping.
     - The user repo's `ToRow` and an "already named" constant.
     - Setting keys as constants.
     - One backoff `NextDelay`.
     - One file-stream helper.
     - One analyser `Decode`.
     - `BeatmapQueryFilter`'s character list from the Domain.
   - `FileBeatmapsetStorage`: `PackAsync` cleans up its partial file on failure, and `SaveAsync` is split in two.
   - `UserStats.AddPlay` on the model.
   - A contract test that the private-match listing agrees with `CanViewAsync`.
   - Renames with Rider:
     - `CreateConfirmedContext` / `CreateUnconfirmedContext`.
     - `BeatmapPreviewMaker`.
     - Full-word locals.
   - Remove the `OsuBeatmapContent` and `StoredBeatmap` wrappers.
   - Rewrite the `ponytail:` comments as plain comments, and drop the BOM.
   - Check whether channel-name case is a real bug.
   - Run EXPLAIN on `BestPerPlayer`; change it only if it is slow.

**Verify:** the 3 red tests are green; Infrastructure (Persistence 668, Capabilities 69, Runtime 37) and
Architecture stay green; the sweeper test passes 20 runs in a row.

### Reorder (2026-10-27, after Phase A)

The namespace matrix of `src/Application` (imports between feature folders) shows cycles, so the module projects
cannot be cut first:

- Multiplayer ↔ Sessions (Multiplayer imports Sessions 23 times, Sessions imports Multiplayer once).
- Sessions ↔ Chat, and Users ↔ Sessions.
- Sessions → Scores → Multiplayer → Sessions.
- Servers imports Chat, Content, Multiplayer, Sessions and Users.

Project references cannot form a cycle, so the cycles are removed first, inside the current projects, treating
the feature folders as the future modules. The project split comes last, once an architecture test on namespaces
is green; at that point it is a mechanical move. The order is now B (events), C (rooms), D (Score/Round and Chat),
E (module projects).

Snapshot of the tree at every phase boundary, as a tarball outside the repository
(`~/.local/share/basil-snapshots/`), so the owner can review each phase as its own diff. Phase A:
`phase-a-20261027.tar.gz`.

### Phase B: the event system (D5–D8), in the current projects

- Spike first: does Mediator's source generator find handlers in other assemblies from the project that calls
  `AddMediator`?
- Then the `Event` base with the timestamp set at construction; one shared channel behind a custom
  `INotificationPublisher` and one reader loop; the handlers moved to Application, one per event type; the Bot
  handlers stay in Bot; no handler waits, so the recorder queues and `persistence.md` changes; `EventBus<T>` and
  `HandlerStep` go away.
- Tests: a failing handler does not stop the others; the order across types holds; events published before start
  are delivered once the loop starts.

### Phase C: Room, Lobby and Channel as root objects (D9–D13)

- Immutable snapshots, synchronous writes under the object's own `Lock`, publishing inside the lock; `EnterAsync`
  goes away; Scoped is reduced to a permission check plus `By`; `Lobby.TryClaim`/`Release`.
- Tests: the existing multiplayer contract tests stay green; concurrent starts give one round; join and close
  racing leave a consistent room; every `Require` keeps a test that goes red without it.

### Phase C inventory (2026-10-27, before any change)

What the code already is: `Lobby` and `Channel` already guard their own state with a `System.Threading.Lock`
(short, never across an await); only `Room` still uses a `SemaphoreSlim` gate through `EnterAsync` (about 40 call
sites: every `ScopedRoom*` method, `Lobby.ReleaseAsync/RemoveRefereeAsync/WarnIfStillEmpty/CloseIfStillEmpty`,
`Room.RecordScoreAsync`, the countdown timers). The advisor's check of "awaits inside the scope": none. Every
await sits before the scope (`Settings.GetAsync`, `Matches.CreateAsync`, `Users.BlocksAsync`,
`other.ScopeTo(player).Members.LeaveAsync` in `SeatAsync`) or in start-up code (`CloseUnfinishedAsync`). The room
lock calls into `Lobby` (lock-free `RoomOf`, a concurrent dictionary `Remove`) and `Channel` (its own `Lock`), never
the reverse, so the order Room -> Lobby/Channel is acyclic and D12's "Lobby first, then Room" only matters for
`TryClaim`.

Remaining work, in order:
1. C1 root methods: the state-changing part of every `ScopedRoom*` method moves to a synchronous internal method of
   `Room` that takes the actor, locks `Room._sync`, and returns `RoomClosed` for a closed room; `ScopedRoom*` keeps
   the permission check (and the awaits before the lock) and delegates. Public `Task<Result<RoomOutcome>>` shapes
   stay (141 call sites in src and tests), so no caller changes. `Gate`, `EnterAsync` and `Scope` are deleted.
   Order of slices: Members, Slots, Rounds, Settings, Authority, `ScopedRoom.CloseAsync`, then the Lobby and timer
   callers (`ReleaseAsync`, `RemoveRefereeAsync`, `Warn/CloseIfStillEmpty`, `RecordScoreAsync`, countdown timers).
2. C2 `Lobby.TryClaim(player, room)` / `Release(player)` for "a player is in at most one room" (today two joins
   to two rooms by one player can both pass the `RoomOf` check, because each holds only its own room lock).
3. C3 reads: slots, authority and members are already read without the lock (concurrent sets, a fixed slot array).
   Decide after C1 whether copy-on-write snapshots (D11) are still worth their cost for `RoomSlot`.
4. C4 the round write without waiting (`PostgresRoundRepository`) once the room serves its rounds from memory.

### Phase D: Score/Round and Chat decoupled (D14–D20), then the architecture test

- Score/Round as in D14–D20: scores drop round and team and publish `ScoreSubmitted`; Multiplayer keeps the
  participants snapshot, the awaiting table, matching, the 10 s timer and `RoomRoundDecided`; the result column
  (the migration edits `InitialCreate` in place, as D22 did, while no database holds the schema); the read-only
  report in Multiplayer; `LobbyMatchRecovered`; the bot's missing-score line. Also `UserStats.AddPlay` on the
  model and the contract test that the private-match listing agrees with `CanViewAsync`.
- Chat: Room drops its channel; Chat reacts to the room and lobby events.
- Break the other cycles of the matrix the same way: Users ↔ Sessions, Sessions ↔ Chat, Servers.
- An architecture test on namespaces for the target module graph, with a positive control. While it is red it
  lists the edges that remain.
- Docs and rules (D5, D9 and the rest): AGENTS, `application-design.md`, `persistence.md`, `multiplayer.md`,
  `architecture.md`.
- Tests (integration, because Score and Round no longer know each other): a score before `MatchComplete`, after
  it, and after the next round began; a leaver struck off; a completer who left still counts; the timeout with
  the bot line; abort gives no result; viewing a report writes nothing; a stored result survives a changed
  `Decide`.

### Phase E: module projects (a move, no behaviour change)

- Create the projects and solution folders of section 2 and move the types with Rider `move_type_to_namespace`;
  one `BasilDbContext` in Common.Infrastructure and the migrations in `Basil.Migrations` (A3); mirror the test
  tree; the project-level architecture tests with a positive control.
- Verify: the same test counts as at the end of Phase D, and the container check passes.

## 4. Assumptions and open points

- **A1.** Domain stays one project shared by every module (`ScoreRecord`, `UserRecord`, `Round` are used
  across modules). This was not confirmed.
- **A2 (answered 2026-10-27).** Servers, Sessions, Anticheat and Auth are each their own module, not part of Users.
  Their edges to the other modules are still debt in `ModuleGraphTests` and have to be cut one by one; the target
  edges of the new modules are not decided yet. ImageSharp stays on 3.1.12 (owner: ok). D11 stays unimplemented (owner:
  no opinion).
- **A3.** One database and one `BasilDbContext` with the configurations of every module, and the migrations in
  `Basil.Migrations`. The alternative is one DbContext per module, which brings several migration histories and
  cross-module foreign keys.
- **A4.** If Mediator's generator cannot discover handlers across assemblies from Common.Infrastructure,
  `AddMediator` runs in the Host, which references everything, and the custom publisher stays in
  Common.Infrastructure.
- **A5.** The Hosts are still outside the build (Host migration pending). Phases 1–5 change the API they will
  call. Their migration is not part of this plan.
- **A6.** Remove `Round.Result` once nothing sets it, because the report carries the stored result instead.
  Confirm.
- **A7.** The 10 s rule decides normal rounds as well as tournament rounds. bancho.py applies it to scrims only.

## 5. Way of working

- **Workers.** The OpenCode server runs at `127.0.0.1:4096` (`--server http://opencode:opencode@127.0.0.1:4096`).
  As checked on 2026-10-27, `opencode/deepseek-v4.1-flash-free` works in this repository. The Go-plan models
  (`opencode-go/*`) fail with "Go requires a credential", so only the free models are used until the Go
  credential is set on the server.
- **Phase A and Phase B: DeepSeek free agents.**
  - Each agent gets an explicit, self-contained prompt: files, target shape, verification command, and the Rider
    MCP for moves and renames.
  - At most two agents run at a time, on disjoint files, each in its own worktree.
  - Escalate when the free model fails after one clearer retry: first to a Claude Sonnet or Haiku subagent, then
    to the orchestrator.
- **Phase C: the orchestrator** designs it and writes the core types. DeepSeek agents take the bulk edits:
  turning each `ScopedRoom*` method into a permission check plus a call, splitting handlers per type, and
  writing tests from the cases listed above.
- **Review.** The orchestrator reviews every line, runs the builds and tests, and commits.
- **Expected values.** Test expectations come from the decisions above, bancho.py and the osu! protocol, never
  from running the code.

## 6. Progress

Updated 2026-10-27 (Phase A finished). Nothing is committed: the working tree still holds the earlier uncommitted
IDE sweep, so a commit would need the owner to choose what goes in. OpenCode workers share the main checkout (no
worktrees, they would not see that sweep), work on disjoint files, and one build of a project at a time.

Test database: a private PostgreSQL cluster at `~/.local/share/basil-test-postgres`, port 55432, user `postgres`,
no password. Start it with
`pg_ctl -D ~/.local/share/basil-test-postgres -o "-p 55432 -k /tmp -c listen_addresses=127.0.0.1 -c fsync=off" start`.
Run Persistence tests with `BASIL_TEST_POSTGRES="Host=127.0.0.1;Port=55432;Username=postgres;Database=postgres"`.
Without that variable the fixture starts a Docker container, as before.

Gate after Phase A (all green): Domain 327, Bot 945, Architecture 22, Protocol.Bancho 507, Protocol.Irc 117,
Application.Runtime 681, Infrastructure.Capabilities 69, Infrastructure.Runtime 37, Infrastructure.Persistence
673 (+4 skipped).

Phase A done:
- Regressions: `RawSet`/`RawBeatmap` keep `init;` with a comment; `LikePattern.Literal` and the file-name search
  through `ILike` with new tests; the sweeper test no longer races (8 runs green).
- D21 legacy removal (the `Songs`/`Beatmaps` move, the `Cache/Beatmaps`/`Cache/Staging` cleanup, the empty
  fingerprint). Two `DataPathsTests` cases and one docs sentence removed with it. The Application planner may still
  treat a default fingerprint specially; it was not looked at.
- D22: the BasilBot seed is gone from `UserRowConfiguration` and from `InitialCreate` (the only migration, regenerated
  in place under its old id), and the `users.id` sequence starts at 2 so BasilBot, stored under id 1 at start-up,
  never collides with the first account. Five listing tests that counted the seeded bot were updated.
- D23: `archive-novideo.osz` leaves out exactly the files the difficulties declare as video
  (`SaveArchiveWithoutVideoAsync(set, videoFiles, ...)`), with storage tests and an Application-level test; the test
  fake `BeatmapsetWorld.Osz` can now declare a video.
- D24: `MemoryRepository` plus `RemovableMemoryRepository`. The dead `EraseCommand` of the beatmap repository is gone.
- Duplication and clean-up (DeepSeek workers, reviewed line by line): `WeakDictionary` base of `IdentityMap` and
  `WeakIndex`; `ReadUntilStableAsync`; `BeatmapsetRows.ToRecord`; `StoredBeatmap` wrapper removed; analyser
  `Decode`; `OsuBeatmapContent` wrapper removed; `BeatmapAssetStore` -> `BeatmapPreviewMaker`; buffer-size
  constants; user repository `ToRow`/`AlreadyNamed`; setting-name constants; `NextRetryDelay`; `PackAsync` cleans
  its partial file; `FileBeatmapsetStorage.SaveAsync` split; `ponytail:` comments and the BOM gone;
  `CreateSynchronousContext`/`CreateAsynchronousContext` -> `CreateConfirmedContext`/`CreateUnconfirmedContext`.
- Channel-name case: checked, not a bug. `ScopedServerChannels.CreateAsync` refuses a name that differs only in
  case (`OpenChannel` returns `NameTaken`) before the repository sees it.

Phase A left over (moved to Phase C because they sit on code Phase C rewrites): `UserStats.AddPlay` on the model;
the contract test that the private-match listing agrees with `CanViewAsync`. Not done: EXPLAIN of `BestPerPlayer`
(no sign it is slow; do it with real volume or leave it).

ImageSharp: **no change, needs the owner's decision.** The last 3.x (3.2.0) still carries the same 5 advisories
(NuGet data). The first clean version is 4.1.2+ (checked 4.1.3: builds and the 69 Capabilities tests pass), but
ImageSharp 4 needs a commercial Six Labors licence for Release builds ("Release builds require a valid license";
Debug only warns). ImageSharp.Web 4.0.2 matches 4.1.3. Options: buy or qualify for a licence and move to 4.x,
replace ImageSharp (for example with SkiaSharp or MetadataExtractor for header identification), or accept the risk.
The only use is `Image.IdentifyAsync` in `ImageSharpScreenshotInspector`.

### Phase B (events) - done 2026-10-27

- Mediator 3.0.2 (source generated, singleton, internal types) in Application.Runtime. `Event : INotification`;
  `EventPublisher` (one class, `IEventPublisher<T>` for every category) stamps `Timestamp` and calls
  `IPublisher.Publish` through a `Lazy<IPublisher>` (breaks the Mediator -> handler -> object -> publisher cycle).
  `QueuedNotificationPublisher` turns the call into one `IEventDelivery` on the `EventQueue`; `Publish` runs no handler.
- Infrastructure.Runtime is only the shipper: `EventDispatcher` (a hosted service that only runs the Application-owned `EventQueue`; the queue itself, one unbounded channel,
  one reader, deliveries in publish order, a failing handler is logged and does not stop the rest). `EventBus<T>` and
  `IEventHandler<T>` are gone.
- Handlers are per-event-type `INotificationHandler<T>` in the module that owns the reaction: `ConnectionHandler`,
  `RestrictionHandler`, `ScoreHandler`, `MatchRecorder` moved to Application.Runtime (internal); `RoomAnnouncer`,
  `AnticheatAnnouncer`, `ChatHandler` stay in Basil.Bot and are registered by hand.
- Tests: the four handler tests moved to `tests/Application/Runtime`; `EventPublisherTests` (stamp, nothing runs
  before `Publish` returns, order, a failing handler, an unhandled event) and `EventQueueTests` (order, returns
  before the run, a failing delivery, stop with deliveries queued) replace `EventBusTests`.
  Mediator only knows event types declared in the Application.Runtime assembly, so a publisher test must use a real
  event type; a type declared in a test assembly throws `MissingMessageHandlerException`.
- Container check (throwaway console on `TestApplication` + `AddInfrastructureRuntime()` + `AddBot()`): 195
  registrations resolve, no cycle, no hang.
- Gate: Domain 327, Bot 944 (one test gone with the untyped `OtherAnticheatEvent` case), Architecture 22,
  Protocol.Bancho 507, Protocol.Irc 117, Application.Runtime 701, Infrastructure.Capabilities 69,
  Infrastructure.Runtime 22 (15 moved), Infrastructure.Persistence 673 (+4 Windows-only skips).

Deferred out of Phase B: making the round write non-waiting. `PostgresRoundRepository.ListAsync` reads the database
directly, so a write that is not awaited can be missed by a read right after it. It belongs to Phase C, when the
round is a live object of its room and the read is served from memory. `persistence.md` changes with it.
The docs that still describe `EventBus<T>`/`IEventHandler<T>` (`architecture.md` lines ~153 and ~238,
`application-design.md` ~201, `AGENTS.md` feature table) are rewritten in Phase D with the rest of the docs.

### Phase C1 (root transitions) - done 2026-10-27

- Every state change of a room is a synchronous internal method of `Room` that locks `Room._sync`, returns
  `RoomClosed` for a closed room and publishes inside the lock: `Room.Members.cs` (Join, SeatPlayer, Leave, Kick, Ban,
  Unban, Invite, ObserverJoin/Leave, Close), `Room.SlotOps.cs`, `Room.RoundOps.cs` (also the countdown timers, now
  synchronous), `Room.SettingsOps.cs` (Configure, SetLocked, with the freemod and team helpers),
  `Room.AuthorityOps.cs`. `ScopedRoom*` are shells: the permission check, the awaits that must precede the lock,
  and one call (`Task.FromResult` keeps the public `Task<Result<RoomOutcome>>` shapes, 141 callers untouched).
- `Gate`, `EnterAsync` and the `Scope` class are deleted; `Room.Enter()` hands the same lock to `Lobby`'s empty-room
  timers. `IsClosed` is volatile. The lobby's referee removal calls `Room.RemoveReferee`; `Lobby.ReleaseAsync` calls
  `Room.Leave`/`ObserverLeave`.
- Gate: Application.Runtime 701, Bot 944, Architecture 23 (one new routing test), Infrastructure.Runtime 22.
- Left for C2: `Lobby.TryClaim` for "one room per player" (the join check `RoomOf(by)` still runs under the room's own
  lock only); the checks in `Join`/`SeatPlayer`/`TakeSeat` hold the room lock while reading `Lobby.RoomOf`.

### Phase C2-C4 - done 2026-10-27

- C2: `Lobby.TryClaim(player, room)` / `Release(player, room)` (on `IRoomLobbyPort`): a join or a manager's seat claims the
  player for the length of the seating, so two rooms cannot both seat one player; the room's own `RoomOf` check
  keeps the old order of refusals. It is a deviation from D12's literal "claim, then join, release on failure": the
  claim is only the in-flight marker, a seated player is found by `RoomOf`, so no claim has to be released at the
  many places a slot is vacated. Room creation claims the creator too. Tests with real parallelism
  (`Task.Run`, 200 rounds) for one player racing for two rooms, two starts racing, two joins for the last slot;
  mutation check: with `TryClaim` always true the race test fails every run.
- C3: reads stay lock-free over concurrent sets and a fixed slot array (as before). Copy-on-write snapshots (D11)
  for `RoomSlot` were **not** done: a reader can see a slot between two of its field writes (status and mods), which
  no contract test or protocol packet depends on; revisit if a packet ever needs a consistent slot. Owner decision
  wanted if D11 must be literal.
- C4: `PostgresRoundRepository.CreateOrUpdateAsync` queues and returns (`_ = SaveAsync`); `ListAsync` adds the live
  rounds whose first write is still queued. `persistence.md` and `IRoundRepository` say so. Three tests that pinned
  "stored when it returns" now wait for the row (`WaitForAsync`); a new test pins "listed at once".
- Docs: AGENTS.md, `architecture.md`, `multiplayer.md` describe the lock model; the event docs were aligned in B.
- Gate: Domain 327, Bot 944, Architecture 23, Protocol.Bancho 507, Protocol.Irc 117, Application.Runtime 703,
  Infrastructure.Capabilities 69, Infrastructure.Runtime 22, Infrastructure.Persistence 674 (+4 skips); Release
  builds clean.

Phase C follow-ups after the advisor's review:
- The lobby's empty-room timers now run through `Room.Fire`, as the countdown timers do: a fault in a thread-pool timer
  callback would otherwise end the process.
- Mutation sweep of every `Require` in `ScopedRoom*.cs` (23 sites, run on a copy of the tree): 22 killed; the survivor
  was `StartCountdownAsync`'s combined check, now pinned by `StartCountdownAsync_ByAPlainPlayer_IsRefusedAndStartsNothing`
  (killed on a second run). The `ScopedLobby` `Require`s are older code and were not part of this phase.
- `SeatAsync` is a shell again: the checks that decide whether a player can be moved from another room are
  `Room.MakeRoomFor`. Known limit: leaving the other room and seating here are two transitions, because no two room
  locks are held at once; a failure between them leaves the player in no room.
- A join racing a close (100 rounds) leaves nobody seated in the closed room, `RoomOf` null, and the claim released.
- The mediator and its handlers are built on the first publish of each event type, on the publisher's thread, which can
  be inside a room lock. The handlers' constructors only store references (checked), and the singletons they pull in
  take no lock in their constructors, so this cannot deadlock; no warm-up was added.
- **Owner decision not carried out: D11 (copy-on-write snapshots of slots, authority, settings, members).** Reads are
  lock-free over concurrent sets and a fixed slot array, so a reader can see a slot between two writes of one
  transition. Whether a Bancho packet can depend on that cannot be checked while the hosts are outside the build.
  Needs the owner's call before the hosts are migrated.

### Phase D1 (Score/Round, D14-D20) - done 2026-10-27

- Scores no longer know rounds: `ScoreRecorder` has no round, team or `IRoundScoreRepository`; `ScoreSubmitted(Player,
  Score, Stats)`; the connection still reads its player's room only to accept a score on an unknown beatmap that is the
  round's.
- `Room.Scoring.cs` + `RoundSettlement`: participants snapshot (with the slot's team) at `BeginRound`; `ReceiveScore`
  matches on player, beatmap, not decided, no earlier score; a leaver before completing is struck off, a completer who
  left is still awaited; the round is decided when it has ended and nobody awaited is missing, else `ScoreWait` (10 s,
  total) after the end on a timer through `Room.Fire`; an aborted round is never decided; a room that closes while its
  round waits decides it first. `ScoreHandler` offers each score to every open room.
- Events: `RoomRoundScoreSubmitted` (a handler stores the relation), `RoomRoundDecided(Room, Round, Result?, Missing)`
  (a handler stores the round; the bot says `announce.scoreMissing` per missing player), `LobbyMatchRecovered`
  (`CloseUnfinishedAsync` mutates and publishes; `MatchRecorder` stores; `LobbyContext` lost its `MatchEvents`).
- Stored decision: `RoundVerdict` (Domain) = the ranking, winner, margin, team scores; `Round.Decision`, `RoundResult.Verdict`,
  `RoundResult.Restore`; `rounds.result` jsonb (the single `InitialCreate` migration regenerated in place, as in D22;
  only the new column differs). The report restores from the stored decision and writes nothing.
- `PostgresRoundScoreRepository.RecordAsync` queues and returns, and `ListAsync` adds the relations still queued.
- Tests: `RoomScoringTests` (15, mutation-checked: aborted-close, strike-off, completer-struck), domain
  `Restore`/`Verdict` tests, report tests (stored decision shown even when the rules would differ now; an undecided
  round has no result and the report decides nothing), persistence tests for the decision and pending relations.
- Gate: Domain 330, Bot 950, Architecture 23, Application.Runtime 718, Infrastructure.Persistence 678, Infrastructure.Runtime 22,
  others unchanged; Release builds clean.
- Remaining for D: chat decoupling (Room drops its channel), the other cycles of the matrix, the namespace
  architecture test, docs (`application-design.md`, `persistence.md`, `architecture.md` event/Score sections).

Phase D1 follow-ups after the advisor's review:
- A round is decided before the next one begins (`BeginSettlement` calls `DecideForClosing`), so a quick restart no
  longer loses a decision; `RoomRoundDecided` is published after the event that ended the round (`Room.Emit` settles a
  round that ended in the same transition after publishing). Both are pinned by `RoomScoringTests` and were mutation
  checked.
- A score on a beatmap the server does not have is accepted when the lobby says a round of an open room still awaits
  it from the player (then `IConnectionLobbyPort.AwaitsScore`, now `IScoreBeatmapAcceptance.AcceptsUnknown`, implemented
  by `Lobby`): a completer who left is accepted, a decided round's late
  score is rejected as `UnknownBeatmap`. Known limit (D16 as written): a late score of a decided round on the same
  beatmap as the next round is taken by the next round if it awaits that player, because the match is by player and
  beatmap only.
- Known deviation: `MatchReportRepository` assigns `Round.Result` on the live tracked round instances (derived from the
  stored decision, so the value is the same one `Room.Decide` set); the plan said to remove `Round.Result`. The Api
  host reads `Round.Result` and is outside the build, so it is kept until the hosts are migrated.
- Precondition of the in-place migration edit: no database may have applied the old `InitialCreate` (the local
  cluster holds only the default database). A database that did will fail on `rounds.result`. State this to the owner.

### Phase D2 (partial) - 2026-10-27

- `tests/Architecture/ModuleGraphTests`: the namespace graph of Application.Runtime as a **ratchet**. Target edges
  (`Scores -> Users, Beatmaps; Multiplayer -> Users, Beatmaps, Scores; Chat -> Users, Multiplayer`; `Common`/`Events` are
  the shared kernel) plus an explicit list of the debt edges that exist today; a new edge fails, a debt edge that is
  gone fails (so the list only shrinks), and a positive control proves the scanner sees edges. Every edge to or from
  Sessions, Servers and Anticheat is debt until their target edges are decided (A2 answered: each is its own module).
- Cut so far: the connection and restriction handlers were split by the module that owns each reaction
  (`Chat.ConnectionChannelHandler`, `Multiplayer.ConnectionLobbyHandler`, `Multiplayer.RestrictionLobbyHandler`;
  `Connections.ConnectionHandler`/`RestrictionHandler` keep spectating), with their tests moved.
- Sessions debt cut (2026-10-27): `2f53ddfd` moves score submission to `ScoreSubmitter.ScopeTo(connection).SubmitAsync`
  and drops the score members of the connection and `ConnectionContext` (Sessions -> Scores); `b35d0671` moves the report
  to `AnticheatReporter.Report(player, flags)` and removes `IConnectionLobbyPort` (Sessions -> Anticheat and
  Sessions -> Multiplayer); `a0765cfd` moves `SpectatorChannel` into `Chat.SpectatorChannels`, kept in step by the
  handlers for `UserSpectatorJoined`, `UserSpectatorLeft` and `UserConnectionClosed` (Sessions -> Chat).
- Debt cut afterwards (2026-10-28, uncommitted): `Chat.RoomChannels` keeps a room's channel (handlers for the lobby and
  room events; `Of(room)` synchronises membership under the room lock and never resurrects a closed room's channel;
  Multiplayer -> Chat); `Multiplayer.Rooms.RoomBlocks` answers block questions from `IRelationshipRepository`
  (Users -> Multiplayer, `User.HasBlockedAsync` removed); `Chat.PmChannels` keeps a private-message channel per online
  user, synchronised at `Of(user)` so a whisper right after a login is delivered (Users -> Chat); then the online-user
  types (`User`, `UserRegistry`, `ScopedUser*`, `UserContext`, `LoginResult`, `UserOutcome`) moved out of `Users/` into
  the sessions area (Users -> Sessions). Channel membership of room and PM channels is eventually consistent with the events.
  The debt list in `ModuleGraphTests` is empty.
- Sessions dissolved (2026-10-27, uncommitted, owner decision): "Sessions" is a technique, not a module, so the
  folder and namespace are gone. Pure move, no behaviour change. The online-user types (`User`, `UserContext`,
  `UserRegistry`, `ScopedUser*`, `LoginResult`, `UserOutcome`, `UserEvents`) live in `Users/`; the connections
  (`Connection` and its kinds, `ScopedBanchoConnection`, `ConnectionContext`, `ConnectionRules`, `ConnectionOutcome`,
  `PlayerStatus`, the connection and restriction handlers) live in `Connections/`. `Users` and `Connections` use each
  other and are accepted as one cluster (the one target cycle); the `ModuleGraphTests` areas are `Users` and
  `Connections` again with an empty debt list, and a new test rejects any other cycle between areas.
- **Known deviations:** `AnticheatAnnouncer` finds the room when it handles the event, not when the flag is reported,
  so a player who leaves the room within the event queue's latency is not announced in that room's chat; the flag is
  still published and recorded. The planned fix is a Multiplayer-side handler.
- Not done in D2: chat decoupling, the other cycles, the project split (Phase E).

Target edges proposed 2026-10-27 and encoded in `ModuleGraphTests` (Auth has no Runtime namespace; it is Persistence and
Capabilities only): Beatmaps depends on nothing; Users and Connections use each other (the one accepted cycle: a user
holds its connections, a connection knows its user); Anticheat -> Connections, Users;
Scores -> Connections, Users, Beatmaps; Multiplayer -> Connections, Users, Beatmaps, Scores; Chat -> Connections, Users,
Multiplayer; Servers -> every module, and nothing references Servers. Of the 11 debt edges listed when the ratchet was
written, the actor interface in Common (Beatmaps -> Connections/Users), the anticheat event naming the player only
(Anticheat -> Multiplayer) and the four Connections edges (Scores, Anticheat, Multiplayer, Chat) are cut; four remain:
Chat reacting to room events (Multiplayer -> Chat) and the online user (with its connections) moving to Connections
(Users -> Chat/Multiplayer/Connections).

Next: E (module projects).

Later on 2026-10-27 the owner asked whether `IEventQueue` was still needed: the queue is (order, handlers never run under a room lock), the interface is not. `EventQueue` now belongs to Application (`RunAsync`, internal `Enqueue`/`DrainAsync`); `EventDispatcher` only calls `RunAsync`. Tests hold events by not running the queue, so `TestApplication` needs no stub.
