# AGENTS.md

This file provides guidance to coding agents working in this repository. `CLAUDE.md` only imports
it.

## What this is

Basil is a lightweight, high-performance [osu!](https://osu.ppy.sh/) (stable) backend platform specialized for
multiplayer. The server provides the core capabilities (accounts and sessions, permissions, rooms, chat, scores,
beatmaps, events); specific features such as tournament tooling and overlays are clients built on top
of it, each signing in as an ordinary user. The server works the same with or without any of them.

It is built on [bancho.py](https://github.com/osuAkatsuki/bancho.py), but it is **not a full bancho.py port**. Basil deliberately has a smaller feature surface. pp calculation, clans, osu-web v1/v2 API compatibility, and other unrelated features are intentionally out of scope. Chat commands, announcements and room countdowns are in scope: they are BasilBot, a feature of the server.

The identity, permissions and restriction rules are in [`docs/for-agents/identity-permissions.md`](docs/for-agents/identity-permissions.md); read
them before touching users, sessions, permissions, rooms or chat.

Before porting or recreating anything from bancho.py, read:

* [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md) — authoritative feature scope
* [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) — system structure and dependency direction
* [`README.md`](README.md) — project overview

The generated references are served by a running instance on its `api.` host and published on [GitHub Pages](https://thnhmai06.github.io/osuBasil/): the HTTP API at `/docs/v1`, the endpoints the osu! (stable) client calls at `/docs/osu-stable` (both Scalar), and the BasilBot and IRC guides at `/guides`. Each part of the server writes its own reference (BasilBot from its command catalog, the IRC host from its commands, the osu! host from its routes and packet enums). Do not create a second hand-written reference in Markdown.

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
* Do not duplicate the generated references (HTTP API, osu! (stable) client endpoints, BasilBot commands, IRC).

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
* **Layout mirrors the sources.** One test project per source project (`tests/` tree = `src/` tree, `Basil.X.Tests`),
  folders inside mirror the feature folders; shared fixtures live in `tests/Support/Basil.Testing`; sample content in
  `tests/Assets`.
* **A refusal test checks the missing mask** of `Result<TOutcome>`, and every `Require` has a test that goes red when it
  is removed.

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
dotnet run --project src/Basil
```

Run every test project listed in `Basil.slnx`, one at a time; never pass the solution to `dotnet test`:

```bash
dotnet test tests/Domain -c Release --nologo -v q
dotnet test tests/Application/Users -c Release --nologo -v q
```

Run one test project (the test tree mirrors `src/`):

```bash
dotnet test tests/Application/Users
```

Run a specific test class:

```bash
dotnet test tests/Application/Users \
  --filter "FullyQualifiedName~<TestClassName>"
```

Verify a change with the whole solution in Release, then run the affected test projects one at a time:

```bash
dotnet build Basil.slnx -c Release --nologo -v q
dotnet test tests/Application/Users -c Release --no-build --nologo -v q
```

Projects that need PostgreSQL start a container; set `DOCKER_HOST` when the daemon is not on the default socket, or set
`BASIL_TEST_POSTGRES` to use an existing server. Never run every project in one `dotnet test` invocation.

A change to a constructor, a context or a `DependencyInjection.cs` also needs the container check, which
`tests/Architecture/CompositionTests.cs` runs: it calls `AddBasil()` of the composition root (`Basil`) and `AddBot()`, builds the provider
with `ValidateOnBuild`, resolves every registration (factory lambdas included) and checks that every event handler is
registered, against a deadline: a dependency cycle shows up as a hang, not as an error.

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow.

See [`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md) for runtime configuration and data directories.

See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker.

## Architecture

> **Migration status.** This section describes the target architecture. The code is migrated phase by phase; a part that is
> not migrated yet is marked **(target)**. [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) owns
> the full description and the status.

```text
Basil.Domain                     the system at rest: durable models and the      -> (nothing)
                                 rules that stay true offline

Basil.Application.Common         shared contracts, Result<T>, events, Mediator   -> Domain, Mediator.Abstractions
                                 messages
Basil.Infrastructure.Common      shared database, files and event dispatch       -> Application.Common, Domain

Basil.Application.<Module>       the module's contracts (services, ports,        -> Domain, Application.Common,
                                 repositories, storages) and its runtime objects:   the Application projects of the
                                 Objects/, Registry/, Capabilities/ (Scoped*        modules it uses (below)
                                 only), Ports/, Persistence/, Storage/,
                                 Services/ (contracts); Add<Module>Application()
Basil.Infrastructure.<Module>    the implementations of those contracts: Handlers/ -> its own Application,
                                 (Mediator listeners), Logic/, Services/,            Infrastructure.Common,
                                 Persistence/, Storage/, Adapters/, Workers/         Application.Common
                                 Add<Module>Infrastructure() is its only public
                                 surface
Basil.Infrastructure.Persistence    Npgsql provider (IDatabaseProvider), design-time  -> Infrastructure.Common, the
                                 factories and all PostgreSQL migrations;           module Infrastructure projects
                                 only Basil references it
Basil.Protocol.Stable            the translation language of the osu! wire,      -> Domain, external libraries
                                 score submission cipher (BouncyCastle)
Basil.Protocol.Irc               the translation language of IRC                 -> (nothing)
Basil.Gateway                    admission control: Allow/Deny from size, rate,  -> the rate-limiting library
                                 concurrency and load (hosts enforce: target)
Basil.Host.Shared                views and outcome-to-error mapping              -> Application projects
Basil.Host.Stable / .Irc /       transports: translate the wire to messages and  -> Host.Shared, Protocol, Gateway,
.Rest / .Cli                     send them through Mediator                         Application projects
Basil                            entry point, composition    -> everything
                                 root, "the way to run"

Basil.Bot                        BasilBot, a module at the top of the graph      -> the Application projects of the
                                 (target: Application.Bot / Infrastructure.Bot)     modules it uses, Application.Common
```

The layers stay Clean Architecture (Domain, Application, Infrastructure, hosts); inside the Application and Infrastructure layers
the code is split by module, one project per module and layer (`Basil.Application.<Module>`,
`Basil.Infrastructure.<Module>`; the solution folders `/Sources/Application/` and `/Sources/Infrastructure/` list them).
References only point down:

```text
Auth        -> Common
Beatmaps    -> Common
Content     -> Common
Users       -> Common, Authentication, Content   (folders Users/ and Connections/: they reference each other)
Anticheat   -> Users
Scores      -> Users, Beatmaps
Multiplayer -> Users, Beatmaps, Content, Scores
Chat        -> Users, Multiplayer
Diagnostics -> Users
Servers     -> every module above
Bot         -> Anticheat, Beatmaps, Chat, Multiplayer, Scores, Servers, Users      (target as a module)
```

A module Infrastructure project never references another module; the foreign keys between the module databases
decide the order in which `AddBasil()` registers the migrations (Common, Users, Authentication, Beatmaps, Content, Scores,
Multiplayer, Chat), not these references. `tests/Architecture/ModuleGraphTests.cs` reads the project references and fails on
an edge outside this graph or on a cycle; `HostGraphTests.cs` does the same for the hosts.

`Basil.Protocol.Stable` wraps the communication between the osu! client and the server, as an API wrapper
wraps a remote API: it turns what the client sends (osu-stable packets, the delimited strings of the web
endpoints such as the score submission, the client hash, the client build) into C# models of primitives and
back, including undoing the client's own encoding (the score submission's Rijndael cipher). What the client
sends and receives is a DTO and belongs to Protocol (`RoomPacket`, `LoginRequestBody`, `ScoreSubmission`; their
enum-like fields stay primitives). What the messages mean belongs to Domain, and Protocol builds on it: the game
enums (`GameMode`, `GameMods`, `UserActivity`, `BeatmapsetStatus`, `GameTeamType`, `GameWinCondition`,
`UserPrivileges`, `OsuVersionStream`) carry the client's numbering, so the host casts a primitive to them and back
with no table, and the spectated play (`SpectatorFrame`, `SpectatorScore`, `SpectatorAction`) and the client version
(`OsuVersion`, text in `ClientBuildText`) are Domain values that Protocol reads and writes. Protocol does not
validate and gives no business meaning: a number the client sends is checked by the host with `WireMap.Known` and
`WireMap.Mods` before Application sees it. What has no Domain twin (a slot's neutral team, the open/locked/quit slot
states) stays a Protocol enum, mapped by the host. If a wire format changes, only Protocol changes; a Domain enum
value is never renumbered for a client. `Basil.Protocol.Irc` wraps IRC the same way and references nothing. Only the
transport hosts reference Protocol; Application and Infrastructure never do. Protocol is the language the hosts use to
translate the wire into the system's own messages and back; it does not validate and coordinates nothing.

Projects live under `src/` in folders that mirror the solution folders under `/Sources/`, each named by
its short name without `Basil.`: `src/Domain/`,
`src/{Application,Infrastructure}/{Common,<Module>}/`, `src/Hosts/{Host,Shared,Stable,Irc,Rest,Cli}/`,
`src/Protocol/{Stable,Irc}/`, `src/Bot/`. The project file and the namespace keep the full name
(`Basil.Domain.csproj`, `Basil.Domain.*`, `Basil.Application.Users.Persistence`). Tests mirror the same tree under
`tests/` (`tests/Application/Users/Basil.Application.Users.Tests.csproj` tests `src/Application/Users`;
shared fixtures are under `tests/Support/`, and the tests that build the whole composition and run several hosts
under `tests/Architecture/` and `tests/Hosts/`).

The project that defines an options type binds it (`Add<Module>...(IConfiguration)`) **(target)**; the composition root
only chooses what to run (Kestrel, ports, TLS, the database provider, logging, lifecycle). Until the migration reaches a
project, `Basil` still binds its configuration and hands it over as `IOptions<T>`. Application reads no configuration.

`Application.Common` owns `IActor`, `Result<T>`, `ScopedRights`, shared query values and event contracts/queue; it depends
on Domain and external packages. `Infrastructure.Common` owns shared database, memory and file implementations and the
event dispatcher; it depends on Domain, Application.Common and its external packages.

Inside a module Application project each folder owns one kind of type. **Objects/** holds the live objects and what
they do: runtime objects, their parts, the contexts they take their collaborators from, the outcome enum of each
object, and the records of its events (`Objects/Events/`); every coordination between objects lives here, and it
publishes the events that are left to the handlers. **Registry/** holds what keeps live objects in RAM and hands them
out (`UserRegistry`, `Lobby`, the channel registries) and owns the operations on what it holds. **Capabilities/**
holds only the `Scoped*` views (the object's behaviour as one connection may do it). **Ports/** holds the ports to
external computation (a capability with business meaning whose implementation needs external libraries: beatmapset
reader, analyser, assets, mirror, screenshot inspector; these interfaces and the data they exchange are the only public
types). **Services/** holds the contracts of the services the system provides (`IAnticheatReporter`). **Persistence/** holds the
repository ports and their query records, **Storage/** the file-storage ports. Application holds **no** handler, no
object-free logic and no background run: the Mediator handlers (**Handlers/**, one `internal` class per message type),
the object-free application logic that needs no live object (**Logic/**: account checks, beatmapset indexing, score
validation and recording; it takes and returns records and plain results, never creates, receives or publishes an
object or an event), the implementations of **Services/** and the background runs (**Workers/**) live in
`Basil.Infrastructure.<Module>`. When an object calls such logic, the object's project declares an `internal` interface
in **Ports/** (`IAccountRules`, `IScoreRecorder`, `IBeatmapsetImporter`) and the Infrastructure project implements it;
the plain data both sides name stays in **Persistence/**. `tests/Architecture/ImplementationPlacementTests.cs` enforces
this. Application grants `InternalsVisibleTo` to its own Infrastructure project and the tests of both.
Each module's public `Add<Module>Application()` builds its contexts and objects; its `Add<Module>Infrastructure()`
registers the implementations of its ports and its migration. Handlers stay `internal`; a project that has them grants
`InternalsVisibleTo` to the composition root (the generated Mediator code names them) and to its own test project, and
nothing else is made visible (the Application project of the module also grants it to the Infrastructure project, because
the handlers and the logic use its internal members). The composition root calls `AddBasil()`. A call across levels of the ladder (Service, Ports,
the three stores, Objects) is direct dependency injection; a call between peers is a Mediator message.

### Hosts, Gateway and Server

The transports are `Basil.Host.Stable` (osu! packets, polling and legacy web endpoints), `Basil.Host.Irc` (plain TCP),
`Basil.Host.Rest` (`api.`, images on `a.`/`assets.`, SSE, diagnostics) and `Basil.Host.Cli` (terminal dashboard and
command box). The composition root is the project `Basil`: the one
`AddMediator` call, logging, shutdown/restart, host and subdomain routing, and the choice of Kestrel, ports, TLS and the
database provider. It holds no business rule, no recovery logic and no admission policy. It is "the way to run" the
system; no host or gateway knows it exists.

* A host translates: inbound it turns the wire into a strongly typed message of the system (using Protocol), checks what
  the client sent, and sends it through Mediator; outbound it listens to Notifications and writes its wire. It holds no
  business, permission or state rule. **(target)** A host never calls `ScopeTo`, never references Infrastructure and
  never references another host; today hosts still call `x.ScopeTo(connection)` and the migration replaces that with
  messages, module by module. Every message carries the `Connection` of the session as its actor, and only the Users
  module creates a `Connection`.
* Every transport references `Basil.Host.Shared` and never another transport. Rest and Cli never reference
  `Protocol.Stable`. `tests/Architecture/HostGraphTests.cs` enforces this.
* `Basil.Gateway` (enforcement by the hosts: **target**) is a leaf project that answers `Allow` or `Deny` for each incoming request from size, rate,
  concurrency and load, in two tiers (before and after authentication; the second tier receives an opaque
  `AdmissionSubject` that `Host.Shared` maps from the `Connection`). It has no user types and no business facts. Every
  entry point declares a policy. See [`docs/for-developers/gateway.md`](docs/for-developers/gateway.md).
* The project that defines an options type binds it **(target)**; BasilBot's reply file is bound with the bot
  (`Configurations/Templates/replies.json`, written with the defaults when missing).
* Live HTTP streams use the one hub in `Basil.Host.Rest`; see [`docs/for-developers/sse.md`](docs/for-developers/sse.md).
* The console is the one entry point that skips rights checks (`OperatorConnection`); a chat command still uses the
  typist's rights. See [`docs/for-agents/identity-permissions.md`](docs/for-agents/identity-permissions.md).

### Application feature folders

Application is sliced by module first, then by folder of responsibility; namespace = project + folder
(`Basil.Application.Chat.Objects`). Application.Common owns shared values, paging/search syntax and event
contracts/queue; Infrastructure.Common owns shared persistence and event delivery. The table lists where each folder's
contents are; `Handlers/`, `Logic/` and `Workers/` and the implementations of `Services/` are in the module's
Infrastructure project.

Every module uses these folders, and creates only the ones it needs:

| Folder | Holds |
|---|---|
| `Objects/` | The module's live objects, their parts and contexts, each object's outcome enum, and its event records (`Objects/Events/`) |
| `Registry/` | What keeps live objects in RAM and hands them out (`UserRegistry`, `Lobby`, the channel registries); one of the three stores with Repository (`Persistence/`, awaited, cache-aside) and Storage (`Storage/`) |
| `Capabilities/` | The `Scoped*` views only |
| `Ports/` | The ports to external computation and the data they exchange |
| `Services/` | The contracts of the services the system provides (the implementation is in Infrastructure). Only what a module offers as a service belongs here |
| `Persistence/` | The repository ports, their query records and the data both layers name |
| `Storage/` | The file-storage ports (`IBeatmapsetStorage`, `IReplayStorage`, `IFaqStorage`, the avatars and menu assets) |
| `Messages/` | The Mediator requests the module answers (a message carries the `Connection` as `Actor`) and the values they name |

Infrastructure mirrors those ports: `Persistence/` for the `DbContext`, rows and repositories (PostgreSQL and its migrations are in `Infrastructure.Persistence`), `Storage/` for the
file implementations, `Adapters/` for what needs an external library, `Registry/` for the shared memory
primitives, `Handlers/` for the Mediator handlers, `Logic/` for the object-free application logic, `Services/` for the
service implementations and `Workers/` for the loops and hosted services. Tests mirror the same folders under `tests/`.

```text
Users/         IUserRepository and friends, UserQuery, login inputs    (Persistence)
               IUserAvatarStorage                                      (Storage)
               IAccountRules (internal)                                (Ports)
               User, UserContext, UserGrants, UserProfile, UserOutcome  (Objects)
               Connection, ConnectionContext, ConnectionRules,
               PlayerStatus                                            (Objects/Connections)
               UserRegistry                                            (Registry)
               ScopedUser*, ScopedStableConnection                      (Capabilities)
               AccountRules                                            (Infrastructure Logic)
               ConnectionHandler, RestrictionHandler                   (Infrastructure Handlers)
Chat/          IChannelRepository                                      (Persistence)
               the channel tree, ChannelContext, ChannelOutcome,
               ChannelEvents                                            (Objects)
               GeneralChannelRegistry, RoomChannels, PmChannels,
               SpectatorChannels                                       (Registry)
               ScopedChannel, ScopedGeneralChannel                     (Capabilities)
               PostChatMessage, ChatTarget                             (Messages)
               PostChatMessageHandler and the room, lobby, connection
               and spectator handlers                                  (Infrastructure Handlers)
Multiplayer/   match, round, event and report repositories             (Persistence)
               Room and its parts, RoomOutcome, the event records       (Objects)
               Lobby, LobbyContext                                      (Registry)
               ScopedLobby and the room's scopes                        (Capabilities)
               the match recorders, ScoreHandler, the lobby handlers    (Infrastructure Handlers)
Beatmaps/      beatmap and beatmapset repositories, import and scan
               data                                                     (Persistence)
               IBeatmapsetStorage                                       (Storage)
               Beatmapset, Beatmapsets, contexts, outcomes, events      (Objects)
               ScopedBeatmapset                                         (Capabilities)
               the reader/analyser/mirror/assets ports and
               IBeatmapsetImporter, IBeatmapsetScanner (internal)       (Ports)
               the importer, scanner and planner                        (Infrastructure Logic)
Scores/        score, stats repositories, submission inputs             (Persistence)
               IReplayStorage                                           (Storage)
               ScoreSubmitter, IScoreBeatmapAcceptance,
               ScoreSubmissionOutcome, ScoreOutcome, ScoreEvents        (Objects)
               ScopedScoreSubmitter                                     (Capabilities)
               IScoreRecorder (internal)                                (Ports)
               ScoreRecorder                                            (Infrastructure Logic)
Anticheat/     IAnticheatReporter                                       (Services)
               AnticheatReporter                                        (Infrastructure Services)
               AnticheatEvents                                          (Objects/Events)
Content/       ISettingsRepository, IMenuBannerRepository,
               ServerSettings                                           (Persistence)
               the FAQ, menu, screenshot and seasonals storages          (Storage)
               IScreenshotInspector                                     (Ports)
Servers/       Server, ServerContext, ServerOutcome, ServerRecovery     (Objects)
               ScopedServer and its parts                               (Capabilities)
Diagnostics/   ServerDiagnostics, DiagnosticsOutcome                    (Objects)
               ScopedDiagnostics                                        (Capabilities)
               IDiagnosticsIntake, IDiagnosticsFeed                     (Services)
```

`Diagnostics` (intake, feed and the maintenance actions behind `ScopedDiagnostics`) is a module of its own; the
contribution contracts every module answers (`CollectDiagnostics`, `IDiagnosticsCollector`, `IClearTally`, `ICacheCleaner`,
`DiagnosticsValue`, `DiagnosticsSample`) live in Application.Common. Feature event **records** live under `Objects/Events/` of the module that owns the object they describe; the shared
event contract, publisher and queue belong to Application.Common, and the dispatcher belongs to
Infrastructure.Common's `Workers/`. Concrete handlers live in the `Handlers/` folder of the module's Infrastructure project or in the consumer that
owns them, including `Basil.Bot`. Host configuration (ports, TLS, data paths) belongs to the hosts. BasilBot commands
and announcements are server features: a command is an entry point that uses the typist's scoped objects, and the bot
only replies. Query **records** (the filter a repository applies, with `Parse` for the search syntax) belong to
Persistence; routes and handlers that answer queries do not. Server settings changed at runtime (`ServerSettings`,
owned by Content.Application) exist only while the server runs and are stored through `ISettingsRepository`; they are
not Domain and not host configuration.

* Common projects depend only on Domain and shared external packages. `Users`, `Connections`, `Beatmaps` and `Scores` do not depend on
  `Multiplayer` objects. Anticheat flags,
  wherever they arrive (score submission, `lastfm.php`), are reported through `IAnticheatReporter.Report`,
  the single source of anticheat events; BasilBot turns them into chat warnings.
* `Chat` references `Multiplayer`, and both reference `Users` and `Connections` (`Room.Host`, `Channel.Members`, the online
  `User`); treat them as one cluster. `Users` and `Connections` reference each other (a user holds its connections, a
  connection knows its user), so those two form one cluster of their own. Their runtime objects and registries live in Objects;
  persistent repositories remain in each module's Persistence folder. Objects change their own state through behaviour, with
  implementation access limited to the owning object and its collaborators. References run from the acted-on
  object to the actor, never back.

### Domain and Application

The detailed rules moved out of this file so each can be read when needed. **Read the matching file before touching that area:**

* [`docs/for-agents/application-design.md`](docs/for-agents/application-design.md): Domain models, Application objects, capabilities (`ScopeTo`), contexts, events, channels, naming. Before touching `src/Domain`, `src/Application/*`, `src/Bot`.
* [`docs/for-agents/identity-permissions.md`](docs/for-agents/identity-permissions.md): permissions, privileges, restrictions, hierarchy, sessions, BasilBot. Before touching users, connections, permissions or the bot.
* [`docs/for-agents/persistence.md`](docs/for-agents/persistence.md): repositories, byte storages, the Registry / Repository (cache-aside) / Storage model, beatmapset storage. Before touching a repository, a storage or `src/Infrastructure/*` or `src/Infrastructure/Common`.
* [`docs/for-agents/code-style.md`](docs/for-agents/code-style.md): C# code style and IDE-suggestion rules. Before writing or reviewing C#.

### Important invariants

#### Multiplayer concurrency

`Room` is mutable shared state.

Operations that read and then mutate a room are synchronous methods of `Room` that hold the room's own `Lock` across the complete state transition and publish inside it; a closed room answers `RoomClosed`. `ScopedRoom` only checks permissions and calls them.

Do not introduce a second synchronization mechanism for the same state.

Never await under the room lock.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) for the complete model.

#### Authority

Referee, host, and creator are separate concepts.

Whoever creates a room is its creator, however it was created (in game, or through the API, directly or
by a client acting for the user); every room has a creator. Creating a room needs `RoomCreate`. The
creator is remembered on the match and ranks above referees; it is not added to the referee list. A creator
who created the room in game is also its first host. When a room is opened through the API or BasilBot, a creator whose
game client is online and not in any room is seated in the new room as host; a creator already in another
room stays there and does not become host. Managing a room is for the creator and the referees (and
`RoomManageAny`, which acts with the creator's authority in every room); being the host does not
grant management rights. A creator or referee whose permissions are all suspended cannot manage. Every room
operation with a permission rule runs on `room.ScopeTo(actor)`, which checks authority, not the transport. BasilBot's `!mp` commands call these operations using the typist's connection.

Do not treat them as interchangeable.

The creator has permanent match authority. Referee membership and host state have different lifecycles.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) before changing match permissions or `!mp` authorization.

#### Streaming

Data reaches its consumer directly.

* A database read is bounded (a limit, a page), loaded, the connection released, and only then sent to a client.
* Every upload is spooled to a temporary file, validated, then moved atomically into place; no part of an uploaded name
  becomes a path.
* Application does not buffer a payload in a `MemoryStream` or a `byte[]` copy; an upload it must check goes through
  `IUploadSpool` (a file, not memory), and a refused one is kept in `<Data>/Quarantine/<guid>` for seven days.
* A stored file is written once: the writer streams into the storage's temporary file (`DataStorage.SaveAsync` with a
  writer), then the move is atomic. A replay is never copied through memory on its way to disk.
* Large payloads (replays, `.osz` archives, images) never pass through a cache.

#### No pp in gameplay

Basil does not use performance points for scoring, leaderboards, or match win conditions.

Locally calculated difficulty/star-rating information is display-only.

Do not introduce pp-dependent gameplay behavior.

#### Stable protocol

osu! (stable) packet layouts are wire contracts.

Changes to packet encoding or decoding must be treated as protocol changes and covered by protocol tests.

#### SSE

Live HTTP streams use dedicated `/live` endpoints.

Do not introduce content negotiation between JSON and SSE on the same route.

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

### Notes for the code-principles skill

Principles that came up while restructuring Application and the hosts and are not osuBasil-specific. They are proposals:
the owner merges them into the skill later, nobody edits the skill silently.

* **Read the IDE's suggestions** (see Code style): async overloads with the token in scope, LINQ where a loop reads as
  a sentence, return-switch over switch over if, ignore allocation hints and serializer-filled DTO reports.
* **Internal application logic is object-free; system Services are explicit contracts.** A helper that only implements a
  capability takes and returns records and plain results, never receives or creates a runtime object and never
  publishes an event. A service in a module's Services folder is different: it is a public system operation with a
  contract and implementation. `IAnticheatReporter` is the explicit event-source exception: its purpose is to report
  a live game's flags, and it is the single producer of anticheat events. Do not infer from this exception that internal
  helpers belong in Services. Coordination between objects otherwise belongs to the object acted on or its registry
  (`registry.LoginAsync`, `lobby.ReleaseAsync`, `scoreSubmitter.ScopeTo(connection).SubmitAsync`); a file full of
  former helpers ending up as `Server.X` is the God Actor in disguise. This supersedes the old rule "one child service per part".
* **Plain data that two layers both name in public signatures lives in the lower layer** (inputs, failure enums, import
  results), so the upper layer can expose it while the lower one stays internal.
* **Flip a reference direction in stages with a temporary internal bridge interface** declared in the upper project:
  move the logic, keep the call working through the interface, flip the reference, delete the interface.
* **Break a construction cycle with a deferred port** (`Func<T>`/`Lazy<T>` resolved at first use) rather than by
  letting one object build another inside its constructor; check the container with a harness that resolves every
  registration under a timeout, because a cycle shows up as a hang.
* **Detect the kind of an uploaded file from its content when the extension is unknown or when its size is needed**
  (ImageSharp header for images, ffprobe for audio); trust a known extension otherwise; no part of an uploaded name
  becomes a temporary file name. Cap what the input can cost (bytes while reading, pixels per side) before using it.
* **Give an architecture test a positive control**: after writing a no-dependency rule, assert once that the opposite
  claim (the project does depend on X) is detected, so a rule that always passes is caught.
* **Review reads the code, not only the green tests.** A passing suite proves the cases it covers; it does not prove
  that the code implements the contract, that every branch is reachable, or that a test was not weakened to pass. Read
  the diff line by line against the contract, the failure modes and the untested paths.
* **Delegated work is fast and time-boxed.** Give a subagent explicit files, a target state, what it must not change,
  the command that verifies it and a deadline (about 10 minutes for mechanical work, 20 for design). When the deadline
  passes, stop and report what was done instead of exploring outside the task.
* **Working with delegated agents:** stage explicit paths, never the whole tree; a worktree agent cannot use the IDE
  tools of the main checkout, so the orchestrator runs the container check after merging; two agents at a time at most,
  and only on disjoint files; review for the faults listed in "Final verification" item 8 and for a changed meaning
  hidden in a "style" rewrite (a `switch` turned into a nullable result plus an `if`).
* **Capabilities and `Result<TOutcome>`** (2026-10-21, revised; see `docs/for-agents/application-design.md`): a `Scoped*`
  capability is a `readonly struct` valid for one request that snapshots the effective permissions and states each
  requirement through `ScopedRights.Require` (`null` = met); behaviours return a shared `Result<TOutcome>` with
  per-object outcome enums starting at `Ok = 1`.
* **No `ConfigureAwait(false)`** in a server application (owner decision, 2026-10-26: the project rule stands and the
  skill's "every library await uses it" does not apply here).
* **Tests for computed values** pin properties (valid, finite, monotone under a modifier), not the exact number, unless
  the number is a protocol or ruleset contract; expected values come from the specification, never from running the code.
* **Test projects mirror source projects** one to one (`tests/` tree = `src/` tree, folders inside mirror feature
  folders); shared fixtures live in one support project.
* **A persistence layer has no SQL text** (owner decision, 2026-10-22): Entity Framework Core with LINQ and row objects,
  never Dapper or raw SQL (`FromSql`, `ExecuteSql`, `SqlQuery`, `NpgsqlCommand`); the few schema details EF cannot
  express (a filtered unique index, check constraints) are written in the model configuration and listed in the
  review. Rows are separate entity types, never the Domain types; migrations are EF migrations.
* **A fixed-id system account** (the bot, id 1) is taken over if it exists and created if it does not, by startup code,
  documented as the one exception to "the server creates no account".

* **Run the real binary before calling a host done** (2026-10-30). Tests that start the server inside the test process
  never execute `Program`, the exit path or the signal handling: the first run of the real executable found a crash
  at every stop, a fatal log line from a listener and an image route answering 500. Start it against a real database,
  create an account with the offline command, and exercise one request per protocol, one refusal, the stop signal and
  the exit code.
* **Resolve what the exit path needs before running the host.** The service provider is disposed when the host has
  stopped; reading a service from it afterwards throws and loses the exit code. Take the object first, run, then use it.
* **Read the memory-first store in the order that cannot lose a record.** (Being removed with the write-behind model:
  a repository that awaits its writes has no queue to snapshot. It stays only until the last module is migrated.)
* **Validate at the boundary what the consumer will need, and let the consumer survive what slipped through.** A
  header-only inspection accepts a picture that cannot be decoded; the serving side answers 404, not 500. Probe only the
  formats the server accepts, so the parsers of the others never see uploaded bytes.
* **Authorize before you look up.** A command or route that answers "no such X" before it checks the right to manage X
  tells an unauthorized caller which names exist. Check the right first; the refusal must be identical whether or not
  the target exists (same status, same missing-permission mask).
* **Tests that share one running host own their identities.** A limit counted per address or per account is shared
  with every earlier test that is still closing sockets; give each limit test addresses or accounts nobody else uses.
* **Read a reply to its last line, and pace every poll.** A long list is split over several lines, so look at all of
  them. A loop that polls a fixed number of times without a pause ends before an asynchronous queue delivers; use the
  helper that pauses between polls.
* **A flake gets its failure message and a root cause, never a longer timeout or a retry.** Quiet test output hides the
  message: log to a result file (trx) and loop the test until it fails. Most of the flakes of the Host phase were the
  test reading too early or too little; one was a real race in production code.
* **Adding a member to an enum means running the exhaustive tables.** Run the whole test project of every area the
  change touches, not the filtered tests of the feature: a table that lists every outcome only goes red there.
* **Delegated agents are hands; the orchestrator decides how.** Write the files, the exact change, the cases and the
  verification into the prompt, pass it as a file, bound it with a timeout and have it write an exit marker (a long
  prompt passed on the command line hung a run for seven hours). Read the agent's diff, not its summary.
* **Re-verify a suspected defect in the code before dispatching it.** A handover list is a hypothesis: three of its
  fifteen items were already handled (the code closed the connection, or the lock had nothing left to block).
* **Check the license before bumping a dependency to a fixed version.** A security release can change its terms; the
  version that fixes the advisories may not build without a key, which is the owner's decision, not a build detail.

Proposals from the modular-monolith redesign, for the owner to merge into the skill (nobody edits the skill silently):

* **Define the layers by lifecycle, not by technology.** Domain is the system at rest (what is stored and the rules true
  offline); Application is the contracts plus the running system; Infrastructure implements the contracts. A type that
  only means something while running is Application, however plain it looks.
* **Separate "who" from "what".** Services are the doers (verbs with a contract); stores and objects are the things.
  Order them Service > Ports > Registry | Repository | Storage > Objects, use direct injection across levels and a
  mediator between peers.
* **Never await a notification under a lock.** Objects enqueue events while they hold their lock; a dispatcher publishes
  outside it.
* **A translation layer is a language, not a service.** Protocol is the vocabulary of a wire; the host translates and
  dispatches, and the message type (not the host) chooses the handler. The actor travels in the message as an
  unforgeable object created by one module only.
* **Admission control is a leaf and knows no business.** Allow/Deny from size, rate, concurrency and load, in two tiers,
  with an opaque subject for the second; every entry point must declare a policy and a test fails when one does not.
* **Persistence is awaited.** Prefer cache-aside over write-behind; reads are bounded, a listing never instantiates live
  objects, and a large payload never passes through a cache.
* **Streaming is a rule, not an optimization.** Spool uploads to a temporary file, validate, move atomically; no
  `MemoryStream` in the application layer.
* **An architecture test over source text needs a positive control.** The ban on `MemoryStream` in Application scans
  `src/Application/**/*.cs` (reflection cannot see a local variable) and a temporary file that contains it proves the
  scan can fail.
* **Keep what validation refused, briefly.** A refused upload moves to a quarantine under a name of the server's own
  and is swept after a week, so an operator can look at it and the disk cannot fill with it.
* **A listing that only decides "seen or not" asks for ids.** The scanner lists ids bounded by the list ceiling and
  loads only the few rows it forgets.

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
2. Run the complete test suite for the final change.
3. Run a Release build when the change affects production code.
4. Check architecture tests for cross-layer changes.
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
   reject: an action without a real `actor`, authority decided by connection kind, a check on granted instead of
   effective permissions, a stored or configured `Privileges`, privileges used to grant anything, and a
   delegation of any kind. Since the 2026-10-16 redesign, also reject the five anti-patterns of "Behaviour belongs
   to the object that owns it" (God Actor, service-centric procedural design, permission plumbing, exposed mutable
   state, fake scoping), and a bot message that goes through a rights check or a bot command that runs with the
   bot's rights instead of the typist's.
8. Review delegated work for what agents tend to leave behind, besides the rules above: `.ConfigureAwait(false)` in
   new code, a changed final newline or reflowed lines nobody asked for, a renamed parameter outside the task,
   stray tabs in XML documentation, a constructor made `internal` on a class the container creates, a member that
   nothing calls any more, a wait between a rights check and the change it guards, a handler that waits, a
   `switch` statement rewritten as a nullable-result switch expression plus an `if` to silence a hint, a whole-tree
   `git add` that sweeps in files nobody asked for, and a
   consequence that skips offline users or one of several paths. Run parallel agents in separate git worktrees
   (two at a time at most) and merge their commits yourself.

The goal is not merely to produce compiling code. The goal is a verified change that respects Basil's architecture, scope, contracts, and documentation.
