# AGENTS.md

This file provides guidance to coding agents working in this repository. `CLAUDE.md` only imports
it.

## What this is

Basil is a private [osu!](https://osu.ppy.sh/) stable server focused on offline multiplayer tournaments.

It is built on [bancho.py](https://github.com/osuAkatsuki/bancho.py), but it is **not a full bancho.py port**. Basil deliberately has a smaller feature surface. pp calculation, clans, a general-purpose public v1/v2 API, the full bancho.py chat-command set, and other unrelated features are intentionally out of scope.

Before porting or recreating anything from bancho.py, read:

* [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md) — authoritative feature scope
* [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) — system structure and dependency direction
* [`README.md`](README.md) — project overview

The generated HTTP API and BasilBot command reference is available at `api.<domain>/docs/` on a running instance and on [GitHub Pages](https://thnhmai06.github.io/osuBasil/). Do not create a second hand-written API reference in Markdown.

## Rules

### 1. Think before coding

**Do not assume. Do not hide uncertainty. Surface tradeoffs.**

Before implementing:

* State important assumptions.
* If multiple interpretations are plausible, identify them.
* If a simpler solution exists, prefer it and explain the tradeoff.
* If a requirement is genuinely ambiguous, ask before coding.

Do not silently invent requirements.

### 2. Simplicity first

Implement the smallest solution that satisfies the request.

Do not add:

* speculative features
* unused abstractions
* unnecessary configurability
* future-proofing without a concrete requirement
* error handling for impossible states

If a solution is significantly more complicated than the problem requires, simplify it.

### 3. Surgical changes

Change only what the task requires.

When modifying existing code:

* preserve existing behavior unless the task changes it
* match the surrounding style
* do not refactor unrelated code
* do not rewrite adjacent comments or formatting
* do not remove unrelated dead code

Remove imports, variables, methods, or other artifacts only when **your own changes** make them unnecessary.

Every changed line should have a clear connection to the task.

### 4. Goal-driven execution

Define a concrete success criterion before implementing.

Examples:

```text
"Add validation"
→ Add tests for invalid input, then make them pass.

"Fix the bug"
→ Reproduce the bug with a regression test, then make it pass.

"Refactor X"
→ Verify behavior before and after the refactor.
```

For multi-step work, use a short plan:

```text
1. [change] → verify: [check]
2. [change] → verify: [check]
3. [change] → verify: [check]
```

Do not stop at "the code looks right". Verify the result.

### 5. API documentation

Every `.WithSummary` and `.WithDescription` on an `api.` host route describes the **public API contract**, never its implementation.

Apply the Implementation Test:

> If the implementation were replaced while the endpoint's behavior remained identical, would this sentence need to change?

If yes, it is implementation detail and does not belong in the API documentation.

#### Summary

Use one sentence describing what the endpoint does.

```csharp
.WithSummary("Retrieve a match report.")
```

Do not describe storage, frameworks, services, or internal mechanisms.

#### Description

Use Markdown paragraphs in this order:

1. what the endpoint provides
2. important special cases
3. related or live endpoints
4. important errors

Do not restate information already represented by the OpenAPI schema, such as the HTTP method, path, ordinary parameter definitions, or declared response type.

Prefer raw strings for multi-paragraph descriptions.

Never document implementation details such as:

* SQLite
* Redis
* DI
* middleware
* `Channel<T>`
* background services
* cache keys
* internal file paths
* internal service or type names

The exception is when the mechanism itself is part of the public contract, for example:

```text
The response is streamed using Server-Sent Events.
```

### 6. XML documentation

Every `///` comment describes **responsibility and observable behavior**, never implementation.

Apply the same Implementation Test as rule 5.

#### Summary

Use one sentence answering:

> What is this member, and what does it do?

Do not simply restate its name.

#### Remarks

Use remarks only for additional caller-visible behavior that does not fit in the summary.

Never document:

* temporary files
* locks
* cache keys
* dictionaries
* reflection
* switch statements
* framework implementation
* dependency injection
* internal collaborators

Do not document historical implementation decisions unless the member exists specifically for backward compatibility.

`<param>` and `<returns>` describe the meaning of the value, not how it is produced.

Use `<see cref>` only when it improves navigation for the reader.

### 7. Markdown documentation

`docs/*.md` explains **why the system exists, how it works, and how its components fit together**.

XML documentation is reference material. Markdown documentation is architecture and guidance.

Follow these rules:

* Explain **Why → What → How**.
* Keep one major topic per file.
* Start with an overview before implementation details.
* Separate public contract from implementation.
* Put implementation details in a dedicated Design/Implementation section.
* Include design rationale where useful.
* Prefer diagrams for multi-step flows.
* State important system invariants explicitly.
* Use short illustrative examples.
* Do not paste large amounts of source code.
* Link to related documentation instead of duplicating it.
* Include a `Related code` section when source locations are useful.
* Do not duplicate the generated HTTP API or BasilBot command reference.

Authoritative topic ownership is defined by [`docs/index.md`](docs/index.md).

If a topic already has an authoritative document, update that document instead of creating a competing description elsewhere.

### 8. Test writing

**Tests pin observable contracts, not implementation details.**

Apply the Implementation Test:

> If the implementation changed but the observable behavior stayed the same, would this test still pass?

If not, the test is probably testing implementation.

Contract examples include:

* bancho packet bytes
* IRC wire text and numerics
* HTTP status codes
* JSON and envelope shapes
* API error codes
* user-visible BasilBot replies
* multiplayer state transitions

Do not normally assert:

* Serilog messages
* debug output
* internal diagnostic strings
* exception messages that never reach a client
* internal call counts or ordering

See [`docs/for-developers/testing.md`](docs/for-developers/testing.md) for the complete policy.

Important rules:

* one behavior per test
* one reason for failure
* deterministic tests
* no real-time sleeps
* no uncontrolled randomness
* no environment-dependent state
* test through public APIs
* cover relevant edge cases
* add a regression test for every reproducible bug fix

User-visible contract strings must be centralized in the production code that owns them. Tests reference those constants rather than duplicating literals.

### 9. User-visible reply strings

User-visible chat text is public behavior.

Each chat surface owns its reply constants.

Examples:

* `MpReplies` for `!mp`
* `IrcReplies` for IRC responses

Do not scatter user-visible strings across handlers.

Changing the wording of a user-visible response is a contract change. Update the named production constant and its tests deliberately.

Internal diagnostics and logs are not subject to this rule.

## Commands

Restore and build:

```bash
dotnet restore
dotnet build --configuration Release
```

Run Basil locally:

```bash
dotnet run --project src/Basil.Host
```

Run all tests:

```bash
dotnet test
```

Run one test project:

```bash
dotnet test tests/Basil.Application.Tests
```

Run a specific test class:

```bash
dotnet test tests/Basil.Application.Tests \
  --filter "FullyQualifiedName~<TestClassName>"
```

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow.

See [`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md) for runtime configuration and data directories.

See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker.

## Architecture

```text
Basil.Domain                     business model and its own validity          -> (nothing)
Basil.Protocol.Bancho            bancho wire format                            -> (nothing)
Basil.Protocol.Irc               IRC wire format                               -> (nothing)

Basil.Application.Storage        persistent ports, registries, runtime models,  -> Domain
                                 paging and query records
Basil.Application.Contracts      service contracts, capability ports, events,   -> Storage
                                 inputs and results
Basil.Application.Services       service implementations (internal)             -> Contracts

Basil.Infrastructure             persistence, files, media, capability ports,   -> Contracts, Protocol.*
                                 background loops, event dispatch and handlers     (never Services)
Basil.Host.Bancho / .Irc / .Api  transports                                     -> Contracts, Services,
                                                                                   Infrastructure, their Protocol
Basil.Host                       entry point and composition                    -> everything
```

`Basil.Infrastructure` never references `Basil.Application.Services`; it gets every service through
dependency injection by its contract. The hosts may reference Services, which sits beside Infrastructure, but
only to call `AddApplicationServices()`; they too use services through their contracts.

> **Migration in progress.** Application has been split into Storage, Contracts and Services by
> [`plans/storage-services-split-plan-20261003.md`](plans/storage-services-split-plan-20261003.md) (phases 1–6
> done; its section 13 lists where the code differs from the plan); read it before touching Application. It
> replaces the "runtime objects carry their own behaviour" part of
> [`plans/application-environment-plan-20260930.md`](plans/application-environment-plan-20260930.md), whose
> other decisions (naming, event tree, channel names, identity by reference) still hold. Older plans are
> history.
>
> Infrastructure, the hosts and the tests have not been migrated. Until they are, **only `Basil.Domain` and
> the three Application projects build**: `Basil.Infrastructure`, `Basil.Host.*` and every test project still
> use old Application namespaces, so solution-wide `dotnet build`/`dotnet test` and `Basil.ArchitectureTests`
> do not run. Verify with `dotnet build src/Basil.Application.Services/Basil.Application.Services.csproj`
> (it builds Storage and Contracts too).
>
> Documentation under `docs/` may describe an older structure; rewrite each document when the code it
> describes is migrated, not before. Where a plan conflicts with
> [Domain and Application](#domain-and-application) below, this file wins.

### Application feature folders

Each Application project is sliced by feature, not by kind of type; namespace = project + folder. The same
feature folders appear in Storage, Contracts and Services.

```text
Common/        PageRequest, Page<T>, Interval<T>                                   (Storage)
Events/        Event, IEventPublisher<T>                                           (Contracts)
Users/         IUserRepository, ICredentialRepository (passwords and admin key), ILoginRepository,
               IRelationshipRepository, IUserAvatarStorage, UserQuery, LoginQuery;
               IAuthService, IUserService, LoginAttempt, RegisterAttempt, results
Sessions/      UserSession, Connection (+ ConnectionType), UserRegistry, PlayerStatus,
               SpectatorChannelSession; ISessionService and its events
Chat/          ChannelSession tree, GeneralChannelRegistry, IChannelRepository;
               IChannelService, channel events and results
Multiplayer/   Lobby, Room, RoomSlot(s), RoomChannelSession, IMatchRepository, IRoundRepository,
               IMatchEventRepository, MatchQuery; ILobbyService, IRoomService, IMatchService,
               room and lobby events, RoomResult, RoomSettingsChange
Beatmaps/      beatmap and beatmapset repositories, IBeatmapsetStorage, BeatmapQuery;
               IBeatmapsetService, IBeatmapAnalyser, IBeatmapsetReader, IBeatmapAssets,
               IBeatmapsetMirror, beatmapset events
Scores/        IScoreRepository, IReplayStorage, IUserStatsRepository, ScoreQuery;
               IScoreService and its events
Anticheat/     IAnticheatService (judges client flags) and its events
Content/       ISettingsRepository, IMenuBannerRepository, IMenuBannerStorage, IMenuIconStorage,
               IMenuSeasonalsStorage, IFaqStorage
```

Notifications, event handlers and dispatchers, chat commands and their reply strings, and host
configuration (ports, TLS, data paths, bot command prefix) do not belong in Application; they live in
Infrastructure, the hosts or the bot. Query **records** (the filter a repository applies, with `Parse` for
the search syntax) belong to Storage; the routes and handlers that answer queries do not. Server settings
changed at runtime (`ServerSettings`) are persistent Domain data, not host configuration.

* `Common` and `Events` depend on no feature. `Users`, `Beatmaps` and `Scores` do not depend on
  `Multiplayer` services; the score service only reads the player's room to find the round. Anticheat flags,
  wherever they arrive (score submission, `lastfm.php`), are reported by the host to `IAnticheatService`, the single
  source of anticheat events; Infrastructure's dispatcher delivers its warnings (room chat, referees, creator).
* `Sessions`, `Chat` and `Multiplayer` reference each other (`Room.Host`, `Room.Channel`,
  `ChannelSession.Members`); treat them as one cluster. Their runtime models live together in Storage, and
  Services changes them through `internal` members (`InternalsVisibleTo("Basil.Application.Services")`).
  References run from the acted-on object to the actor, never back.

### Domain and Application

* **Domain** holds business models that exist beyond runtime (`Match`, `Round`, `MatchSettings`,
  `User`, `Beatmap`, `Login`, `ServerSettings`, …). A model guarantees that **it is valid**: field values
  in range, its own text format, values derived purely from its own data. No runtime-only state, no events.
* **Storage holds data, Services hold behaviour.** `Basil.Application.Storage` holds the persistent ports
  and what exists only at runtime (`Room`, `RoomSlot(s)`, `UserSession` and its connections,
  `ChannelSession`s) together with the registries that keep them (`UserRegistry`, `GeneralChannelRegistry`,
  `Lobby`): data, lookups and concurrency scopes only. Storage never decides a rule, emits an event, calls a
  service or starts a timer. `Basil.Application.Services` holds every action: a service guarantees that a
  change **is meaningful** (authority, timing, osu! rules, not tampered with, the consequences of the
  change). A class named like a model (`Gateway`) must not hold logic either.
* **One contract per service.** `IXService` in `Basil.Application.Contracts` declares every public action of
  `XService` and its event stream (`IRoomService : IEventPublisher<RoomEvent>`). This is a deliberate
  exception to "no interface with one implementation": the contracts are the list of capabilities the
  system offers, independent of how they are implemented. Implementations are `internal sealed` and
  registered by `AddApplicationServices()`. Where one service may call another (see "Peers talk through events"), it
  calls the contract; coordination that is
  not a public capability (`LobbyService.RoomEmptied`, `RoomRules`) is an `internal` member called through
  the concrete class inside Services.
* **Capability ports.** A capability with business meaning whose implementation needs external libraries
  or resources is a contract in Contracts that Infrastructure implements (`IBeatmapAnalyser`,
  `IBeatmapsetReader`, `IBeatmapAssets`, `IBeatmapsetMirror`), even when only the hosts use it. Pure
  plumbing (logging, diagnostics, metrics, SSE hub, TLS, mDNS, update checks, OpenAPI, the response
  envelope) gets no contract.
* **Contract names do not reveal the mechanism.** If the work moved from the file system to the network, or
  from one library to another, the name would not change: `ScanAsync`, not `ScanFileAsync`;
  `IBeatmapAssets.OpenAsync`, not `ReadFromDiskAsync`.
* **Registries, runtime models and query records get no interface.** They are not ports. Do not add
  `IUserRegistry` or `ILobby`. Ports are for what lies outside the process: storage, files, network,
  external computation, clock (`TimeProvider`).
* **Outer layers use storage directly.** Infrastructure, the hosts and the bot read, list and search
  storage, and write persistent records whose write carries no rule or consequence (an admin renames a
  user, hides a beatmapset), directly through the repository. Services are only for actions; a service
  never wraps a plain read or write (`ScoreService.GetAsync` forwarding to `IScoreRepository.GetAsync` is
  wrong). Runtime state changes only through services: runtime setters are `internal`.
* **The service of an object's kind enforces its rules in the same operation.** When a rule belongs to the
  environment (a room closes when its last player leaves, a room keeps its channel's members in step with
  its players), the service that performs the operation enforces it; it is never left to a hook that might
  not be installed.
* **Background work.** A contract declares one run of a background job (`ISessionService.CloseIdle()`,
  `IBeatmapsetService.ScanAsync()`); Services implements it; Infrastructure owns the loop, the trigger, the
  period and its configuration. A one-shot timer that is the consequence of an operation (an empty room's
  closing, a countdown) stays in Services on `TimeProvider`.
* **Derive whatever is derivable.** A property a runtime object co-owns with its domain object
  (truth in Domain) is a forwarding property (`Room.Name => Match.Name`).
* **A user online is one `UserSession`; each place they log in from is a `Connection`.**
  `UserSession` (sealed) represents the user regardless of where they connect and holds what is
  shared (`AwayMessage`, `PmChannel`) plus its connections as child data, keyed by
  `ConnectionType` (`Bancho`, `Tourney`, `Irc`, `Bot`), with typed accessors (`Bancho`, `Irc`, `Bot`,
  `Tourneys`); only `Tourney` allows several connections per user, a rule the session service owns.
  `Connection` is an abstract class (`Session`, `Login`, `IsOpen`, `Type`) with `BanchoConnection`,
  `TourneyConnection`, `IrcConnection`, `BotConnection`; each holds only what exists for that kind of client
  (`BanchoConnection.Status`, `BanchoConnection.SpectatorChannel`). Rooms, channels and the lobby
  hold connections, not sessions. Owned parts (a session's `PmChannel`, a connection's
  `SpectatorChannel`) are created and destroyed with their owner; they are not relations.
* **Identify by the basis object**, not its id (`Room` by `Match`, `GeneralChannel` by `Name`). Runtime
  objects that can be recreated with the same user or name compare **by reference**: a connection
  is one login, a `UserSession` is one period online (look it up by `User` through `UserRegistry`), a
  runtime channel is one opening (`#mp_5` is reused when a room id is reused). Cleanup that carries
  an old object can never touch its replacement. "Is this user already here?" compares the user
  and is a separate check from object identity. Ids are for repository lookups only. Choose object vs identifier
  deliberately (`Room.Beatmap` object, `PlayerStatus` beatmap `Md5?`); never `string` for an MD5.
* **Relations are references** in `ConcurrentSet<T>`, never `ConcurrentDictionary<T, byte>`.
* **`User` or a connection?** If the information is gone once the user goes offline, store the
  connection (`BanchoConnection` as slot occupant or host); otherwise the `User` (creator, referees,
  bans).
* **The acted-on object owns the interaction.** An actor (a user, a session, any active object)
  only *calls* an action. The relation is stored on the record of the object acted on
  (`ChannelSession.Members`, `Room.Slots`), the operation lives in the service of that object's kind
  (`IChannelService.Join(channel, by)`, `IRoomService.JoinAsync(room, by, password)`), and the event belongs to
  that object's category (`ChannelMemberJoined` is a `ChannelEvent`, emitted by the channel service). The
  session does **not** keep a `Channels` set, does **not** have a `Join(ChannelSession)` method, and nothing
  emits events on its behalf. Otherwise every new joinable kind (channel, room, spectator stream, …) forces a
  new `Join*` method and a new set on the user. "Which channels is X in?" is a query over channels, not state
  on X. Ask "who is acted on?" for every relation, member and event.
* **Each service manages its own kind, and reacts to other kinds only through events.** The session service
  opens and closes connections and announces `UserConnectionOpened`/`UserConnectionClosed`; its responsibility
  ends there. Leaving rooms and parting channels when a connection closes is done by Infrastructure handlers
  that receive the event and call the owning service. When a second connection of the same kind arrives
  for a user an owner still holds, the owner **asks the old connection who it is** (`connection.IsOpen`):
  still open, refuse the new one; already closed (cleanup not run yet, or failed), remove the old one as an
  ordinary leave and admit the new one. Cleanup operations must be safe to run again.
* **Peers talk through events; parents call their children directly.** Two services are peers when neither
  owns the other and neither's rule depends on the other (scores and rooms, the anticheat and rooms): the
  acting service only emits its event, and Infrastructure's dispatcher delivers it to the peer by calling the
  peer's contract (`ScoreSubmitted` → `IRoomService.RecordScoreAsync`, `AnticheatPlayerFlagged` → the room's
  chat). A service calls another directly only for a clear parent/child or ownership relation, or to keep a
  rule that must hold within the same operation: services use storage; a room manages its own channel and a
  session its PM and spectator channels; the room service tells the lobby when a room empties or fills;
  logging in opens the connection; deleting a user closes their connections at once, a security rule that
  must not wait for a handler.
* **Application emits events; it does not consume them.** Each service is one event source over its own
  `Channel<T>`; storage never emits. One operation emits one event carrying its whole consequence. Events sit
  in a nested category tree (`Event` ← `RoomEvent` ← `RoomSlotEvent` ← `RoomSlotTeamChanged`) so a consumer
  can subscribe to a whole category. Each item of a `Channel<T>` reaches one reader, so Infrastructure runs
  exactly one dispatcher per service stream that fans out to its handlers; nothing else reads `Events`
  directly. Handlers, dispatchers and event → client-notification routing belong to Infrastructure and the
  hosts, never to Application.
* **Repository contracts are written per model** as `IXxxRepository` (`IMatchRepository`,
  `IScoreRepository`, …), each declaring the operations Services and the outer layers need. Verbs:
  `CreateAsync(XData) → X` (the store assigns the identity), `CreateOrUpdateAsync(X)`, `GetAsync(id)`,
  `GetByYAsync(y)` for a unique key, `ListAsync(XQuery, PageRequest) → Page<X>` for listing and search,
  `DeleteAsync(X)`. Soft deletion is a service setting `DeletedAt` and calling `CreateOrUpdateAsync`. `Save`
  is only for byte storages (`IXxxStorage`: `SaveAsync`, `OpenAsync`, `DeleteAsync`). Extract a shared
  interface only when several repositories genuinely share an operation, and keep the `IXxxRepository`
  contract callers depend on; no parallel generic ports (`ICreatable`) and no generic query system
  (`Query<T>`, `SortOptions`). Key normalization (for example case- and space-insensitive user names) is the
  repository's own lookup concern. A repository filters only by explicit criteria in its query record
  (`IncludeHidden`, `IncludeDeleted`, `IncludePrivate`); the caller sets them from the asker's authority.
* **Naming: Domain model `X`, runtime model `XSession`, the object that holds the live sessions
  `XRegistry`, the contract `IXService` and its implementation `XService`.** No suffixes such as
  "Definition". `Channel` → `ChannelSession` → `GeneralChannelRegistry` (it holds only general channels);
  `User` → `UserSession` → `UserRegistry`. The osu! terms win for matches: `Match` → `Room` → `Lobby`. The
  room's channel is named after the room (Domain `RoomChannel`, runtime `RoomChannelSession`) because the
  room is where things happen and `Match` is only the record. `Channel` shares its name with
  `System.Threading.Channels.Channel<T>` on purpose; the two differ by generic arity.
* **Event names are `{Group}{Subject}{PastTenseVerb}`.** The group is the category the event belongs
  to (`Room`, `Channel`, `User`, `Lobby`); when the subject is the group itself it is written once
  (`ChannelOpened`, `UserSilenced`). A subject that belongs to another is written `{Parent}{Child}`
  (`RoomRoundPlayerLoaded`, `RoomSlotTeamChanged`, `UserConnectionOpened`). The abstract category
  records follow the same rule (`RoomRoundEvent`, `ChannelMembershipEvent`).
* **Chat channels.** Domain: abstract `Channel` (`Name`, `Topic`, IRC convention) with
  `GeneralChannel` (configured chat; alone carries `ReadPrivilege`, `WritePrivilege`, `AutoJoin`,
  `Visible`), `RoomChannel`, `SpectatorChannel`, `PmChannel`; messages are the record `Message`.
  Runtime: abstract `ChannelSession` with `GeneralChannelSession`, `RoomChannelSession`,
  `SpectatorChannelSession`, `PmChannelSession`. **Each channel lives on the record of its owner**:
  `GeneralChannelRegistry` holds the general channels, a `Room` its room channel, a `UserSession` its PM
  channel, a `BanchoConnection` its spectator channel. The channel service decides, per kind of channel, who
  may read and write. A private message is a post into the recipient's PM channel. Access follows osu!: a
  user may join a channel (including `/join` over IRC) only if an osu! client doing the matching in-game
  action would have access.
* **Privilege checks require every bit.** A user satisfies a `ClientPrivileges` requirement only if
  every bit set in the requirement is set on the user (`ClientPrivileges.Has`); an empty
  requirement is always satisfied. This applies everywhere, not only to channels.
* **A channel's name tells its kind.** A channel several users take part in (general, room,
  spectator) is named `#name` (`#osu`, `#lobby`, `#mp_5`, `#spec_7`); a private-message channel is
  named after its owner, without `#`, as an IRC nick is. This is a naming convention each kind of
  channel follows when it names itself; nothing else checks it. `#multiplayer`/`#spectator` are
  aliases the transport resolves to the sender's room or spectator channel.
* **BasilBot is an ordinary user with fixed rules.** It is always user id `0` (`SystemUserIds.BasilBot`) in
  `IUserRepository`; the session service creates it at startup when it is missing and opens its
  `BotConnection`. Its name and country are edited through the repository like any user's; configuration
  keeps only the command prefix. It can never be deleted, silenced, given other privileges, kicked, banned
  or made a referee, and it can never log in from a client, whatever password is stored for it.
* **The API acts as BasilBot.** A route of the admin API calls services with the bot's connection as `by`;
  `by` is never `null`. The bot's connection has server authority and passes every room and channel
  authority check. A route answers 503 while the bot is offline.
* **Setters only assign and validate.** A Domain setter stores the value and throws if it is invalid; a
  runtime setter is `internal set` and only assigns. A setter never emits an event or changes anything else.
  A change that has consequences (emits an event, changes other fields, checks authority, groups several
  fields into one event) is a service method. Such a method is a real operation, not a
  `Change*`/`Rename`/`With*` wrapper around a single field.
* **Name a contract after the object it acts on.** The unit that is imported, stored, read and mirrored is
  the beatmapset, so the contracts are `IBeatmapsetService`, `IBeatmapsetReader`, `IBeatmapsetStorage`,
  `IBeatmapsetMirror` and `BeatmapsetImportResult`. A byte storage is `I{Owner}{Thing}Storage`
  (`IUserAvatarStorage`, `IMenuBannerStorage`, `IMenuSeasonalsStorage`); medium words (`Image`,
  `Background`, `Archive`) and redundant qualifiers (`Server` in `ISettingsRepository`) stay out of names.
* **Knowledge about a Domain value lives on that value.** A fixed set of flags is a member of its enum
  (`ClientFlags.CheatSigns`), a per-mode factory a static member of its type (`BeatmapObjects.NewFrom(mode)`);
  services use them instead of keeping their own copies.
* **One concept, one name.** Use the same name for the same concept in every model, parameter,
  event and API: the moment something happened is `Timestamp`, a span is `StartedAt`/`EndedAt`, a
  future window is `StartsAt`/`EndsAt`, a stored record's lifecycle is
  `CreatedAt`/`UpdatedAt`/`DeletedAt`, the caller of an operation is `by`. Do not introduce
  `OccurredAt`, `When`, `Since`, `LoginTime` or similar synonyms.

### Code style

* A small type used by one type lives in that type's file (`ConnectionType` in `Connection.cs`).
* A member that uses no instance state is `static` (`Lobby.EnterAsync`, `LobbyService.RoomOccupied`).
* Prefer primary constructors, property patterns (`room is { InProgress: false, Beatmap: not null }`) and
  one guard for conditions that return the same result; no redundant casts, `? true : false` or
  discarded-task lambdas (`_ => action()`, not `_ => _ = action()`).
* The Application registration class is `DependencyInjection` with `AddApplicationServices()`.

### Important invariants

#### Multiplayer concurrency

`Room` is mutable shared state.

Operations that read and then mutate a room must hold the room's exclusive scope across the complete state transition. That scope comes from `Lobby.EnterAsync(room)`, which returns `null` once the room is closed; the registry owns the lock, like a database transaction.

Do not introduce a second synchronization mechanism for the same state.

Do not hold the room scope across long-lived or unrelated waits.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) for the complete model.

#### Authority

Referee, host, and creator are separate concepts.

Whoever creates a room is its creator, however it was created (in game, `!mp make`, or the API when
a creator is given; an API-created room without one has no creator). The creator is remembered on
the match and ranks above referees; it is not added to the referee list. A creator who created the
room in game is also its first host. After `!mp make`, a creator whose game client is online and
not in any room is seated in the new room as host; a creator already in another room stays there
and does not become host. `!mp` is available only to the creator and the referees; being
the host does not grant `!mp` rights. Every room operation with a permission rule takes its actor
and the room service (`RoomService`) checks authority, not the transport.

Do not treat them as interchangeable.

The creator has permanent match authority. Referee membership and host state have different lifecycles.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) before changing match permissions or `!mp` authorization.

#### No pp in gameplay

Basil does not use performance points for scoring, leaderboards, or match win conditions.

Locally calculated difficulty/star-rating information is display-only.

Do not introduce pp-dependent gameplay behavior.

#### Stable protocol

Bancho packet layouts are wire contracts.

Changes to packet encoding or decoding must be treated as protocol changes and covered by protocol tests.

#### SSE

Live HTTP streams use dedicated `/live` endpoints.

Do not introduce content negotiation between JSON and SSE on the same route.

See [`docs/for-developers/sse.md`](docs/for-developers/sse.md).

#### API responses

The `api.` host uses a common response envelope for JSON responses.

The generated OpenAPI schema must describe the same contract that the runtime returns.

Do not manually work around the envelope in individual routes without first checking the API middleware and schema transformation rules.

See [`docs/for-client/response-envelope.md`](docs/for-client/response-envelope.md).

#### User-visible enums

Keep API record properties typed as their actual enum types.

Do not replace enums with `int` or `string` merely to influence serialization.

Serialization behavior belongs to the API contract and is documented separately.

#### Naming

Use `Privilege`, not `Priv`, for new code and public models.

Do not reintroduce the old abbreviation.

## Scope

Basil is deliberately smaller than bancho.py.

Do not implement a bancho.py feature simply because it exists upstream.

Before adding:

* a chat command
* a persistence model
* an API surface
* a social feature
* a scoring feature
* a compatibility endpoint

check [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md).

If the requested behavior conflicts with the documented scope, surface that conflict before coding.

## Repository documentation

Use [`docs/index.md`](docs/index.md) to find the authoritative document for a topic.

Important developer documents:

* [`architecture.md`](docs/for-developers/architecture.md) — architecture and dependency direction
* [`working-scopes.md`](docs/for-developers/working-scopes.md) — feature scope
* [`testing.md`](docs/for-developers/testing.md) — test policy
* [`development.md`](docs/for-developers/development.md) — local development
* [`multiplayer.md`](docs/for-developers/multiplayer.md) — match/session behavior
* [`sse.md`](docs/for-developers/sse.md) — live HTTP streams
* [`database.md`](docs/for-developers/database.md) — persistence design
* [`logging.md`](docs/for-developers/logging.md) — logging design
* [`docs-guideline.md`](docs/for-developers/docs-guideline.md) — documentation rules

Technician documentation:

* [`configuration.md`](docs/for-technicians/configuration.md) — configuration and data directories
* [`https.md`](docs/for-technicians/https.md) — TLS requirements
* [`docker.md`](docs/for-technicians/docker.md) — Docker deployment

Agent documentation:

* [`guidelines.md`](docs/for-agents/guidelines.md) — agent workflow
* [`domain.md`](docs/for-agents/domain.md) — domain-modeling guidance
* [`issue-tracker.md`](docs/for-agents/issue-tracker.md) — GitHub Issues workflow

## Agent skills

### Issue tracker

Issues live in GitHub Issues for [`thnhmai06/osuBasil`](https://github.com/thnhmai06/osuBasil).

See [`docs/for-agents/issue-tracker.md`](docs/for-agents/issue-tracker.md).

### Domain modeling

Domain-modeling work uses:

```text
CONTEXT.md
docs/adr/
```

These files are created lazily by the `/domain-modeling` workflow.

See [`docs/for-agents/domain.md`](docs/for-agents/domain.md).

## Final verification

Before considering a code change complete:

1. Run the smallest relevant tests while iterating.
2. Run the complete test suite for the final change.
3. Run a Release build when the change affects production code.
4. Check architecture tests for cross-layer changes.
5. Update authoritative documentation when behavior or design changes (except during the
   Application rework, see the migration note under [Architecture](#architecture)).
6. Review the final diff for unrelated changes.
7. Check every new or changed member in the Application projects against
   [Domain and Application](#domain-and-application), above all "storage holds data, services hold
   behaviour", "the acted-on object owns the interaction" and "Application emits events; it does not
   consume them". This applies to code written by delegated agents too: the orchestrator reviews it
   against these rules before accepting it. `UserSession.Channels`, `UserSession.Join(ChannelSession)`,
   sessions emitting events, event handlers inside Application, and rules or timers on runtime models and
   registries all got through review once; do not let them back in. Nor should a service that only wraps
   a repository read, an interface for a registry, a contract name that reveals its mechanism, or a
   reference from Infrastructure to `Basil.Application.Services`.

The goal is not merely to produce compiling code. The goal is a verified change that respects Basil's architecture, scope, contracts, and documentation.
