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
verify a change with the eight projects that do build, one at a time:

```bash
dotnet build src/Domain --nologo -v q
dotnet build src/Application/Storage --nologo -v q
dotnet build src/Application/Objects --nologo -v q
dotnet build src/Application/Services --nologo -v q
dotnet build src/Infrastructure/Storage --nologo -v q
dotnet build src/Infrastructure/Capabilities --nologo -v q
dotnet build src/Infrastructure/Runtime --nologo -v q
dotnet build src/Bot/Application --nologo -v q
```

A change to a constructor, a context or `DependencyInjection.cs` also needs the container check: a throwaway
console project that calls the registrations of the application (`AddApplication()` of Application.Objects), `AddInfrastructureRuntime()` and `AddBotApplication()`,
registers a stub for every external port (storage and capability), builds the provider with `ValidateOnBuild`, and
resolves every registration (factory lambdas included). Run it with a timeout: a dependency cycle shows up as a hang,
not as an error. It must finish without a cycle or a missing registration.

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow.

See [`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md) for runtime configuration and data directories.

See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker.

## Architecture

```text
Basil.Domain                     business model and its own validity          -> (nothing)

Basil.Application.Storage        ports to the outside of the process: persistent  -> Domain
                                 (repositories, storages, query records, paging)
                                 and capability ports (beatmapset reader,
                                 analyser, assets, mirror, screenshot inspector)
Basil.Application.Services       application logic that needs no live object:     -> Application.Storage
                                 it takes and returns records and results,
                                 never publishes an event; every type internal
Basil.Application.Objects        the live objects and what they do: runtime       -> Domain, Application.Storage,
                                 objects, scopes, contexts, events, registries,      Application.Services
                                 root objects, the coordination between
                                 objects; the public AddApplication()

Basil.Infrastructure.Storage     implements the persistent ports: PostgreSQL,    -> Application.Storage
                                 files, memory, migrations
Basil.Infrastructure.Capabilities implements the capability ports: osu!          -> Application.Storage
                                 rulesets, FFMpegCore, beatmap mirror, ImageSharp
Basil.Infrastructure.Runtime     event buses and handlers, background loops,     -> Application.Objects
                                 startup work                                       (no Infrastructure project
                                                                                    references Application.Services)
Basil.Protocol.Bancho            osu! client-server wire formats, score         -> external libraries only
                                 submission cipher (BouncyCastle)
Basil.Protocol.Irc               IRC wire format                                -> (nothing)
Basil.Host.Bancho / .Irc / .Api  transports                                     -> Application.Objects,
                                                                                   Infrastructure, their Protocol
Basil.Host                       entry point, composition, logging,             -> everything
                                 diagnostics, LAN advertising

Basil.Bot.Application            BasilBot: chat commands and their replies,     -> Application.Objects
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
its short name without `Basil.`: `src/Domain/`, `src/Infrastructure/`, `src/Application/{Storage,Objects,Services}/`,
`src/Hosts/{Host,Api,Bancho,Irc}/`,
`src/Protocol/{Bancho,Irc}/`. The project file and the namespace keep the full name (`Basil.Domain.csproj`,
`Basil.Domain.*`).

Configuration is bound only in `Basil.Host`. Infrastructure and the transports receive it as `IOptions<T>` of
an options type that the project using it owns; Application reads no configuration.

Application has three projects named by role. **Application.Storage** holds the ports to the outside of the process:
repository and storage contracts, query records and paging, and the capability ports (a capability with business
meaning whose implementation needs external libraries; Infrastructure implements them). **Application.Services**
holds the application logic that needs no live object (account checks, beatmapset indexing, score validation and
recording): it takes and returns records and plain results, never creates, receives or publishes an object or an
event, and every type is `internal`. **Application.Objects** holds the live objects and what they do: runtime
objects, registries, root objects, scopes, contexts, events, and every coordination between objects; it calls the
services and publishes the events they leave to it. Its public `AddApplication()` builds the contexts and the
objects and registers the services (Services gives Objects access to its internals). `Basil.Infrastructure`
references only Application.Storage (Storage and Capabilities) or Application.Objects (Runtime), never
Application.Services. The hosts reference Application.Objects and call `AddApplication()`.

> **Migration status (2026-10-21).** The object-scope redesign and the service moves are implemented across Domain,
> Application, Infrastructure.Runtime and Basil.Bot.Application: behaviour belongs to the owning runtime object and
> permission-dependent operations use its short-lived `ScopeTo(connection)` view; the user, room, lobby, channel,
> beatmapset and score services were absorbed by the objects that own them (`UserRegistry`, `Lobby`, `Room`,
> `GeneralChannelRegistry`, `Beatmapsets`, `BanchoConnection`). The reference is flipped (Objects ->
> Services, every Services type `internal`, the public `AddApplication()` in Objects) and the temporary bridge
> interfaces are gone. The hosts and all test projects still use old namespaces; solution-wide `dotnet build`, `dotnet test` and
> `Basil.ArchitectureTests` do not run. The operator console (`Basil.Cli`), host migration and test migration remain
> unfinished. Verify the migrated projects individually with the eight builds listed under Commands. The decisions in
> [`plans/object-scope-plan-20261016.md`](plans/object-scope-plan-20261016.md) and
> [`plans/application-layering-plan-20261021.md`](plans/application-layering-plan-20261021.md) remain authoritative
> where they agree with this file; older plans are history.

### Application feature folders

Application is sliced by feature, not by kind of type; namespace = project + folder. Application.Storage owns the
ports to the outside (persistent repository and storage ports, query records, paging, capability ports).
Application.Services owns the object-free application logic. Application.Objects owns runtime objects, registries,
scoped views and contexts, events, results and the coordination between objects.

```text
Common/        PageRequest, Page<T>, Interval<T>, Paging                           (Storage)
Events/        Event, IEventPublisher<T>, IEventHandler<T>                         (Objects)
Users/         user and restriction repositories, UserQuery; User, ScopedUser,
               UserRegistry, ScopedUserRegistry, UserResult, login and registration
               inputs; AccountService                                              (Services)
Sessions/      Connection tree (BanchoConnection also submits scores and reports
               anticheat flags), ScopedBanchoConnection and user events
Chat/          Channel tree, GeneralChannelRegistry, IChannelRepository; ScopedChannel,
               ScopedGeneralChannel, events and results
Multiplayer/   Lobby, Room, their parts and scopes; match, round and score repositories;
               room and lobby events and results
Beatmaps/      beatmap and beatmapset repositories and storage, capability ports;
               Beatmapset, Beatmapsets, ScopedBeatmapset; BeatmapsetIndexer         (Services)
Scores/        score, replay and stats repositories; score events; ScoreRecorder     (Services)
Anticheat/     anticheat events
Content/       content repositories, storage ports and the screenshot inspector port
Servers/       Server, ScopedServer, server contexts and server resources
```

Event-handler contracts belong to Application.Objects; event buses and concrete handlers live in
Infrastructure.Runtime or the consumer that owns them, including `Basil.Bot.Application`. Host configuration
(ports, TLS, data paths) belongs to the hosts. BasilBot commands and announcements are server features: a
command is an entry point that uses the typist's scoped objects, and the bot only replies. Query **records**
(the filter a repository applies, with `Parse` for the search syntax) belong to Storage; routes and handlers
that answer queries do not. Server settings changed at runtime (`ServerSettings`) are persistent Domain data,
not host configuration.

* `Common` and `Events` depend on no feature. `Users`, `Beatmaps` and `Scores` do not depend on
  `Multiplayer` objects; a game client's connection only reads its player's room to find the round. Anticheat flags,
  wherever they arrive (score submission, `lastfm.php`), are reported by the host through `BanchoConnection.Report`,
  the single source of anticheat events; BasilBot turns them into chat warnings.
* `Sessions`, `Chat` and `Multiplayer` reference each other (`Room.Host`, `Room.Channel`,
  `Channel.Members`); treat them as one cluster. Their runtime objects and registries live in Objects;
  persistent repositories remain in Application.Storage. Objects change their own state through behaviour, with
  implementation access limited to the owning object and its collaborators. References run from the acted-on
  object to the actor, never back.

### Domain and Application

The detailed rules moved out of this file so each can be read when needed. **Read the matching file before touching that area:**

* [`docs/for-agents/application-design.md`](docs/for-agents/application-design.md): Domain models, Application objects, scopes (`ScopeTo`), contexts, events, channels, naming. Before touching `src/Domain`, `src/Application`, `src/Bot`.
* [`docs/for-agents/identity-permissions.md`](docs/for-agents/identity-permissions.md): permissions, privileges, restrictions, hierarchy, sessions, BasilBot. Before touching users, connections, permissions or the bot.
* [`docs/for-agents/persistence.md`](docs/for-agents/persistence.md): repositories, byte storages, memory-first model, beatmapset storage. Before touching a repository, a storage or `src/Infrastructure/Storage`.
* [`docs/for-agents/code-style.md`](docs/for-agents/code-style.md): C# code style and IDE-suggestion rules. Before writing or reviewing C#.

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

### Notes for the code-principles skill

Principles that came up while restructuring Application and are not osuBasil-specific. They are proposals: the
owner merges them into the skill later, nobody edits the skill silently.

* **Read the IDE's suggestions** (see Code style): async overloads with the token in scope, LINQ where a loop reads as
  a sentence, return-switch over switch over if, ignore allocation hints and serializer-filled DTO reports.
* **Services are logic without live objects.** In a layered backend, a service takes and returns records and plain
  results, never receives or creates a runtime object, never publishes an event and has no public contract. Anything
  that coordinates several objects is a method of the object that is acted on or of the registry that holds them
  (`registry.LoginAsync`, `lobby.ReleaseAsync`, `connection.SubmitScoreAsync`); a file full of former services
  ending up as `Server.X` is the God Actor in disguise. This supersedes the old rule "one child service per part".
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
* **Working with delegated agents:** stage explicit paths, never the whole tree; a worktree agent cannot use the IDE
  tools of the main checkout, so the orchestrator runs the container check after merging; two agents at a time at most,
  and only on disjoint files; review for the faults listed in "Final verification" item 8 and for a changed meaning
  hidden in a "style" rewrite (a `switch` turned into a nullable result plus an `if`).

* **Scoped base class and `Result<TOutcome>`** (2026-10-21, see `docs/for-agents/application-design.md`): one abstract
  `Scoped<T>` holds the effective permissions snapshot and offers `Require(required)` returning the missing permissions
  (`null` = met); behaviours return a shared `Result<TOutcome>` with per-object outcome enums starting at `Ok = 1`.
* **Project rule that contradicts the skill: no `ConfigureAwait(false)`** in a server application; the skill's core list
  says every library await uses it. The owner decides whether the skill gains a "server app" exception.
* **Tests for computed values** pin properties (valid, finite, monotone under a modifier), not the exact number, unless
  the number is a protocol or ruleset contract; expected values come from the specification, never from running the code.
* **Test projects mirror source projects** one to one (`tests/` tree = `src/` tree, folders inside mirror feature
  folders); shared fixtures live in one support project.
* **A fixed-id system account** (the bot, id 1) is taken over if it exists and created if it does not, by startup code,
  documented as the one exception to "the server creates no account".

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
   [Domain and Application](#domain-and-application), above all "behaviour belongs to the object that owns it", "the acted-on object owns the interaction" and
   "Application publishes events; it does not consume them". This applies to code written by delegated agents too: the orchestrator reviews it
   against these rules before accepting it. `User.Channels`, `User.Join(Channel)`,
   sessions emitting events, event handlers inside Application, and rules or timers on runtime models and
   registries all got through review once; do not let them back in. Nor should a service that only wraps
   a repository read, a service that takes, returns or creates a live object or publishes an event, a public
   service contract, a contract name that reveals its mechanism, or a reference from Infrastructure to
   Application.Services. Since the identity redesign, also
   reject: an action without a real `by`, authority decided by connection kind, a check on granted instead of
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
