# AGENTS.md

This file provides guidance to coding agents working in this repository. `CLAUDE.md` only imports
it.

## What this is

Basil is a lightweight, high-performance [osu!](https://osu.ppy.sh/) (stable) backend platform specialized for
multiplayer. The server provides the core capabilities (accounts and sessions, permissions, rooms, chat, scores,
beatmaps, events); specific features such as tournament tooling and overlays are clients built on top
of it, each signing in as an ordinary user. The server works the same with or without any of them.

It is built on [bancho.py](https://github.com/osuAkatsuki/bancho.py), but it is **not a full bancho.py port**. Basil deliberately has a smaller feature surface. pp calculation, clans, osu-web v1/v2 API compatibility, and other unrelated features are intentionally out of scope. Chat commands, announcements and room countdowns are in scope: they are BasilBot, a feature of the server.

The direction of the current redesign (identity, permissions, restrictions) is in
[`plans/identity-permissions-plan-20261005.md`](plans/identity-permissions-plan-20261005.md); read it before
touching users, sessions, permissions, rooms or chat.

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

* PostgreSQL
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
dotnet run --project src/Hosts/Host
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

While the hosts and the tests are not migrated (see the migration status under [Architecture](#architecture)),
verify a change with the six projects that do build, one at a time:

```bash
dotnet build src/Domain --nologo -v q
dotnet build src/Application/Services/Implementations --nologo -v q
dotnet build src/Infrastructure/Storage --nologo -v q
dotnet build src/Infrastructure/Services --nologo -v q
dotnet build src/Infrastructure/Runtime --nologo -v q
dotnet build src/Bot/Application --nologo -v q
```

A change to a constructor, a context or `DependencyInjection.cs` also needs the container check: a throwaway
console project that calls `AddApplicationServices()`, `AddInfrastructureRuntime()` and `AddBotApplication()`,
registers a stub for every external storage port, builds the provider with `ValidateOnBuild`, and resolves every
registration (factory lambdas included). It must finish without a cycle or a missing registration.

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow.

See [`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md) for runtime configuration and data directories.

See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker.

## Architecture

```text
Basil.Domain                     business model and its own validity          -> (nothing)

Basil.Application.Storage.Contracts       persistent ports, paging and query records -> Domain
Basil.Application.Services.Contracts     runtime objects, registry contracts,       -> Domain, Storage.Contracts
                                          scopes, contexts, events and service ports
Basil.Application.Services.Implementations registries, factories and services       -> Services.Contracts,
                                                                                     Storage.Contracts

Basil.Infrastructure.Storage     persistent ports: PostgreSQL, files,           -> Storage.Contracts
                                 migrations
Basil.Infrastructure.Services    capability ports: osu! rulesets, FFMpegCore,   -> Services.Contracts
                                 beatmap mirror
Basil.Infrastructure.Runtime     event pumps and handlers, background loops,    -> Services.Contracts
                                 startup work                                     (no Infrastructure project
                                                                                   references any Implementations)
Basil.Protocol.Bancho            osu! client-server wire formats, score         -> external libraries only
                                 submission cipher (BouncyCastle)
Basil.Protocol.Irc               IRC wire format                                -> (nothing)
Basil.Host.Bancho / .Irc / .Api  transports                                     -> Services.Contracts, the two
                                                                                   Implementations, Infrastructure,
                                                                                   their Protocol
Basil.Host                       entry point, composition, logging,             -> everything
                                 diagnostics, LAN advertising

Basil.Bot.Application            BasilBot: chat commands and their replies,     -> Services.Contracts
                                 an entry point of the server (not a client)
```

`Basil.Protocol.Bancho` wraps the communication between the osu! client and the server, as an API wrapper
wraps a remote API: it turns what the client sends (bancho packets, the delimited strings of the web
endpoints such as the score submission, the client hash, the client build) into C# models of primitives and
back, including undoing the client's own encoding (the score submission's Rijndael cipher). It does not
validate and gives no business meaning; Domain owns meaning and validity, Application the checks. If a wire
format changes, only Protocol changes. `Basil.Protocol.Irc` does the same for IRC. Protocol is not a peer of
Domain: only the transport hosts reference it, and it may use external libraries for the wire work.

Projects live under `src/` in folders that mirror the solution folders under `/Sources/`, each named by
its short name without `Basil.`: `src/Domain/`, `src/Infrastructure/`, `src/Application/Services/{Contracts,Implementations}/`,
`src/Application/Storage/Contracts/`, `src/Hosts/{Host,Api,Bancho,Irc}/`,
`src/Protocol/{Bancho,Irc}/`. The project file and the namespace keep the full name (`Basil.Domain.csproj`,
`Basil.Domain.*`).

Configuration is bound only in `Basil.Host`. Infrastructure and the transports receive it as `IOptions<T>` of
an options type that the project using it owns; Application reads no configuration.

Application has parallel **Storage** and **Services** parts. Storage.Contracts holds persistent repository and
storage ports, query records and paging. Services.Contracts holds runtime objects, their registries and scoped
behaviour, events and service contracts; Services.Implementations contains the registry and service
implementations. `AddApplicationServices()` registers them. Services.Implementations uses storage through
Storage.Contracts. `Basil.Infrastructure` references no Implementations project; it receives capabilities through
their contracts. The hosts reference Services.Implementations to call `AddApplicationServices()`.

> **Migration status (2026-10-20).** The object-scope redesign is implemented across Domain, Application,
> Infrastructure.Runtime and Basil.Bot.Application: behaviour belongs to the owning runtime object and
> permission-dependent operations use its short-lived `ScopeTo(connection)` view. The hosts and all test
> projects still use old namespaces; solution-wide `dotnet build`, `dotnet test` and `Basil.ArchitectureTests`
> do not run. The operator console (`Basil.Cli`), the bot's own connection, host migration, test migration and
> silent channel filtering for blocks remain unfinished. Verify the migrated projects individually with
> `dotnet build src/Domain`, `dotnet build src/Application/Services/Implementations`,
> `dotnet build src/Infrastructure/Storage`, `dotnet build src/Infrastructure/Services`,
> `dotnet build src/Infrastructure/Runtime` and `dotnet build src/Bot/Application`. The decisions in
> [`plans/object-scope-plan-20261016.md`](plans/object-scope-plan-20261016.md) remain authoritative where they
> agree with this file; older plans are history.

### Application feature folders

Application is sliced by feature, not by kind of type; namespace = project + folder. Storage.Contracts owns
persistent repository and storage ports, query records and paging. Services.Contracts owns runtime objects,
their registries, scoped views and contexts, events, results and the contracts for services that remain.
Services.Implementations contains registry and service implementations.

```text
Common/        PageRequest, Page<T>, Interval<T>                                   (Storage)
Events/        Event, IEventPublisher<T>, IEventHandler<T>                         (Services.Contracts)
Users/         user and restriction repositories, UserQuery; User, ScopedUser,
               ScopedUserRestrictions, ScopedUserRelationships, UserResult
Auth/          credential and login repositories; IAuthService, login and registration inputs
Sessions/      UserSession, Connection tree, ScopedUserSession, UserRegistry,
               ScopedBanchoConnection; ISessionConnectionsService and events
Chat/          ChannelSession tree, IChannelRepository; ScopedChannel, ScopedGeneralChannel,
               GeneralChannelRegistry, IGeneralChannelRegistry, IChannelService, events and results
Multiplayer/   Lobby, Room, their parts and scopes; match, round and score repositories;
               IMatchService, IRoomService, room and lobby events and results
Beatmaps/      beatmap and beatmapset repositories and storage; Beatmapset, ScopedBeatmapset,
               IBeatmapsetService and capability ports
Scores/        score, replay and stats repositories; IScoreService and score events
Anticheat/     IAnticheatService and its events
Content/       content repositories and storage ports
Servers/       Server, ScopedServer, server contexts and server resources
```

Event-handler contracts belong to Services.Contracts; dispatchers and concrete handlers live in
Infrastructure.Runtime or the consumer that owns them, including `Basil.Bot.Application`. Host configuration
(ports, TLS, data paths) belongs to the hosts. BasilBot commands and announcements are server features: a
command is an entry point that uses the typist's scoped objects, and the bot only replies. Query **records**
(the filter a repository applies, with `Parse` for the search syntax) belong to Storage; routes and handlers
that answer queries do not. Server settings changed at runtime (`ServerSettings`) are persistent Domain data,
not host configuration.

* `Common` and `Events` depend on no feature. `Users`, `Beatmaps` and `Scores` do not depend on
  `Multiplayer` services; the score service only reads the player's room to find the round. Anticheat flags,
  wherever they arrive (score submission, `lastfm.php`), are reported by the host to `IAnticheatService`, the single
  source of anticheat events; BasilBot turns them into chat warnings.
* `Sessions`, `Chat` and `Multiplayer` reference each other (`Room.Host`, `Room.Channel`,
  `ChannelSession.Members`); treat them as one cluster. Their runtime objects and registries live in Services;
  persistent repositories remain in Storage.Contracts. Objects change their own state through behaviour, with
  implementation access limited to the owning object and its collaborators. References run from the acted-on
  object to the actor, never back.

### Domain and Application

* **Domain** holds business models that exist beyond runtime (`Match`, `Round`, `MatchSettings`,
  `User`, `Beatmap`, `Login`, `ServerSettings`, …). A model guarantees that **it is valid**: field values
  in range, its own text format, values derived purely from its own data. No runtime-only state, no events.
* **Behaviour belongs to the object that owns it, and is reached through that object.** Every concept with state, invariants and
  rights is an **Application object** (`User`, `UserSession`, `Connection`, `Room`, `RoomSlot`, `Lobby`, the
  channels, `Beatmapset`, and the root `Server`) in Services.Contracts. It owns its state,
  keeps its invariants, and offers its behaviour; its state changes only inside its own behaviour. Its persistent
  value is a Domain record named `XRecord` with the plain value `XData` (`UserRecord`, `BeatmapsetRecord`,
  `RestrictionRecord`, `MatchRecord`); the object wraps it. A part is reached through its owner
  (`room.Slots[3]`), never through an outside object that takes the owner as a parameter.
  **`x.ScopeTo(connection)`** returns `ScopedX`, a short-lived capability for one request or command. It snapshots
  the caller's effective permissions and role on `X` once, and each behaviour checks that snapshot and returns
  `NotAuthorized` or `Restricted` before it takes the object's lock, keeps the invariants, acts and emits the
  event. The caller never passes `connection`, permissions or an actor into a call. **Anything whose actions
  depend on permissions or privileges is scoped; only a system that needs no check is not** (the server's own work:
  Runtime handlers, startup, the bot's announcements, public reads).
  Services remain for queries, persistence, infrastructure and coordination across several objects (`IAuthService`,
  beatmapset import and scan, score submission, anticheat judgement, match reports, leaderboards). Five
  anti-patterns to reject in review: a **God Actor** that gathers every behaviour; **service-centric procedural
  design** (a service takes an object and does its behaviour for it); **permission plumbing** (rights passed
  through many layers and methods); **exposing mutable state** (callers change internals past the invariants);
  **fake scoping** (`ScopeTo` only wraps while behaviour and checks stay in a central service). The shared
  authority primitives (`Access`, effective permissions, hierarchy, "a block yields to a higher right") are a
  library each `ScopeTo` calls, not a layer to pass through. A domain record and its Application object never
  share a name.
* **Storage holds persistence; runtime objects and registries live in Services.** Storage.Contracts holds
  persistent ports, query records and paging. Runtime objects (`Room`, `RoomSlot`, `UserSession`, connections
  and `ChannelSession`s), registry contracts (`IUserRegistry`, `IGeneralChannelRegistry`) and their scoped
  behaviour live in Services.Contracts; registry implementations live in Services.Implementations. Storage
  never decides a business rule, emits an event, calls a service or starts a timer. A state change and its
  consequences belong to the object that owns the state.
* **Contracts exist for the services that remain.** `IXService` in Services.Contracts declares the public
  capabilities of a service and, where applicable, its event stream. This deliberate exception to "no
  interface with one implementation" keeps cross-object coordination and one-run operations independent of
  their implementation. Examples include `IAuthService`, `IBeatmapsetService`, `IScoreService`,
  `IAnticheatService`, `IMatchService` and `ISessionConnectionsService`. Objects such as `Room` and `User` do
  not have service contracts: callers reach their permission-dependent behaviour through `ScopeTo(connection)`.
  `IRoomService` remains for system operations such as `Members.ReleaseAsync`, `Members.RemoveSuspendedAsync`,
  `Members.RemoveRefereeAsync` and `Rounds.RecordScoreAsync`. Implementations are `internal sealed` and
  registered by `AddApplicationServices()`.
* **Object or service?** One question decides it: does the operation have a caller whose rights it must
  check? Then it is behaviour of the object it changes, reached through `ScopeTo(connection)`. An operation that
  has no caller (the server's own work, a handler's reaction, startup) or that coordinates several objects
  (login, import, score submission) is a service.
* **Objects get their collaborators through a context.** An object takes a `…Context` record of ports (clock,
  event sink, repositories, the ports of the objects it works with: `RoomContext`, `UserContext`, `LobbyContext`,
  `ChannelContext`, `SessionContext`, `BeatmapsetContext`, `ServerContext`) when it is created. The context, the
  object's constructor and the event sinks are `internal`; `Services.Implementations` builds them in factory
  lambdas in `DependencyInjection.cs`, because the container only sees public constructors. A service class that
  the container creates by itself needs a `public` constructor even when the class is `internal`. An object never
  reaches a service; a port is implemented by whoever owns the other object (`Lobby` implements `IRoomLobbyPort`).
  Because the factories hide dependencies from `ValidateOnBuild`, verify the container by resolving every
  registration with the external storage ports stubbed (see Commands).
* **A scope is a snapshot: re-check after waiting.** A `ScopedX` is for one request or command and is never kept.
  A behaviour that awaits between its rights check and its change (a lookup, a repository call) checks again,
  inside the object's own scope or lock, what the wait could have changed (the permission, `IsOpen`, a block)
  before it changes anything.
* **System operations are labelled.** A public method on an object that skips the rights check (`Lobby.Unwatch`,
  `Server.SayAsync`, `Server.WhisperAsync`, `Server.OpenChannel`, `IRoomService.Members.ReleaseAsync`) says in its
  XML summary that the server itself calls it and needs no rights. A chat command, an API route or any other entry
  point that acts for a user never calls one; it uses the scope of the user's connection.
* **Capability ports.** A capability with business meaning whose implementation needs external libraries
  or resources is a contract in Contracts that Infrastructure implements (`IBeatmapAnalyser`,
  `IBeatmapsetReader`, `IBeatmapAssets`, `IBeatmapsetMirror`), even when only the hosts use it. Pure
  plumbing (logging, diagnostics, metrics, SSE hub, TLS, mDNS, update checks, OpenAPI, the response
  envelope) gets no contract.
* **Contract names do not reveal the mechanism.** If the work moved from the file system to the network, or
  from one library to another, the name would not change: `ScanAsync`, not `ScanFileAsync`;
  `IBeatmapAssets.OpenAsync`, not `ReadFromDiskAsync`.
* **Registries belong with runtime objects.** A registry holds live runtime objects, so its contract is in
  Services.Contracts (`IUserRegistry`, `IGeneralChannelRegistry`) and its implementation is in
  Services.Implementations. Mutating registry members are `internal` on the contract. Persistent repositories,
  storage ports, query records and paging belong to Storage.Contracts. Other capability ports describe what
  lies outside the process: files, network, external computation and the clock (`TimeProvider`).
* **Outer layers use storage directly.** Infrastructure and the hosts read, list and search storage, and
  write persistent records whose write carries no rule or consequence (an admin renames a user, hides a
  beatmapset), directly through the repository, after the host has checked the caller's permission. Services are only for actions; a service
  never wraps a plain read or write (`ScoreService.GetAsync` forwarding to `IScoreRepository.GetAsync` is
  wrong). Runtime state changes only through the behaviour of the object that owns that state; runtime setters
  are `internal`.
* **Each object enforces its own rules.** An operation keeps the owning object's parts consistent in that same
  operation (a room keeps its channel in step with its players). A consequence that crosses object boundaries
  (a restriction removes memberships in rooms and spectator streams) is announced as an event; Infrastructure
  handlers call the operations owned by those objects.
* **Background work.** A contract declares one run of a background job (`ISessionConnectionsService.CloseIdle()`,
  `IBeatmapsetService.ScanAsync()`); Services implements it; Infrastructure owns the loop, the trigger, the
  period and its configuration. A one-shot timer that is the consequence of an operation (an empty room's
  closing) belongs to the lobby that owns the room and runs on `TimeProvider`. A room's countdown belongs to
  its scoped round behaviour; aborting a round cancels a pending start. BasilBot announces the countdown marks.
* **Derive whatever is derivable.** A property a runtime object co-owns with its domain object
  (truth in Domain) is a forwarding property (`Room.Name => Match.Name`).
* **A user online is one `UserSession`; each place they log in from is a `Connection`.**
  `UserSession` (sealed) represents the user regardless of where they connect and holds what is
  shared (`AwayMessage`, `PmChannel`, the active `Restrictions`) plus its connections as child data, keyed by
  `ConnectionType` (`Bancho`, `Tourney`, `Irc`, `Api`), with typed accessors (`Bancho`, `Irc`, `Tourneys`,
  `Apis`); only `Tourney` and `Api` allow several connections per user, a rule the session service owns.
  `Connection` is an abstract class (`Session`, `Login`, `Token`, `IsOpen`, `Type`) with `BanchoConnection`,
  `TourneyConnection`, `IrcConnection`, `ApiConnection`; each holds only what exists for that kind of client
  (`BanchoConnection.Status`, `BanchoConnection.SpectatorChannel`). Rooms, channels and the lobby
  hold connections, not sessions. Owned parts (a session's `PmChannel`, a connection's
  `SpectatorChannel`) are created and destroyed with their owner; they are not relations.
  Spectating is a relation on the spectated osu! client (`BanchoConnection.Spectators`), changed through
  `host.ScopeTo(by).SpectateAsync()` (what only an osu! client has lives under `BanchoConnection`:
  spectating and its presence status), which keeps the client's spectator channel in step (the first spectator brings the
  host in, the last one out) the way the room service keeps a room's channel; the channel never stands for who is
  spectating.
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
  only *calls* an action. The relation is stored on the object acted on (`ChannelSession.Members`,
  `Room.Slots`), and its short-lived scope exposes the operation (`channel.ScopeTo(by).Join()`,
  `room.ScopeTo(by).Members.JoinAsync(password)`). The event belongs to that object's category
  (`ChannelMemberJoined` is a `ChannelEvent`). The
  session does **not** keep a `Channels` set, does **not** have a `Join(ChannelSession)` method, and nothing
  emits events on its behalf. Otherwise every new joinable kind (channel, room, spectator stream, …) forces a
  new `Join*` method and a new set on the user. "Which channels is X in?" is a query over channels, not state
  on X. Ask "who is acted on?" for every relation, member and event.
* **Parts are reached through the owning object's scope.** `room.ScopeTo(by)` exposes `.Slots`, `.Rounds`,
  `.Settings`, `.Authority` and `.Members`; `users.Of(record).ScopeTo(by)` exposes the user's restrictions,
  relationships and account operations. Channels, sessions, connections, the lobby, beatmapsets and the server
  follow the same pattern. Services stay flat and expose only system operations or coordination that spans
  objects; there are no child service trees for object behaviour.
* **Each service manages its own kind, and reacts to other kinds only through events.** The session service
  opens and closes connections and announces `UserConnectionOpened`/`UserConnectionClosed`; its responsibility
  ends there. Leaving rooms and parting channels when a connection closes is done by Infrastructure handlers
  that receive the event and call the owning service. When a second connection of the same kind arrives
  for a user an owner still holds, the owner **asks the old connection who it is** (`connection.IsOpen`):
  still open, refuse the new one; already closed (cleanup not run yet, or failed), remove the old one as an
  ordinary leave and admit the new one. Cleanup operations must be safe to run again.
* **Peers talk through events; owners call their parts directly.** Two operations are peers when neither
  object owns the other and neither's rule depends on the other (scores and rooms, the anticheat and rooms): the
  acting operation emits its event, and Infrastructure's dispatcher delivers it to the peer's contract
  (`ScoreSubmitted` → `IRoomService.Rounds.RecordScoreAsync`, `AnticheatPlayerFlagged` reaches clients that
  watch it, such as BasilBot). An object calls another directly only for a clear ownership relation or to keep
  a rule that must hold within the same operation: objects use storage ports; a room manages its own channel, a
  session its PM channel and a connection its spectator channel; a room tells the lobby when it empties or fills;
  logging in opens the connection; deleting a user closes their connections at once, a security rule that
  must not wait for a handler.
* **Application emits events; it does not consume them.** Each event stream is read by one Infrastructure
  dispatcher, which fans events out to `IEventHandler<T>` implementations in Infrastructure.Runtime or
  `Basil.Bot.Application`. Storage never emits. One operation emits one event carrying its whole consequence. Events sit
  in a nested category tree (`Event` ← `RoomEvent` ← `RoomSlotEvent` ← `RoomSlotTeamChanged`) so a consumer
  can subscribe to a whole category. Each item of a `Channel<T>` reaches one reader, so Infrastructure runs
  exactly one dispatcher per service stream that fans out to its handlers; nothing else reads `Events`
  directly. Handlers, dispatchers and event → client-notification routing belong to Infrastructure and the
  hosts, never to Application.
* **Handlers are quick; slow work goes to its own queue.** A dispatcher awaits its handlers one after another, so a
  handler that waits (a lookup, a room, the store) holds up every later event of its category. A handler that may
  wait only queues the work on a bounded queue of its own, with a cap per user and a timeout per item, and returns
  (`CommandWorker` of the bot). A dropped item is logged, never silent.
* **A consequence reaches everyone, through every path.** What a change does to the people it concerns it does
  whether they are online or not (a restriction removes an offline referee from the open rooms too), and every
  path that grants the same authority applies the same gate (`ScopedRoom` and `ChannelRules.InRoom` agree on
  `RoomManageAny` and `RoomJoin`).
* **Repository contracts are written per model** as `IXxxRepository` (`IMatchRepository`,
  `IScoreRepository`, …), each declaring the operations Services and the outer layers need. A relation
  between two models that is stored on its own gets its own repository rather than a column on either
  (`IRoundScoreRepository` relates a round to a score and keeps the team it was set for). Verbs:
  `CreateAsync(XData) → X` (the store assigns the identity) with `UpdateAsync(X)` for a record created that way
  (nothing is created under an id the store has not given out), `CreateOrUpdateAsync(X)` only for a record keyed by
  what it is about (credentials by user, a relationship by its two users, stats by user and mode), `GetAsync(id)`,
  `GetByYAsync(y)` for a unique key, `ListAsync(XQuery, PageRequest) → Page<X>` for listing and search,
  `DeleteAsync(X)`. A listing without a page is complete and is never copied to be returned: a list the
  repository holds (an owned list) comes back as it is, through its read-only interface (`IReadOnlyList<X>`); a
  listing computed when asked (filtered or ordered from memory) is an `IEnumerable<X>`, and the caller decides
  whether to materialise it. Soft deletion is a service setting `DeletedAt` and calling
  `UpdateAsync`. `Save` is only for byte storages (`IXxxStorage`: `SaveAsync`, `OpenAsync`,
  `DeleteAsync`, plus `ListAsync` and `RenameAsync` where the content is managed); `OpenAsync` serves the server's
  own reads, never clients, who get stored assets from the asset host. A storage lists with `IEnumerable<X>` (the
  caller decides whether to materialise it). `IBeatmapsetStorage` keeps each set in `Data/Beatmapsets/{id} {Artist} - {Title}/`: `archive.osz`
  exactly as given is the truth about the set (`Beatmapset.Fingerprint` records it), and
  `archive-novideo.osz`, `versions/{md5}.osu`, `backgrounds/`, `audio/` (with `preview_{audio}`), `video/` and
  `storyboard.osb` are made from it when the set is indexed; nothing else is extracted, and reading never makes a file
  (a missing one reads as absent). Three kinds of type, three owners: the storage holds raw content (`BeatmapsetSource`: a
  `BeatmapsetArchive` `.osz` or a `BeatmapsetFolder` of one set's files), `IBeatmapsetReader` understands it
  (`BeatmapsetContent`), and the repositories hold the records (`Beatmapset`, `Beatmap`; created from `BeatmapsetData`
  through `IBeatmapsetRepository.CommitAsync`). Raw content is identified by a `ContentFingerprint`: its `ContentStamp`
  (size, modification time) is known without reading, the MD5 is taken (`BeatmapsetArchive.FingerprintAsync`) only when
  the stamp differs from the one recorded on the set; a `Beatmap` is identified by the MD5 of its `.osu`. One import is
  one beatmapset: `IBeatmapsetService.ImportAsync` takes one `.osz`; a folder exists only when placed by hand and is
  packed into an `.osz` first. Difficulties lie at the top of an archive. There are no collections: a `.zip`, a loose
  `.osu` or any other file placed by hand is not a source and is left as it is, as is an invalid source; an upload
  that is rejected leaves nothing, and its set and beatmaps are stored in one transaction. Two sources of one scan never
  become the same set or share a difficulty (the first found wins). A folder being deleted is
  renamed with `.deleted_` first; anything being written (an upload, a derived file, a set folder before it moves into
  place) lies under a top-level `.incoming_` name, which is never listed or watched, so moves stay on one volume. Every
  name written from untrusted input avoids the names Windows keeps for devices (`CON`, `NUL.txt`, `COM1`, ...): a
  derived file gets a `_` prefix. `IBeatmapsetService.ScanAsync()` runs at startup before clients are served: it checks every entry
  (size and modification time, then the hash), indexes what changed, makes missing files again, and stores every
  record change in one `IBeatmapsetRepository.CommitAsync` transaction; `ScanAsync(entry)` handles each change the
  storage watcher reports. `IBeatmapsetReader` reads an archive into a `BeatmapsetContent` whose files open on demand
  (no `byte[]` copies) and owns the input checks; only the beatmapset service uses it. `IBeatmapAssets` only makes
  derived files at index time (audio previews). `IBeatmapAnalyser.AnalyzeAsync` reads a beatmap's `.osu` once
  and keeps the understood file as long as that `Beatmap` instance lives (again when its hash changes), so changing mods
  reads no file; the Domain holds no such runtime content, and metadata stays in the database. A
  storage of text reads and writes it through `StreamReader`/`StreamWriter` (`IFaqStorage.ReadAsync`,
  `IFaqStorage.CreateOrUpdateAsync`) instead of a raw `Stream`. Extract a shared
  interface only when several repositories genuinely share an operation, and keep the `IXxxRepository`
  contract callers depend on; no parallel generic ports (`ICreatable`) and no generic query system
  (`Query<T>`, `SortOptions`). Key normalization (for example `UserData.NormalizedName`, case-insensitive with a space equal to an underscore) lives on the
  Domain value (`UserData.SafeName`); the server writes the normalized key it looks records up by. A repository filters only by explicit criteria in its query record.
  A query record searched with text (`UserQuery`, `BeatmapQuery`, `MatchQuery`) is `IParsable<T>`: every criterion, a
  state included, is written in the search text (`deleted=1`,
  `hidden=0`, `ended=0`, `private=1`, enum masks as numbers such as `permission=4`), and a state is a `bool?` (`null` for both,
  written `*`) whose default, kept when the text has no token for it, is `null` (both). Every caller narrows a query to the asker
  before using it, `UserQuery.Parse(text).LimitTo(effective)`: `LimitTo` sets each state the asker may not see to
  what a user without that permission sees (`Deleted = false`, `Hidden = false`, `Private = false`). A query used
  without `LimitTo` shows everything, so only the server's own work (a scan, startup recovery) skips it.
* **Memory is the truth; the database is a copy of it.** Memory is where the server works; the database only keeps
  a record of what was done. **No operation waits for the database unless its result comes from the database**
  (a try-operation: an identity the store assigns, a unique value). Everything asks memory first and goes to the
  database only on a miss, to verify or to load into memory; a miss means "not loaded", never "does not exist". A
  change is applied to the live object in memory, and its copy is written to the database alongside. "Cache" means
  only the on-disk `Cache/` directory of derived, rebuildable files (repacked `.osz` archives, audio previews); data
  in memory is never called a cache.
  * **Resident data**, the database records every client uses (`ServerSettings`, general channels, menu banners), is
    loaded at startup and never released; it changes only through its repository, never in the database behind the
    server's back. File storages are not resident and hold no file content in memory: the file system is their
    index, read as it is when asked.
  * **On-demand data:** a repository holds one live instance per identity, returned by every `GetAsync`,
    `GetByYAsync` and `ListAsync`. It lives while anything holds it (a session its user, a room its match) or while
    a change of it is not stored yet, and is then released; there is no TTL. A list owned by an object (a user's
    restrictions and relationships, a set's beatmaps, a match's rounds and events, a round's scores) lives as long as
    its owner instance: it is loaded whole the first time it is needed, then changed item by item.
  * **Writes:** `CreateOrUpdateAsync`/`DeleteAsync` queue a copy of the values **as they are when queued** and return
    at once; only a try-operation waits for the database, because its outcome comes from it. `DatabaseWriter` keeps
    one queue per root (`Root.User`, `Root.Match`, `Root.Beatmapset`, `Root.Server`) and sends its writes one
    statement at a time, in order; a copy not sent yet is replaced by a newer copy of the same identity. At most
    `Basil:Database:WriteConnections` connections (8 by default, see [`plans/evidence/postgresql-direct-writes-20261008`](plans/evidence/postgresql-direct-writes-20261008/results.md)) write at once. No write is grouped with unrelated
    writes; a business operation that must store several rows together gets one try-operation of its own. A score
    belongs to its player (`Root.User`); which round it was played in is a relation of its own
    (`round_scores`), listed with the round.
  * **Errors:** only environment errors (lost connection, timeout, server shutting down, read-only after a failover,
    deadlock, authentication, permissions) are retried, before any later write of the same root. Any other error
    fails that write alone: a try-operation throws it to its caller, a copy is logged as an error. A try-operation
    runs in its own transaction and settles a broken commit with `pg_xact_status`; its cancellation token counts
    only while it waits for its turn. Appends (logins, match events) carry no business id: the database assigns it,
    the repository queues the append and returns at once (logging in works without a database), and
    `DatabaseWriter.AppendAsync` runs each append in its own transaction settled the same way, so it is stored
    exactly once.
  * **Reads** go straight to PostgreSQL on a pooled connection of their own and see only what is committed. A listing
    asks the database which rows match and returns the live instance of each; a row whose deletion is still queued
    never comes back into memory.
  * **A value unique across a table** (a user's safe name, a beatmap's hash or osu! id, a score's checksum, an id the
    store assigns): when memory holds a record with that value the repository rejects the change at once; otherwise
    the insert runs as a try-operation and a unique violation from the database means the value is taken. Either way
    the repository throws `AlreadyExistsException` (a rename returns `false`) and does not load the existing record.
  * A derived read model (the match report) is projected from memory when asked; it is not stored or rebuilt.
  * A child refers to a parent of another root only once the parent is committed (such a parent is created by an
    awaited try-operation); parents are created before their children. One database belongs to one Basil process. An
    operation's lambda uses only its connection and transaction and never queues another operation.
  * Without a database, creating identities and checking unique values wait for it; everything already in memory
    keeps working.
* **Naming: Domain model `X`, runtime model `XSession`, the object that holds the live sessions
  `XRegistry`, the contract `IXService` and its implementation `XService`.** No suffixes such as
  "Definition". `Channel` → `ChannelSession` → `GeneralChannelRegistry` (it holds only general channels);
  `User` → `UserSession` → `UserRegistry`. The osu! terms win for matches: `Match` → `Room` → `Lobby`. The
  room's channel is named after the room (Domain `RoomChannel`, runtime `RoomChannelSession`) because the
  room is where things happen and `Match` is only the record. `Channel` shares its name with
  `System.Threading.Channels.Channel<T>` on purpose; the two differ by generic arity.
* **Event names are `{Group}{Subject}{PastTenseVerb}`.** The group is the category the event belongs
  to (`Room`, `Channel`, `User`, `Lobby`); when the subject is the group itself it is written once
  (`ChannelOpened`, `UserRestricted`). A subject that belongs to another is written `{Parent}{Child}`
  (`RoomRoundPlayerLoaded`, `RoomSlotTeamChanged`, `UserConnectionOpened`). The abstract category
  records follow the same rule (`RoomRoundEvent`, `ChannelMembershipEvent`).
* **Chat channels.** Domain: abstract `Channel` (`Name`, `Topic`, IRC convention) with
  `GeneralChannel` (configured chat; alone carries `ReadPrivileges`, `WritePrivileges`, `AutoJoin`,
  `Visible`), `RoomChannel`, `SpectatorChannel`, `PmChannel`; messages are the record `Message`.
  Runtime: abstract `ChannelSession` with `GeneralChannelSession`, `RoomChannelSession`,
  `SpectatorChannelSession`, `PmChannelSession`. **Each channel lives on the record of its owner**:
  `GeneralChannelRegistry` holds the general channels, a `Room` its room channel, a `UserSession` its PM
  channel, a `BanchoConnection` its spectator channel. The channel service decides, per kind of channel, who
  may read and write. A private message is a post into the recipient's PM channel. Access follows osu!: a
  user may join a channel (including `/join` over IRC) only if an osu! client doing the matching in-game
  action would have access. General channels are created, configured and deleted at runtime through
  `Server` (`ServerConfigure`); configuring a channel removes the members who may no longer read it.
* **Assets are referred to by `ContentReference`** (Domain), never by `string` or `Uri`: either the relative path of a
  file the server stores under its data directory (`Menu/Banners/x.png`, valid on every machine) or an absolute
  http/https address of an asset hosted elsewhere, kept as given and never copied. The value is canonical (its
  parsing and normalization live on the type). File storages give out the reference of what they store; the hosts
  serve stored files under a fixed public path per asset directory (static file provider rooted at each asset
  directory, never at the whole data directory) and only distribute them. Replays are named
  `{ScoreId} {Player} - {Artist} - {Title} [{Version}] ({yyyy-MM-dd}) {Mode}.osr`, as osu! names an exported
  replay; screenshots get a UUIDv7 name.
* **Permissions are the source of truth.** What an account may do is its `Permissions` (Domain, on
  `UserData`): one flat enum of independent actions named `[Resource][Action]` (`RoomCreate`, `ChatPost`,
  `UserRestrict`, `ServerConfigure`, …), split into `#region`s by resource. The enum has **no group members**.
  A larger permission still contains the bits of the smaller ones it implies, because it is still *one
  permission* ("whoever holds this can also do what is below it"): a manage permission includes the matching view
  permission (`BeatmapsetManage` holds `BeatmapsetViewHidden`, `RoomManageAny` holds `RoomViewPrivate`,
  `UserRestrict` holds `UserSilence`). A group says only "these permissions belong together" and lives in the
  static class `PermissionsGroups` beside the enum, one mask per area (`Chat`, `Multiplayer`, `Spectating`,
  `Beatmapsets`, `Users`, `Server`), each with the permission that manages the area. The same pattern, `{Enum}Groups`,
  holds the combinations of every other flags enum (`GameModsGroups`, `BanchoFlagsGroups`); no flags enum has
  combination members. A check asks for the smallest permission that allows the action, so the manager and a user
  granted only that permission both pass; never `Allows(A) || Allows(B)` for a pair where one contains the other.
  Do not add a permission that is merely a consequence of another (playing and submitting scores are one:
  `ScoreSubmit`).
* **Privileges are roles, permissions are rights.** `Privileges` (Domain; renamed from `BanchoPrivileges`) is one
  flags enum of the roles osu! knows (`Player`, `Moderator`, `Supporter`, `Owner`, `Developer`, `Tournament`; the
  values stay the wire bits). `privilege.Permissions` (an extension) lists the permissions that belong directly to
  that role, at depth one. A user has the bit of a role when their **effective** permissions hold at least one of
  those. A requirement written in privileges (a channel's `ReadPrivileges`/`WritePrivileges`) is met when the user
  has every bit asked for. Privileges are derived, never stored or configured, and they grant nothing: a client
  that claims a role it holds no permission for is refused.
* **Permission checks require every bit** (`Permissions.Allows`); an empty requirement is always
  satisfied. Checks use the **effective** permissions: the granted ones minus those suspended by the user's
  active restrictions (`Effective(restrictions, now)`).
* **Restrictions are timed records,** not account fields. A `RestrictionRecord` suspends a mask of permissions
  from `StartsAt` to `EndsAt` (open-ended until lifted; lifting sets `EndsAt`, history is kept). It holds only that
  data: no reason and no note. Named masks live on `Restriction`, not on `Permissions`:
  `Silence = Chat | Multiplayer` follows the osu! wiki (no chatting anywhere, no multiplayer; watching others play
  stays allowed) and `Restrict = Silence | Spectating | ScoreSubmit | BeatmapsetUpload | ScreenshotUpload`. There is
  one restrict operation, taking a mask. **Whoever manages an area cannot be restricted in that area:** the
  restriction is stored, but the effective permissions keep every bit of the areas the user manages (a
  `RoomManageAny` holder who is silenced still plays and manages multiplayer, but cannot chat unless they also
  manage chat). Expiry needs no timer: the effective permissions are computed at `now`. A result tells "suspended by
  a restriction" (`Restricted`) apart from "never granted" (`NotAuthorized`).
* **A block yields to a higher right.** A limit a user sets on others (a block, friends-only messages) does not
  bind someone who manages the matching area (`ChatMessageAnyone` for messages, `RoomManageAny` for invitations,
  `RoomObserve` for spectating). Every "prevent, unless a higher right" rule follows this pattern.
* **Moderation history is a log.** Restricting, lifting, adding a note and viewing a user's login history append an
  entry (action, `by`, target, reason, `Timestamp`) to the moderation log. The log is for people to trace; it
  never affects any right.
* **Hierarchy.** A staff action aimed at another user (restrict, lift, set permissions, set password,
  revoke sessions, delete) requires the actor's granted permissions to be a strict superset of the target's.
  Equals cannot act on each other; acting on oneself is not subject to this rule, except that nobody restricts
  or lifts a restriction of themselves. Granting gives only bits the actor holds.
* **BasilBot is the server's voice.** The server has one fixed user, BasilBot, with id 1, created by a migration:
  it is a friend of everybody, cannot sign in, and holds no permission. Its record is fixed (it cannot be
  deleted, restricted, given permissions or a password, or have its connection revoked); its metadata (name,
  country, avatar) can be edited like any other user's. The bot answers chat commands (`!`, a fixed prefix) and
  posts the server's announcements. A chat command is an entry point like IRC or the API: it runs on the objects
  scoped to the connection of the user who typed it, so it needs that user's rights; the bot only replies. The
  bot's own messages go straight to the chat, past the rights check. The command handler and event-based
  announcers are implemented in `Basil.Bot.Application`. The bot is online with a connection of its own (`BotConnection`,
  brought up by the server at startup; nobody can sign in as it) and a private message to it is a command. Showing the
  bot as online on the transports is planned for the Host phase. Countdowns are a feature of the room.
* **Identity on every action.** Every action has a real actor `by` (a `Connection`); `by` is never `null`.
  A room created through the API has the caller as creator. A server announcement is `by` BasilBot.
* **There is no delegation.** No account acts for another. An outside tool signs in with its own account and the
  rights that account holds.
* **Accounts come only from registration, creation or the console.** Nobody is granted permissions for being first
  or for anything else implicit; a new account gets the default permissions, and more only by being granted.
  Anyone without an account may register one unless `ServerSettings.LockedCreation` locks it. The server creates
  no account by itself: the first administrator is made by the operator with a console command (below).
* **The console is the operator's entry point.** *(Planned for the Host phase, in a new project `Basil.Cli`; not
  built yet.)* The server process offers an interactive console, like a Minecraft server, where the operator types
  commands. A console command acts as BasilBot (by its id, `BasilBot.Id`) and is **not subject to rights checks**: it is
  the one entry point that skips them, because whoever sits at the console owns the machine. Creating an account
  (and so the first administrator, with every permission) is a console command. The `--noconsole` flag turns the
  console off. The CLI is built on the `XenoAtom.*` libraries (not Spectre.Console). What the commands are and how
  they reach the objects is decided with the Host.
* **A name is unique among live accounts.** Deleting an account is a soft delete and is **permanent**: the account
  is never restored, and its id is never reused. The name is unique only among accounts that are not deleted, after
  normalization (case-insensitive; a space and an underscore are the same). `UserData.NormalizedName` does the
  normalization and the database enforces the same rule with a partial unique expression index. A new account may
  take the name of a deleted one.
* **No third party in the core.** Apart from the beatmap mirror, the server uses no third-party service for core
  work. Country at registration comes from the host (a proxy header or an offline GeoLite2 file); Domain and
  Application receive the `Country` as an input and do not know where it came from.
* **Creation locks.** `ServerSettings.LockedCreation` locks the creation of accounts, rooms and beatmapsets to
  their managers (`UserManage`, `RoomManageAny`, `BeatmapsetManage`), on every
  transport. Beatmapset uploads are locked by default. Importing a beatmapset has no actor in Application: the
  host gates it by policy (the lock, `BeatmapsetUpload`, `BeatmapsetManage`). There is no
  administrator key.
* **Sessions and tokens.** Every client signs in with credentials and gets a `Connection` with an opaque
  `Token` (in memory, lost on restart, no absolute expiry). Idle connections close after 300 s for osu!,
  osu!tourney and IRC and after 2 hours for `Api`. Logging out, changing the password, deleting the user, a
  permission change that removes access to a kind of client, and revoking close connections. osu! and IRC
  allow one connection per user; osu!tourney and `Api` allow several. `Api` connections do not make the user
  appear online in game.
* **No authority by connection kind.** Never decide authority with `ConnectionType` or `is XConnection`. The
  kind of client only decides what that client can technically do (only an osu! client takes a slot; an
  osu!tourney client gets no private-message channel). The kind `Bot` is BasilBot's own connection, opened by the
  server at startup so that the bot has a session and a private-message channel; no client can request it or
  sign in as the bot, it holds no permission, and idle sweeps never close it. Transports skip it when they
  deliver a message.
* **A channel's name tells its kind.** A channel several users take part in (general, room,
  spectator) is named `#name` (`#osu`, `#lobby`, `#mp_5`, `#spec_7`); a private-message channel is
  named after its owner, without `#`, as an IRC nick is. This is a naming convention each kind of
  channel follows when it names itself; nothing else checks it. `#multiplayer`/`#spectator` are
  aliases the transport resolves to the sender's room or spectator channel.
* **Setters only assign and validate.** A Domain setter stores the value and throws if it is invalid; a
  runtime setter is `internal set` and only assigns. A setter never emits an event or changes anything else.
  A change that has consequences (emits an event, changes other fields, checks authority, groups several
  fields into one event) is an operation on the object that owns the state. Such an operation is real behaviour, not a
  `Change*`/`Rename`/`With*` wrapper around a single field.
* **Name a contract after the object it acts on.** The unit that is imported, stored, read and mirrored is
  the beatmapset, so the contracts are `IBeatmapsetService`, `IBeatmapsetReader`, `IBeatmapsetStorage`,
  `IBeatmapsetMirror` and `BeatmapsetImportResult`. A byte storage is `I{Owner}{Thing}Storage`
  (`IUserAvatarStorage`, `IMenuBannerStorage`, `IMenuSeasonalsStorage`); medium words (`Image`,
  `Background`, `Archive`) and redundant qualifiers (`Server` in `ISettingsRepository`) stay out of names.
* **Knowledge about a Domain value lives on that value.** A fixed set of flags is a member of its `{Enum}Groups`
  class (`BanchoFlagsGroups.CheatSigns`, `PermissionsGroups.Chat`) or of the owning type (`Restriction.Silence`),
  never a combination member of the enum itself; a per-mode factory is a static member of its type
  (`BeatmapObjects.NewFrom(mode)`);
  services use them instead of keeping their own copies.
* **One concept, one name.** Use the same name for the same concept in every model, parameter,
  event and API: the moment something happened is `Timestamp`, a span is `StartedAt`/`EndedAt`, a
  future window is `StartsAt`/`EndsAt`, a stored record's lifecycle is
  `CreatedAt`/`UpdatedAt`/`DeletedAt`, the caller of an operation is `by`. Do not introduce
  `OccurredAt`, `When`, `Since`, `LoginTime` or similar synonyms.

### Code style

* A small type used by one type lives in that type's file (`ConnectionType` in `Connection.cs`).
* Object-oriented, not procedural: an operation goes through the object that owns the state it changes
  (`DataStorage.SaveAsync`, `RoomState.IsManagedBy`), not through a static helper that is handed the state.
* A type that is a collection implements the matching BCL interface (`DataStorage : IReadOnlyDictionary<string,
  FileInfo>`); a business value with a text form is a value type implementing `IParsable<T>` and `IFormattable`
  (`ContentReference`).
* **Collections are exposed, not copied** (most mistakes so far were in registries and storages):
  * Never materialise a collection only to hand it out (`[.. _channels.Values]`, `.ToList()`, `.ToArray()` on a
    return). Return the collection itself through an interface it implements (`IReadOnlyDictionary<string,
    GeneralChannelSession> All => _channels`), or an `IEnumerable<T>` for a sequence computed on the fly. Copy only
    where the code needs a snapshot (to change the collection while walking it, or to read it outside the lock that
    guards it, as `FileBeatmapsetStorage.ListAsync` does). `ConcurrentDictionary.Values` and `.Keys` are such a copy
    (taken under every lock), so a registry exposes the dictionary, not its `Values`.
  * A collection that is both read and changed is exposed twice, with no methods that only forward to it: a public
    read-only interface and an `internal` read-write one named `Mutable{X}` (`public IReadOnlySet<User> Banned`,
    `internal ISet<User> MutableBanned`; `IDictionary`/`IList` likewise). A method stays only when it carries a rule
    of its own (`Lobby.Add` releases the reserved id; `UserSession.Add` files a connection by its kind).
  * Expose the most derived interface the value has (`IReadOnlyDictionary`, `IReadOnlySet`, `IReadOnlyCollection`
    rather than `IEnumerable`), unless the extra property belongs to the implementation and not to the contract: a
    storage that walks files returns `IEnumerable<T>`, not `IOrderedEnumerable<T>`, even when it sorts them.
* Every stored time is UTC (`DateTimeOffset.UtcNow`, `TimeProvider.GetUtcNow()`, `timestamptz` columns); only the
  presentation converts it to a local date and time. Never `DateTimeOffset.Now` or `DateTime.Now`.
* A folder that would share its name with a type in it is plural (`Multiplayer/Matches/Match.cs`), so code never
  writes `Match.Match`.
* A member that uses no instance state is `static` (`LobbyService.RoomOccupied`).
* Prefer primary constructors, property patterns (`room is { InProgress: false, Beatmap: not null }`) and
  one guard for conditions that return the same result; no redundant casts, `? true : false` or
  discarded-task lambdas (`_ => action()`, not `_ => _ = action()`).
* Services.Implementations registers its services and registries in `DependencyInjection.AddApplicationServices()`.

### Important invariants

#### Multiplayer concurrency

`Room` is mutable shared state.

Operations that read and then mutate a room must hold the room's exclusive scope across the complete state transition. That scope comes from `room.EnterAsync()`, which returns `null` once the room is closed; the room owns the lock, like a database transaction.

Do not introduce a second synchronization mechanism for the same state.

Do not hold the room scope across long-lived or unrelated waits.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) for the complete model.

#### Authority

Referee, host, and creator are separate concepts.

Whoever creates a room is its creator, however it was created (in game, or through the API, directly or
by a client acting for the user); every room has a creator. Creating a room needs `RoomCreate`. The
creator is remembered on the match and ranks above referees; it is not added to the referee list. A creator
who created the room in game is also its first host. When a tournament room is created, a creator whose
game client is online and not in any room is seated in the new room as host; a creator already in another
room stays there and does not become host. Managing a room is for the creator and the referees (and
`RoomManageAny`, which acts with the creator's authority in every room); being the host does not
grant management rights. A creator or referee whose permissions are all suspended cannot manage. Every room
operation with a permission rule runs on `room.ScopeTo(by)`, which checks authority, not the transport. BasilBot's `!mp` commands call these operations using the typist's connection.

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

Basil is a backend platform: it offers capabilities through its protocols and its user-centric HTTP API, with
BasilBot as a built-in chat entry point. Other features are built on top as clients. It is deliberately smaller
than bancho.py.

Do not implement a bancho.py feature simply because it exists upstream. Chat commands, announcements and room
countdowns belong to BasilBot and the room; overlays remain client features.

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
   a repository read, a registry used through its implementation instead of its contract, a contract name that
   reveals its mechanism, or a reference from Infrastructure to Services.Implementations. Since the identity redesign, also
   reject: an action without a real `by`, authority decided by connection kind, a check on granted instead of
   effective permissions, a stored or configured `Privileges`, privileges used to grant anything, and a
   delegation of any kind. Since the 2026-10-16 redesign, also reject the five anti-patterns of "Behaviour belongs
   to the object that owns it" (God Actor, service-centric procedural design, permission plumbing, exposed mutable
   state, fake scoping), and a bot message that goes through a rights check or a bot command that runs with the
   bot's rights instead of the typist's.
8. Review delegated work for what agents tend to leave behind, besides the rules above: `.ConfigureAwait(false)` in
   new code, a changed final newline or reflowed lines nobody asked for, a renamed parameter outside the task,
   stray tabs in XML documentation, a constructor made `internal` on a class the container creates, a member that
   nothing calls any more, a wait between a rights check and the change it guards, a handler that waits, and a
   consequence that skips offline users or one of several paths. Run parallel agents in separate git worktrees
   (two at a time at most) and merge their commits yourself.

The goal is not merely to produce compiling code. The goal is a verified change that respects Basil's architecture, scope, contracts, and documentation.
