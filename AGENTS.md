# AGENTS.md

This file provides guidance to coding agents working in this repository. `CLAUDE.md` only imports
it.

## What this is

Basil is a private [osu!](https://osu.ppy.sh/) stable server focused on offline multiplayer tournaments.

It is built on [bancho.py](https://github.com/osuAkatsuki/bancho.py), but it is **not a full bancho.py port**. Basil deliberately has a smaller feature surface. pp calculation, friends, clans, a general-purpose public v1/v2 API, the full bancho.py chat-command set, and other unrelated features are intentionally out of scope.

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
Basil.Domain                  business model and its own validity        -> (nothing)
Basil.Protocol.Bancho         bancho wire format                          -> (nothing)
Basil.Protocol.Irc            IRC wire format                             -> (nothing)

Basil.Application             use cases, organized by feature folders     -> Domain
                              (runtime model, events, ports, operations)

Basil.Infrastructure          persistence, storage, media, background     -> Application, Protocol.*
                              services, concrete providers
Basil.Host.Bancho / .Irc / .Api  transports                               -> Application, Infrastructure,
                                                                             their Protocol
Basil.Host                    entry point and composition                 -> everything
```

> **Migration in progress.** The Application runtime model has been reworked (phases 0–9 of
> [`plans/application-environment-plan-20260930.md`](plans/application-environment-plan-20260930.md),
> decisions in part A); read it before touching Application. Infrastructure, the hosts and the
> tests have not been migrated to it yet. The older
> [`plans/application-runtime-model-plan-20260929.md`](plans/application-runtime-model-plan-20260929.md)
> is history. Until the lower projects are migrated, **only `Basil.Domain` and
> `Basil.Application` build**: `Basil.Infrastructure`, `Basil.Host.*` and every test project still
> use the old Application namespaces, so solution-wide `dotnet build`/`dotnet test` and
> `Basil.ArchitectureTests` do not run. Verify with
> `dotnet build src/Basil.Application/Basil.Application.csproj`.
> `docs/for-developers/architecture.md` still describes an older structure.
>
> `architecture.md`, `multiplayer.md` and `chat.md` describe the reworked Application and mark
> what is still pending. Other documents may still describe the previous model; rewrite each one when
> the code it describes is migrated, not before.
>
> Where a plan conflicts with [Domain and Application](#domain-and-application) below, this file
> wins. In particular, the older plan's §0.2 item 7 (actions as methods on the session) and §2.5
> (handlers inside Application) are superseded by "the acted-on object owns the interaction" and
> "Application emits events; it does not consume them".

### Application feature folders

`Basil.Application` is one project sliced by feature, not by kind of type. Each folder holds its
runtime model, events, ports and operations together; namespace = folder.

Notifications, event handlers, chat commands and their reply strings, host/storage configuration
and queries do not belong in Application; they live in Infrastructure, the hosts or the bot. Do
not add them back.

```text
Common/        Event base, IEventPublisher
Users/         login attempt, IUserRepository, ICredentialRepository, IAdminKeyRepository, Registration
Sessions/      UserSession, Connection (+ ConnectionType), Presence and its events, Gateway,
               SpectatorChatChannelSession
Chat/          ChatChannelSession tree, ChatChannels (configured channels), channel events and results
Multiplayer/   Lobby, Room (lock, membership, authority, settings, round, countdown), RoomSlot(s),
               RoomChatChannelSession, RoomResult, Events/, IMatchRepository
Beatmaps/      BeatmapCatalog and its events, analyser and storage ports
Scores/        ScoreSubmission, score/replay/stats ports
```

* `Common` depends on no feature. `Users`, `Beatmaps` and `Scores` do not depend on `Multiplayer`
  (`ScoreSubmission` is the exception: it records scores against the player's room).
* `Sessions`, `Chat` and `Multiplayer` reference each other (`Room.Host`, `Room.Channel`,
  `ChatChannelSession.Members`); treat them as one cluster. That is why features are folders, not
  projects: their invariants rely on `internal` members (`Room.Emit`) staying inside one assembly.
  References run from the acted-on object to the actor, never back.

### Domain and Application

* **Domain** holds business models that exist beyond runtime (`Match`, `Round`, `MatchSettings`,
  `User`, `Beatmap`, `Login`, …) and guards their own validity. No runtime-only state, no events.
* **Application** holds what exists only at runtime (`Room`, `RoomSlot(s)`, `UserSession` and its
  connections, `ChatChannelSession`s), plus the events they emit.
* **Application models an environment, not a pipeline.** It simulates the objects that exist while
  the server runs and the facts that happen to them, so other layers can observe and plug in. It
  does not script "do this, then notify that, then save this" flows. When a rule belongs to the
  environment (a room closes when its last player leaves, a room keeps its channel's members in
  step with its players), the object that owns the state enforces it itself; it is never left to a
  hook that might not be installed.
* **Derive whatever is derivable.** A property a runtime object co-owns with its domain object
  (truth in Domain) is a forwarding property (`Room.Name => Match.Name`). The event writer is
  never exposed.
* **A user online is one `UserSession`; each place they log in from is a `Connection`.**
  `UserSession` (sealed) represents the user regardless of where they connect and holds what is
  shared (`AwayMessage`, `PmChannel`) plus its connections as child data, keyed by
  `ConnectionType` (`Bancho`, `Tourney`, `Irc`, `Bot`); whether a user may hold several of a kind is
  the extension method `ConnectionType.AllowsMany()` (only `Tourney` is `true`), with
  typed accessors (`Bancho`, `Irc`, `Bot`, `Tourneys`). `Connection` is an abstract class
  (`Session`, `Login`, `IsOpen`, `Type`) with `BanchoConnection`, `TourneyConnection`,
  `IrcConnection`, `BotConnection`; each holds only what exists for that kind of client
  (`BanchoConnection.Status`, `BanchoConnection.SpectatorChannel`). Rooms, channels and the lobby
  hold connections, not sessions. Owned parts (a session's `PmChannel`, a connection's
  `SpectatorChannel`) are created and destroyed with their owner; they are not relations.
* **Identify by the basis object**, not its id (`Room` by `Match`, `GeneralChatChannel` by `Name`). Runtime
  objects that can be recreated with the same user or name compare **by reference**: a connection
  is one login, a `UserSession` is one period online (look it up by `User` through `Presence`), a
  runtime channel is one opening (`mp_5` is reused when a room id is reused). Cleanup that carries
  an old object can never touch its replacement. "Is this user already here?" compares the user
  and is a separate check from object identity. Ids are for repository lookups only. Choose object vs identifier
  deliberately (`Room.Beatmap` object, `PlayerStatus` beatmap `Md5?`); never `string` for an MD5.
* **Relations are references** in `ConcurrentSet<T>`, never `ConcurrentDictionary<T, byte>`.
* **`User` or a connection?** If the information is gone once the user goes offline, store the
  connection (`BanchoConnection` as slot occupant or host); otherwise the `User` (creator, referees,
  bans).
* **The acted-on object owns the interaction.** An actor (a user, a session, any active object)
  only *calls* an action. The object the action is performed on stores the resulting relation and
  emits the event. A connection joins a channel through `channel.Join(by)`; the channel keeps its
  members and emits `MemberJoined`. The session does **not** keep a `Channels` set, does **not**
  have a `Join(ChannelSession)` method, and does **not** emit events: an event always comes from
  the object that was interacted with. Otherwise every new joinable kind (channel, room, spectator stream, …) forces a new
  `Join*` method and a new set on the user, which breaks OOP. "Which channels is X in?" is a query
  over channels, not state on X. Ask "who is acted on?" for every relation, member and event.
* **Each object manages only what it owns, and reacts to other objects only through events.**
  `Presence` opens and closes connections and announces `ConnectionOpened`/`ConnectionClosed`; its
  responsibility ends there. It does not call rooms or channels. Their cleanup
  (leave the room, part channels) is done by Infrastructure handlers that receive the event and call
  the owning object's operation. When a second connection of the same kind arrives for a user the owner
  still holds, the owner **asks the old connection who it is** (`connection.IsOpen`, set by `Presence`):
  still open, refuse the new one; already closed (cleanup not run yet, or failed), remove the old
  one as an ordinary leave and admit the new one. Like an exam room that finds someone who looks
  like you inside: it checks that person before deciding. Cleanup operations must be safe to run
  again. Rules about an object's own state (a room closes when empty, a room
  keeps its channel in step with its players) stay inside that object.
* **Application emits events; it does not consume them.** Each runtime object is an event source
  over its own `Channel<T>`. One operation emits one event carrying its whole consequence. Events
  sit in a nested category tree (`Event` ← `RoomEvent` ← `RoomSlotEvent` ← `SlotTeamChanged`)
  so a consumer can subscribe to a whole category. Event handlers, dispatchers, and event →
  client-notification routing belong to Infrastructure and the hosts, never to Application.
* **Repository contracts are written per model** as `IXxxRepository` (`IMatchRepository`,
  `IScoreRepository`, …), each declaring exactly the operations Application needs, including
  creation that returns the stored model with its assigned identity. Extract a shared interface
  only when several repositories genuinely share an operation, and even then keep the
  `IXxxRepository` contract that callers depend on. Do not add parallel generic ports
  (`ICreatable`). Key normalization (for example case- and space-insensitive user names) is the
  repository's own lookup concern, not Application's.
* **Runtime objects that exist only in memory are not ports.** Online sessions, live channels and
  rooms are held by concrete Application objects (the environment), which also own the room lock.
  Ports are for what lies outside the process: storage, files, network, clock.
* **Naming: Domain model `X`, runtime model `XSession`.** No suffixes such as "Definition". `Room`
  is the historical name for the runtime of `Match`; the room's channel is named after the room
  (Domain `RoomChatChannel`, runtime `RoomChatChannelSession`) because the room is where things
  happen and `Match` is only the record.
* **Chat channels.** Domain: abstract `ChatChannel` (`Name`, `Topic`, IRC convention; named to avoid
  C#'s `Channel`) with `GeneralChatChannel` (configured chat; alone carries `ReadPrivilege`,
  `WritePrivilege`, `AutoJoin`, `Visible`), `RoomChatChannel`, `SpectatorChatChannel`,
  `PmChatChannel`; messages are the record `ChatMessage`. Runtime: abstract `ChatChannelSession`
  with `GeneralChatChannelSession`, `RoomChatChannelSession`, `SpectatorChatChannelSession`,
  `PmChatChannelSession`; each decides who may read and write. **Whoever owns a channel manages
  it**: `ChatChannels` manages only general channels, a `Room` its room channel, a `UserSession` its PM
  channel, a `BanchoConnection` its spectator channel. A private message is a post into the
  recipient's PM channel. Access follows osu!: a user may join a channel (including `/join` over
  IRC) only if an osu! client doing the matching in-game action would have access.
* **Privilege checks require every bit.** A user satisfies a `ClientPrivileges` requirement only if
  every bit set in the requirement is set on the user (`ClientPrivileges.Has`); an empty
  requirement is always satisfied. This applies everywhere, not only to channels.
* **Channel names are stored without `#`** (`osu`, `lobby`, `mp_5`). Transports add the prefix
  when writing and strip it when reading; `#multiplayer`/`#spectator` are resolved by the transport
  to the sender's room or spectator channel. Do not "fix" stored names to include `#`.
* **Setters only assign and validate.** A setter stores the value and throws if it is invalid;
  it never emits an event or changes anything else. A change that has consequences (emits an
  event, changes other fields, checks authority, groups several fields into one event) is a
  dedicated method, and the setter gets the visibility that stops outsiders from bypassing it
  (`private`/`internal`). Such a method is a real operation, not a `Change*`/`Rename`/`With*`
  wrapper around a single field.
* **One concept, one name.** Use the same name for the same concept in every model, parameter,
  event and API: the moment something happened is `Timestamp`, a span is `StartedAt`/`EndedAt`, a
  future window is `StartsAt`/`EndsAt`, a stored record's lifecycle is
  `CreatedAt`/`UpdatedAt`/`DeletedAt`, the caller of an operation is `by`. Do not introduce
  `OccurredAt`, `When`, `Since`, `LoginTime` or similar synonyms.

### Important invariants

#### Multiplayer concurrency

`Room` is mutable shared state.

Operations that read and then mutate a room must hold the room's exclusive scope across the complete state transition. That scope comes from `Room.EnterAsync()`, which returns `null` once the room is closed; the room owns its lock (plan R13).

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
and checks authority inside `Room`, not in the transport.

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
7. Check every new or changed member in `Basil.Application` against
   [Domain and Application](#domain-and-application), above all "the acted-on object owns the
   interaction" and "Application emits events; it does not consume them". This applies to code
   written by delegated agents too: the orchestrator reviews it against these rules before
   accepting it. `UserSession.Channels`, `UserSession.Join(ChannelSession)`, sessions emitting
   events, and event handlers inside Application all got through review once;
   do not let them back in.

The goal is not merely to produce compiling code. The goal is a verified change that respects Basil's architecture, scope, contracts, and documentation.
