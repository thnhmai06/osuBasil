# AGENTS.md

This file provides guidance to coding agents working in this repository. `CLAUDE.md` only imports
it.

## What this is

Basil is a lightweight, high-performance [osu!](https://osu.ppy.sh/) (stable) backend platform specialized for
multiplayer. The server provides the core capabilities (accounts and sessions, privileges, rooms, chat, scores,
beatmaps, events); specific features such as tournament tooling and overlays are clients built on top
of it, each signing in as an ordinary user. The server works the same with or without any of them.

It is built on [bancho.py](https://github.com/osuAkatsuki/bancho.py), but it is **not a full bancho.py port**. Basil deliberately has a smaller feature surface. clans, osu-web v1/v2 API compatibility, and other unrelated features are intentionally out of scope. Chat commands, announcements and room countdowns are in scope: they are BasilBot, a feature of the server.

The identity, privileges and silence rules are in [`docs/for-agents/identity-privileges.md`](docs/for-agents/identity-privileges.md); read
them before touching users, sessions, privileges, rooms or chat.

Before porting or recreating anything from bancho.py, read:

* [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md) — authoritative feature scope
* [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) — system structure and dependency direction
* [`README.md`](README.md) — project overview

The generated references are served by a running instance on its `api.` host and published on [GitHub Pages](https://thnhmai06.github.io/osuBasil/): the site at `/docs/` lists the HTTP API (`/docs/api/`, Scalar), the IRC guide (`/docs/irc/`) and the BasilBot guide (`/docs/bot/`), and the GitHub Pages site has the same layout. Each part of the server writes its own reference (BasilBot from its command catalog, the IRC host from its commands). The endpoints the osu! (stable) client calls have no generated reference; [`docs/for-developers/osu-stable.md`](docs/for-developers/osu-stable.md) describes them. Do not create a second hand-written reference in Markdown.

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
.WithSummary("Get a match report.")
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
* Do not duplicate the generated references (HTTP API, BasilBot commands, IRC).

Authoritative topic ownership is defined by [`docs/index.md`](docs/index.md).

If a topic already has an authoritative document, update that document instead of creating a competing description elsewhere.

### 8. Test writing

**Tests pin observable contracts, not implementation details.**

Apply the Implementation Test:

> If the implementation changed but the observable behavior stayed the same, would this test still pass?

If not, the test is probably testing implementation.

Contract examples include:

* osu-stable packet bytes
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

Tests are what verify the software for the owner, who reads the code only for patterns and architecture. Therefore:

* **Specification first.** An expected value comes from AGENTS.md, the osu! rules, the protocol or bancho.py behaviour,
  never from running the code under test and pasting its output. A red test means the code is wrong: report it, do not
  edit the test until it is green.
* **Blackbox and whitebox.** Derive cases from the contract (equivalence classes, boundaries, decision tables, state
  transitions) and then from the structure (every branch and guard); coverage is used only to find branches no case
  reached.
* **Pin properties of computed values, not the number** (star rating, difficulty, length: valid, finite, monotone under a
  modifier). Pin the exact value only when the value is itself a contract (wire bytes, accuracy and grade of the
  ruleset, the score submission checksum) with hand-written input and output pairs, never a copy of the formula.
* **Layout mirrors the sources.** One test project per source project (`tests/` tree = the project folders, `Basil.X.Tests`),
  folders inside mirror the feature folders; shared fixtures live in `tests/Support/`; sample content in
  `tests/Assets`.
* **A refusal test checks the outcome `Denied`** of the object's outcome enum (a refusal does not say which privileges
  are lacking), and every privilege requirement has a test that goes red when it is removed: a caller without the
  privilege is `Denied`, a caller with it is not.

### 9. User-visible reply strings

User-visible chat text is public behavior.

Each chat surface owns its replies. BasilBot and IRC declare each reply as a member of a reply enum with its default
wording (`BotReply`, `IrcReply`; `MpReplies` maps a `!mp` outcome onto a `BotReply` member), read through
`IReplies<TReply>` from `Data/Localizations/<surface>.json`, so an operator can reword it. The osu! client's texts are
constants (`StableReplies`, `OsuWebReplies`).

Do not scatter user-visible strings across handlers.

Changing the wording of a user-visible response is a contract change. Update the named enum member or constant and its tests deliberately.

Internal diagnostics and logs are not subject to this rule.

## Commands

Restore and build:

```bash
dotnet restore
dotnet build --configuration Release
```

Run Basil locally:

```bash
dotnet run --project Basil
```

The repository has no test projects at present; `Basil.slnx` lists none. When test projects exist, run each one on its
own and never pass the solution to `dotnet test`. The test tree mirrors the project folders, so a test project for
`tests/<Layer>/<Module>` is run as:

```bash
dotnet test tests/<Layer>/<Module> -c Release --nologo -v q
```

Run a specific test class:

```bash
dotnet test tests/<Layer>/<Module> \
  --filter "FullyQualifiedName~<TestClassName>"
```

Verify a change with the whole solution in Release, then run the affected test projects one at a time:

```bash
dotnet build Basil.slnx -c Release --nologo -v q
dotnet test tests/<Layer>/<Module> -c Release --no-build --nologo -v q
```

Projects that need PostgreSQL start a container; set `DOCKER_HOST` when the daemon is not on the default socket, or set
`BASIL_TEST_POSTGRES` to use an existing server. Never run every project in one `dotnet test` invocation.

A change to a constructor, a context or a `DependencyInjection.cs` also needs the container check: call `AddBasil()` of the
composition root (`Basil`), build the provider with `ValidateOnBuild`, resolve every registration (factory lambdas included)
and check that every event handler is registered, against a deadline, because a dependency cycle shows up as a hang, not as
an error. No test in the repository runs this check yet; the change is checked in review.

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow.

See [`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md) for runtime configuration and data directories.

See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker.

## Architecture

> **Migration status.** This section describes the target architecture. The code is migrated phase by phase; a part that is
> not migrated yet is marked **(target)**. [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) owns
> the full description; [`docs/for-developers/migration-status.md`](docs/for-developers/migration-status.md) owns the status.

```text
Basil.Shared                     primitives with no business meaning (hashes,    -> (nothing)
                                 concurrent sets, path validation, string and
                                 enum helpers)
Basil.Domain                     the system at rest: durable models and the      -> Shared
                                 rules of one object on its own (its state,
                                 invariants and transitions)

Basil.Application.Shared         shared contracts, events, Mediator        -> Domain, Mediator.Abstractions
                                 messages
Basil.Infrastructure.Shared      shared database, files and event dispatch       -> Application.Shared, Domain

Basil.Application.<Module>       the module's contracts (services, ports,        -> Domain, Application.Shared,
                                 repositories, storages) and its runtime objects:   the Application projects of the
                                 Objects/, Registry/, Capabilities/ (Scoped*        modules it uses (below)
                                 only), Ports/, Persistence/, Storage/,
                                 Services/ (contracts); Add<Module>Application()
Basil.Infrastructure.<Module>    the implementations of those contracts: Handlers/ -> its own Application,
                                 (Mediator listeners), Logic/, Services/,            Infrastructure.Shared,
                                 Persistence/, Storage/, Adapters/, Workers/         Application.Shared
                                 Add<Module>Infrastructure() is its only public
                                 surface
Basil.Infrastructure.Persistence    Npgsql design-time factories and all           -> Infrastructure.Shared, the
                                 PostgreSQL migrations;                             module Infrastructure projects
                                 only Basil references it
Basil.Protocol.Game.Stable       the translation language of the osu! wire,      -> Domain, external libraries
                                 score submission cipher (BouncyCastle)
Basil.Protocol.Irc               the translation language of IRC                 -> (nothing)
Basil.Gateway                    the guard: blocks and rate-limits each request  -> the rate-limiting library,
                                 (Allow/Deny from rate, concurrency, load), per     the Application services and
                                 object through Application services/capabilities   capabilities it guards with
Basil.Host.Shared                views and outcome-to-error mapping              -> Application projects
Basil.Host.Stable / .Irc /       transports: translate the wire to messages and  -> Host.Shared, Protocol, Gateway,
.Rest / .Cli                     send them through Mediator                         Application projects
Basil                            entry point, composition    -> everything
                                 root, "the way to run"

Basil.Application.Bot            BasilBot, a module at the top of the graph:     -> the Application projects of the
Basil.Infrastructure.Bot         contracts and objects / handlers, commands,        modules it uses, Application.Shared
                                 workers
```

The layers stay Clean Architecture (Domain, Application, Infrastructure, hosts); inside the Application and Infrastructure layers
the code is split by module, one project per module and layer (`Basil.Application.<Module>`,
`Basil.Infrastructure.<Module>`; the solution folders `/Sources/Application/` and `/Sources/Infrastructure/` list them).
References only point down:

```text
Rulesets    -> Shared
Beatmaps    -> Shared, Rulesets
Content     -> Shared
Users       -> Shared, Content
Sessions    -> Users
Anticheat   -> Users, Sessions
Scores      -> Users, Sessions, Beatmaps, Rulesets
Multiplayer -> Users, Sessions, Beatmaps, Content, Scores
Chat        -> Users, Sessions, Multiplayer
Diagnostics -> Users, Sessions
Servers     -> every module above
Bot         -> Anticheat, Beatmaps, Chat, Multiplayer, Scores, Servers, Sessions, Users
```

A module Infrastructure project never references another module; the foreign keys between the module databases
decide the order in which `AddBasil()` registers the migrations (Users, Sessions, Rulesets (no tables), Beatmaps, Content,
Shared, Scores, Multiplayer, Chat), not these references. The graph and the host rules are checked in review; no test in
the repository enforces them yet.

`Basil.Protocol.Game.Stable` wraps the communication between the osu! client and the server, as an API wrapper
wraps a remote API: it turns what the client sends (osu-stable packets, the delimited strings of the web
endpoints such as the score submission, the client hash, the client build) into C# models of primitives and
back, including undoing the client's own encoding (the score submission's Rijndael cipher). What the client
sends and receives is a DTO and belongs to Protocol (`RoomPacket`, `LoginRequest`, `ScoreSubmission`; their
enum-like fields stay primitives). What the messages mean belongs to Domain, and Protocol builds on it: the game
enums (`GameMode`, `GameMods`, `UserActivity`, `BeatmapsetStatus`, `GameTeamType`, `GameWinCondition`,
`UserPrivileges`, `OsuVersionStream`) carry the client's numbering, so the host casts a primitive to them and back
with no table, and the spectated play (`SpectatorBundle`, `ReplayFrame`, `ScoreFrame`, `SpectatorAction`) and the client version
(`OsuVersion`, whose text form `OsuVersionExtensions` parses and writes) are Domain values that Protocol reads and writes. Protocol does not
validate and gives no business meaning: a number the client sends is checked by the host with `WireMap.Known` and
`WireMap.Mods` before Application sees it. What has no Domain twin (a slot's neutral team, the open/locked/quit slot
states) stays a Protocol enum, mapped by the host. If a wire format changes, only Protocol changes; a Domain enum
value is never renumbered for a client. `Basil.Protocol.Irc` wraps IRC the same way and references nothing. Only the
transport hosts reference Protocol; Application and Infrastructure never do. Protocol is the language the hosts use to
translate the wire into the system's own messages and back; it does not validate and coordinates nothing.

Every project is a folder at the repository root named after its project: `Basil.Domain/`,
`Basil.Application.<Module>/`, `Basil.Infrastructure.<Module>/`, `Basil.Host.{Shared,Stable,Irc,Rest,Cli}/`,
`Basil.Protocol.{Game.Stable,Irc}/`. The project file and the namespace keep the full name
(`Basil.Domain.csproj`, `Basil.Domain.*`, `Basil.Application.Users.Persistence`). Tests mirror the same tree under
`tests/` (a test project for `Basil.Application.Users` goes in `tests/Application/Users/`, named
`Basil.Application.Users.Tests.csproj`; shared fixtures go under `tests/Support/`, and tests that run several hosts go under
`tests/Hosts/`). The repository has no test projects yet, so these are the places new tests belong.

The project that defines an options type binds it (`Add<Module>...(IConfiguration)`), keeping every key path; the
composition root only chooses what to run (Kestrel, ports, logging, lifecycle) and binds only the options it defines
(server, logging, telemetry, updates). Application reads no configuration.

`Application.Shared` owns `IActor`, `ScopedPrivileges`, shared query values and event contracts/queue; it depends
on Domain and external packages. `Infrastructure.Shared` owns shared database, memory and file implementations and the
event dispatcher; it depends on Domain, Application.Shared and its external packages.

Inside a module Application project each folder owns one kind of type. **Objects/** holds the live objects and what
they do: runtime objects, their parts, the contexts they take their collaborators from, the outcome enum of each
object, and the records of its events (`Objects/Events/`); every coordination between objects lives here, and it
publishes the events that are left to the handlers. **Registry/** holds what keeps live objects in RAM and hands them
out (`UserRegistry`, `RelationshipRegistry`, `Lobby`, the channel registries) and owns the operations on what it holds. **Capabilities/**
holds only the `Scoped*` views (the object's behaviour as one connection may do it). **Ports/** holds the ports to
external computation (a capability with business meaning whose implementation needs external libraries: beatmapset
reader, analyser, assets, screenshot inspector; these interfaces and the data they exchange are the only public
types). **Services/** holds the contracts of the services the system provides (`IAnticheatReporter`). **Persistence/** holds the
repository ports and their query records, **Storage/** the file-storage ports. Application holds **no** handler, no
object-free logic and no background run: the Mediator handlers (**Handlers/**, one `internal` class per message type),
the object-free application logic that needs no live object (**Logic/**: account checks, beatmapset indexing, score
validation and recording; it takes and returns records and plain results, never creates, receives or publishes an
object or an event), the implementations of **Services/** and the background runs (**Workers/**) live in
`Basil.Infrastructure.<Module>`. When an object calls such logic, the object's project declares an `internal` interface
in **Ports/** (`IAccountRules`, `IScoreRecorder`, `IBeatmapsetImporter`) and the Infrastructure project implements it;
the plain data both sides name stays in **Persistence/**. This placement is checked in review. Application grants `InternalsVisibleTo` to its own Infrastructure project and the tests of both.
Each module's public `Add<Module>Application()` builds its contexts and objects; its `Add<Module>Infrastructure()`
registers the implementations of its ports and its migration. Handlers stay `internal`; a project that has them grants
`InternalsVisibleTo` to the composition root (the generated Mediator code names them) and to its own test project, and
nothing else is made visible (the Application project of the module also grants it to the Infrastructure project, because
the handlers and the logic use its internal members). The composition root calls `AddBasil()`. A call across levels of the ladder (Service, Ports,
the three stores, Objects) is direct dependency injection; a call between peers is a Mediator message.

### Hosts, Gateway and Server

The transports are `Basil.Host.Stable` (osu! packets, polling and legacy web endpoints), `Basil.Host.Irc` (plain TCP behind nginx, which ends TLS; every connection must open with a PROXY protocol header from a trusted proxy),
`Basil.Host.Rest` (`api.`, images on `a.`/`assets.`, SSE, diagnostics) and `Basil.Host.Cli` (terminal dashboard and
command box). The composition root is the project `Basil`: the one
`AddMediator` call, logging, shutdown, host and subdomain routing, and the choice of Kestrel, ports and the
database provider. It holds no business rule, no recovery logic and no admission policy. It is "the way to run" the
system; no host or gateway knows it exists.

* The roles (owner decision 2026-11-25): **Gateway** guards (blocks and rate-limits each request, nothing else);
  **Host** translates the wire into the system's messages and routes them inward, and translates the system's language
  back out, with no system logic at all; **Protocol** defines the languages the outside world does not provide (osu!,
  IRC); **Application** processes the requests through its contracts; **Infrastructure** provides third-party services;
  **Domain** is the core business and its rules. A rule that needs data the core does not get means the message
  carries that data in; the rule never moves to the host.
* A host translates: inbound it turns the wire into a strongly typed message of the system (using Protocol), checks
  only that the wire is well formed (a number is a number, a required field is there), and sends it through Mediator; outbound it listens to Notifications and writes its wire. It holds no
  business, permission or state rule. A host never calls `ScopeTo`, never references Infrastructure and
  never references another host. Every message carries the `Session` of the session as its actor, and only the Sessions
  module creates a `Session`.
* Every transport references `Basil.Host.Shared` and never another transport. Rest and Cli never reference
  `Protocol.Game.Stable`. This is checked in review.
* `Basil.Gateway` is the guard: it answers `Allow` or `Deny` for each incoming request from rate, concurrency and load
  (a signed-in user also meets an allowance per kind of work; a request without a user meets only the server's capacity,
  because nginx holds every per-address limit). To protect per object (a user, a session, a room) it may use the
  Application services and capabilities (owner decision 2026-11-25); it only guards, never processes the request. Every
  entry point declares a policy. See [`docs/for-developers/gateway.md`](docs/for-developers/gateway.md).
* The project that defines an options type binds it; BasilBot's reply file is read with the bot
  (`Data/Localizations/bot.json`, written with the defaults when missing).
* nginx stands in front of Basil in every deployment and holds what is about the network (TLS, every per-address limit,
  the size a body declares, stored files through `X-Accel-Redirect`). Basil serves plain HTTP and plain IRC, believes a
  forwarded address only from `Basil:Server:TrustedProxies`, and accepts an IRC connection only after a PROXY header from
  that peer. See [`docs/for-developers/edge.md`](docs/for-developers/edge.md).
* Live HTTP streams use the one hub in `Basil.Host.Rest`; see [`docs/for-developers/sse.md`](docs/for-developers/sse.md).
* The console is the one entry point that skips privilege checks (`OperatorSession`); a chat command still uses the
  typist's privileges. See [`docs/for-agents/identity-privileges.md`](docs/for-agents/identity-privileges.md).

### Application feature folders

Application is sliced by module first, then by folder of responsibility; namespace = project + folder
(`Basil.Application.Chat.Objects`). Every module uses the same folders and creates only the ones it needs: `Objects/`,
`Registry/`, `Capabilities/` (the `Scoped*` views only), `Ports/`, `Services/`, `Persistence/`, `Storage/`, `Messages/`;
Infrastructure mirrors them with `Handlers/`, `Logic/`, `Services/`, `Workers/`, `Adapters/`, `Metrics/`. The full map, with what
each module puts in which folder, is in [`docs/for-agents/module-layout.md`](docs/for-agents/module-layout.md). Tests mirror the
same folders under `tests/`.

### Domain and Application

**Every business operation lives in Domain** (decided 2026-11-20): an object's state, invariants and every transition
of it, including the consequences inside the same object (a kick that vacates the slot, hands the host on and ends the
round; a mode change that strips invalid mods from every slot). Domain is synchronous and holds no lock and no
`Volatile` (a collection that is read without the lock may be a concurrent one, such as `ConcurrentSet`), no timer, no repository, no event publishing and no live object of another module
(a seat holds a `UserRecord`, never a `Session`). Application only offers the contracts to the outside (messages,
`Scoped*`, views, events) and does what needs several objects or the runtime: the object's lock, the map to live
sessions, the lobby, timers, publishing. An operation reports what happened (the outcome enum and the data its event
needs, such as the round progress it caused), so Application never re-derives a rule. The live type and its rule type
share a name and are told apart by namespace (`Basil.Application.Multiplayer.Objects.Rooms.Room` holds `State`, a
`Basil.Domain.Multiplayer.Room.Room`). A Domain outcome enum holds only the reasons of that one object; a reason that
needs another object, a connection or the lobby belongs to an Application enum, one per category of operation and never a
flattened catch-all that copies the Domain enum (`LobbyResult`, `RoomJoinResult`, `RoomInviteResult`, `RoomTargetResult`, `SessionAuthResult`,
`ChannelJoinResult`). Test: if the member needs another object, a connection, a lock or the clock's timers, it is
Application; anything else is Domain.

The detailed rules moved out of this file so each can be read when needed. **Read the matching file before touching that area:**

* [`docs/for-agents/application-design.md`](docs/for-agents/application-design.md): Domain models, Application objects, capabilities (`ScopeTo`), contexts, events, channels, naming. Before touching `Basil.Domain/`, `Basil.Application.<Module>/`, `Basil.Application.Bot/`.
* [`docs/for-agents/identity-privileges.md`](docs/for-agents/identity-privileges.md): privileges, silence, hierarchy, sessions, BasilBot. Before touching users, connections, privileges or the bot.
* [`docs/for-agents/persistence.md`](docs/for-agents/persistence.md): repositories, byte storages, the Registry / Repository (cache-aside) / Storage model, beatmapset storage. Before touching a repository, a storage or `Basil.Infrastructure.<Module>/` or `Basil.Infrastructure.Shared`.
* [`docs/for-agents/code-style.md`](docs/for-agents/code-style.md): C# code style and IDE-suggestion rules. Before writing or reviewing C#.

### Important invariants

#### PostgreSQL-native persistence

PostgreSQL-native (owner decision 2026-11-24): PostgreSQL is the only database and persistence uses Npgsql features
(`jsonb`, `inet`, `citext`, `pg_trgm` GIN indexes, `ILike`) through EF Core's API; still no SQL text. Module Infrastructure
projects may reference `Npgsql.EntityFrameworkCore.PostgreSQL`. See [`database.md`](docs/for-developers/database.md).

#### Multiplayer concurrency

`Room` is mutable shared state.

Operations that read and then mutate a room are synchronous methods of the Application `Room` that hold the room's own `Lock` across the call to the Domain rule and publish inside it; the Domain `Room` holds no lock. A closed room (its match has `EndedAt`) answers `RoomClosed`. `ScopedRoom` only checks permissions and calls them.

Do not introduce a second synchronization mechanism for the same state.

Never await under the room lock.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) for the complete model.

#### Authority

Referee, host, and creator are separate concepts.

Whoever creates a room is its creator, however it was created (in game, or through the API, directly or
by a client acting for the user); every room has a creator. Creating a room needs `Player`. The
creator is remembered on the match and ranks above referees; it is not added to the referee list. A creator
who created the room in game is also its first host. When a room is opened through the API or BasilBot, a creator whose
game client is online and not in any room is seated in the new room as host; a creator already in another
room stays there and does not become host. Managing a room is for the creator and the referees (and
`Tournament`, which acts with the creator's authority in every room); being the host does not
grant management privileges. A creator or referee who has lost the `Player` privilege cannot manage. Every room
operation with a privilege rule runs on `room.ScopeTo(actor)`, which checks authority, not the transport. BasilBot's `!mp` commands call these operations using the typist's connection.

Do not treat them as interchangeable.

The creator has permanent match authority. Referee membership and host state have different lifecycles.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) before changing match privileges or `!mp` authorization.

#### Outcomes and refusals

A behaviour that can be refused returns the outcome enum of the object that owns it (`RoomActionResult`, `UserActionResult`), or a tuple of
the outcome and the data. Every outcome enum has `Denied = 0` (the caller lacks the privileges the action needs; the refusal
does not say which) and `Ok = 1`; the other members start at 2. A business refusal (a valid request the rules do not
allow) is an outcome. A business error (data that breaks a rule of the domain) is an exception defined in Domain or
Application, and a broken precondition stays a BCL exception. Room roles (`RoomPrivileges`) are derived per room from
the roles and are separate from the account's `UserPrivileges`. See [`docs/for-agents/application-design.md`](docs/for-agents/application-design.md).

#### Streaming

Data reaches its consumer directly.

* A database read is bounded (a limit, a page), loaded, the connection released, and only then sent to a client.
* Every accepted upload is written into its target storage's temporary file, validated, then moved atomically into
  place; no part of an uploaded name becomes a path.
* Application does not buffer a payload in a `MemoryStream` or a `byte[]` copy. An upload is judged before its bytes
  are taken (owner decision 2026-11-24): the
  caller is authorized before the body is read, a declared length above the limit of that kind of upload is refused at
  once, the body is read as a stream, its first bytes are checked and the connection is cut on a mismatch, on too many
  bytes or on a too slow sender. Accepted bytes go straight into the target storage. A refused upload is logged, never
  kept.
* A stored file is written once: the writer streams into the storage's temporary file (`DataStorage.SaveAsync` with a
  writer), then the move is atomic. A replay is never copied through memory on its way to disk.
* Large payloads (replays, `.osz` archives, images) never pass through a cache.

#### pp is display-only

The server calculates performance points, and a score carries its pp. `null` means "not calculated yet": a submission
waits up to `beatmaps.scoreWait` for the pp and stores the score with them; when they are late the score is stored without
and they are written once calculated. A score that cannot be calculated (a failed play, a beatmap the server does not
have) keeps `null`. Waiting happens only in the Application object that submits, never in a notification handler.

pp is display-only. It never decides a ranking, a leaderboard order, a player's standing or a match win condition,
unless the project owner says otherwise.

Do not introduce pp-dependent gameplay behavior.

#### Configuration split

`appsettings.json` holds only how the application behaves and is safe to publish. A value that depends on the
deployment, above all a secret (connection strings, passwords, keys, tokens), comes from an environment variable and
never from `appsettings.json` (owner decision 2026-11-24). Basil holds no certificate and speaks no TLS: nginx ends it.

#### Data layout

`Data/` is the one persistent volume, at a fixed place beside the executable (`/app/Data` in the container), and holds the
configuration files, `metadata.json`, `Localizations/`, `Rulesets/` and every stored file. `Cache/` (rebuilt on demand),
`Logs/` and `Temp/` sit beside it. A shipped file is copied into `Data/` only where it is missing and is never
overwritten, and a start is refused on a `Data/` layout or a database migration newer than the program knows. See
[`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md).

#### Optional features never stop the server

An optional feature (ffmpeg previews, the update check, the osu! calculation stack, and any other the server can run without)
that fails logs the failure and turns itself off; it never stops or crashes the server (owner decision 2026-11-24).

#### Stable protocol

osu! (stable) packet layouts are wire contracts.

Changes to packet encoding or decoding must be treated as protocol changes and covered by protocol tests.

#### SSE

A live HTTP stream is a route of its own, never a route that answers JSON or SSE by content negotiation. Its path need not end in `/live`; the generated reference marks it as a stream.

See [`docs/for-developers/sse.md`](docs/for-developers/sse.md).

#### API responses

The `api.` host uses a common response envelope for JSON responses.

The generated OpenAPI schema must describe the same contract that the runtime returns.

Do not manually work around the envelope in individual routes without first checking the API middleware and schema transformation rules.

See [`docs/for-client/api/response-envelope.md`](docs/for-client/api/response-envelope.md).

#### User-visible enums

Keep API record properties typed as their actual enum types.

Do not replace enums with `int` or `string` merely to influence serialization.

Serialization behavior belongs to the API contract and is documented separately.

#### Naming

Use `Privilege`, not `Priv`, for new code and public models.

Do not reintroduce the old abbreviation.

#### Names of types and namespaces

Never write the name or full namespace of a type as a string literal in code (`"Basil.Infrastructure.Users.Persistence.UserRow"`).
Use `nameof(...)` for a simple name and `typeof(T).FullName`, `typeof(T).Namespace` or `typeof(T).Assembly` for the full
name, so that renaming or moving a type or a module needs no search through strings. This covers EF migrations and
snapshots (`Entity(typeof(Row).FullName!, ...)`), reflection, logging filters and tests. The exceptions are a name the
compiler cannot reach: a type of another module that this project must not reference (a module's Infrastructure never
references another module), an assembly or project loaded by name in architecture tests, and names that are stored data
(table names, `__ef_migrations_<module>`), which are contracts and never change with a rename.

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
* [`gateway.md`](docs/for-developers/gateway.md) — admission control
* [`diagnostics.md`](docs/for-developers/diagnostics.md) — diagnostics intake, feed and streams
* [`docs-guideline.md`](docs/for-developers/docs-guideline.md) — documentation rules

Technician documentation:

* [`configuration.md`](docs/for-technicians/configuration.md) — configuration and data directories
* [`https.md`](docs/for-technicians/https.md) — TLS requirements
* [`docker.md`](docs/for-technicians/docker.md) — Docker deployment

Agent documentation:

* [`guidelines.md`](docs/for-agents/guidelines.md) — agent workflow
* [`domain.md`](docs/for-agents/domain.md) — domain-modeling guidance
* [`issue-tracker.md`](docs/for-agents/issue-tracker.md) — GitHub Issues workflow

### Working notes

Lessons that came up during the restructuring and are not specific to this repository are collected in
[`docs/for-agents/working-notes.md`](docs/for-agents/working-notes.md). Read them before delegating work to other agents or
reviewing what they wrote.

## Agent skills

### Issue tracker

Issues live in GitHub Issues for [`thnhmai06/osuBasil`](https://github.com/thnhmai06/osuBasil).

See [`docs/for-agents/issue-tracker.md`](docs/for-agents/issue-tracker.md).

### Domain modeling

Domain-modeling work uses `CONTEXT.md`, created lazily by the `/domain-modeling` workflow. ADRs were removed; the plan
and its decisions live in the documentation under `docs/`.

See [`docs/for-agents/domain.md`](docs/for-agents/domain.md).

## Final verification

Before considering a code change complete:

1. Run the smallest relevant tests while iterating.
2. Run the complete test suite for the final change (none exists yet; see Commands).
3. Run a Release build when the change affects production code.
4. Check the module and host graphs by hand for cross-layer changes.
5. Update authoritative documentation when behavior or design changes.
6. Review the final diff for unrelated changes.
7. Check every new or changed member in the Application projects against
   [Domain and Application](#domain-and-application), above all "behaviour belongs to the object that owns it", "the acted-on object owns the interaction" and
   "Application publishes events; Infrastructure only ships them". This applies to code written by delegated agents too: the orchestrator reviews it
   against these rules before accepting it. `User.Channels`, `User.Join(Channel)`,
   sessions emitting events, a handler that waits or serves several event types, and rules or timers on runtime models and
   registries all got through review once; do not let them back in. Nor should an internal helper that only wraps
   a repository read, an internal helper that takes, returns or creates a live object or publishes an event (except
   the explicit `IAnticheatReporter` system service), a contract name that reveals its mechanism, or a reference from a module's Infrastructure project to another module,
   or any project reference outside the graph of the modules. Since the identity redesign, also
   reject: an action without a real `actor`, authority decided by connection kind, a privilege finer than a `UserPrivileges` flag, a privilege
   derived instead of stored, a privilege that implies another, and a delegation of any kind. Since the 2026-10-16 redesign, also reject the five anti-patterns of "Behaviour belongs
   to the object that owns it" (God Actor, service-centric procedural design, privilege plumbing, exposed mutable
   state, fake scoping), and a bot message that goes through a privilege check or a bot command that runs with the
   bot's privileges instead of the typist's.
8. Review delegated work for what agents tend to leave behind, besides the rules above: `.ConfigureAwait(false)` in
   new code, a changed final newline or reflowed lines nobody asked for, a renamed parameter outside the task,
   stray tabs in XML documentation, a constructor made `internal` on a class the container creates, a member that
   nothing calls any more, a wait between a privilege check and the change it guards, a handler that waits, a
   `switch` statement rewritten as a nullable-result switch expression plus an `if` to silence a hint, a whole-tree
   `git add` that sweeps in files nobody asked for, and a
   consequence that skips offline users or one of several paths. Run parallel agents in separate git worktrees
   (two at a time at most) and merge their commits yourself.

The goal is not merely to produce compiling code. The goal is a verified change that respects Basil's architecture, scope, contracts, and documentation.
