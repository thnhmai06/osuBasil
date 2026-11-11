# Application design rules

Rules for Domain and Application objects, capabilities, events and naming. Moved out of `AGENTS.md`; the rules are binding. Read this before touching `src/Domain`, `src/Application/*` or `src/Infrastructure/Bot`.

> **Migration status.** The target architecture ([`architecture.md`](../for-developers/architecture.md)) moves handlers, object-free logic and service implementations from `Basil.Application.<Module>` into `Basil.Infrastructure.<Module>`, makes hosts send Mediator messages instead of calling `ScopeTo`, and moves BasilBot into its own module. The handlers, the object-free logic and the service implementations are already in `Basil.Infrastructure.<Module>`; the contracts stay in Application. A rule below that mentions `Handlers/`, `Logic/` or `Workers/` means the folder of the module's Infrastructure project. Hosts still call `ScopeTo` for the modules not yet migrated. New code follows the target.

### Scopes and results (decided 2026-10-21; supersedes any older wording)

* **A capability (`ScopedX`, "Cap") is a `readonly struct` valid for one request or command.** `x.ScopeTo(connection)`
  returns it. It holds the connection (`Actor`) and `Privileges`, the privileges the connection held when it was created (a
  snapshot taken once, never re-taken), and it never lives in a field or property (an architecture test enforces this).
  It exposes the owner as a property named after it (`Channel`, `Room`, `User`, …) and reaches the owner's parts through
  **forwarding getters** (`room.ScopeTo(actor).Slots`, `.Rounds`, `.Settings`). The owner's raw mutators are `internal` to
  its Application project, so only a capability, which has checked the privileges, can change the owner. A behaviour states
  the requirement through `ScopedPrivileges.Lacks(held, required)`: `true` means something required is not held. A `[Flags]`
  enum is met when every required bit is held; any other enum is ordered levels and is met when the held value is at least
  the required one. Most capabilities hold the caller's **effective** `UserPrivileges` and answer `XOutcome.Denied`;
  `ScopedRoom` holds `RoomPrivileges`, the room privileges its roles give, computed once from creator, referee, host and player
  roles and the global privileges; a role refusal is the outcome `RoleDenied`, or `Denied` when the caller
  may not join rooms at all. An implementation never compares privileges itself.
* **Every behaviour that can be refused returns the outcome enum of its object** (`RoomOutcome`, `ChannelOutcome`,
  `UserOutcome`, `BeatmapsetOutcome`, …), with a tuple such as `(UserOutcome Outcome, T? Data)` when it also returns data.
  Each enum has `Denied = 0` (the refusal for lack of privileges, which does not say which privileges are lacking, and
  which a default value can never confuse with success) and `Ok = 1`; the other members are `NotFound` and the object's
  own reasons. There is no shared result wrapper. There is no
  `NotAuthorized`, `Restricted`, `NoPermission` or `NotPermitted` member and no `bool` or `*Failure`
  stand-in. Callers compare with `XOutcome.Ok` or `XOutcome.Denied`.
* **Granting uses the actor's privileges, ranking uses the granted ones.** An actor can grant only privileges they hold;
  the strict-superset hierarchy test compares the privileges the two accounts were granted.
* **`ConfigureAwait` is not used** anywhere in the project's source (a server has no synchronization context). An
  architecture test fails on `ConfigureAwait(` in `src/`. This overrides the `code-principles` skill until the skill
  is changed.

### Domain and Application

* **Domain** is the system at rest: the durable models (`Match`, `Round`, `MatchSettings`,
  `User`, `Beatmap`, `Login`, …) and the rules that stay true while the system is offline. A model guarantees that **it is valid**: field values
  in range, its own text format, values derived purely from its own data. No runtime-only state, no time, connection or
  I/O, no events. A type that only means something while the server runs (session state, presence, spectator frames, a room in play) is
  Application, not Domain; a row that carries data to the database is Infrastructure.
* **Behaviour belongs to the object that owns it, and is reached through that object.** Every concept with state, invariants and
  privileges is an **Application object** (`User`, `Connection`, `Room`, `RoomSlot`, `Lobby`, the
  channels, `Beatmapset`, and the root `Server`) in the Objects folder of its module (`Basil.Application.<Module>`). It owns its state,
  keeps its invariants, and offers its behaviour; its state changes only inside its own behaviour. Its persistent
  value is a Domain record named `XRecord` with the plain value `XData` (`UserRecord`, `BeatmapsetRecord`,
  `MatchRecord`); the object wraps it. A persistent value that has no live object of its own, such as a
  restriction, is a plain Domain class named after the concept (`UserRecord`). A part is reached through its owner
  (`room.Slots[3]`), never through an outside object that takes the owner as a parameter.
  **`x.ScopeTo(connection)`** returns `ScopedX`, a short-lived capability for one request or command (see "Scopes and
  results" below). It snapshots the caller's effective privileges once, and each behaviour checks that snapshot through
  the base class and returns `XOutcome.Denied` before it takes the object's lock,
  keeps the invariants, acts and emits the event. The caller never passes `connection`, privileges or an actor into a
  call. **Anything whose actions
  depend on privileges or privileges is scoped; only a system that needs no check is not** (the server's own work:
  Runtime handlers, startup, the bot's announcements, public reads).
  Internal capability helpers keep only application logic that needs no live object (account checks, beatmapset
  indexing, score validation and recording); coordination between objects belongs to the object that is acted on
  (`UserRegistry` for logins and connections, `Lobby` for what happens across rooms, `ScoreSubmitter` for a
  game client's score, `AnticheatReporter` for its anticheat flags, `Beatmapsets` for imports). Five
  anti-patterns to reject in review: a **God Actor** that gathers every behaviour (every former service ending up
  as `Server.X`); **service-centric procedural
  design** (a service takes an object and does its behaviour for it); **privilege plumbing** (privileges passed
  through many layers and methods); **exposing mutable state** (callers change internals past the invariants);
  **fake scoping** (`ScopeTo` only wraps while behaviour and checks stay in a central service). The shared
  authority primitives (`Access`, effective privileges, hierarchy, "a block yields to a higher privilege") are a
  library each `ScopeTo` calls, not a layer to pass through. A domain record and its Application object never
  share a name.
* **The service ladder.** Service > Ports > the three stores (Registry | Repository | Storage) > Objects; a level uses only the
  levels below it. A call across levels is direct (dependency injection); a call between peers is a Mediator message. A
  Handler is the Mediator listener for one message type, thin and `internal`, in `Infrastructure.<Module>/Handlers`. Hosts do not call `ScopeTo`: they send a message that carries the `Connection`
  and the handler scopes the object.
* **Each folder of a module owns one kind of type.** The full map is in `AGENTS.md`. **Persistence/** holds the
  persistent ports (repositories) and query records; **Storage/** holds the file-storage ports; Application.Common
  holds shared paging and search syntax. Live objects (`Room`, `RoomSlot`, `User`, connections and channels), their
  contexts, their outcome enums and their event records live in **Objects/**; the registries that keep live objects in
  RAM (`UserRegistry`, `Lobby`, `GeneralChannelRegistry`, the channel registries) live in **Registry/**; the `Scoped*`
  views live in **Capabilities/** (only those), the ports to external computation in **Ports/**, the contracts of the object-free logic in **Ports/** (an `internal` interface, implemented in `Infrastructure.<Module>/Logic`); the event handlers, the logic and the background runs live in the Infrastructure project (**Handlers/**, **Logic/**, **Workers/**). In the
  Users module the online user and its context are in `Objects/`, its connections in `Objects/Connections/`, and its
  registry in `Registry/`. Persistence never decides a business rule, emits an event, calls a service or starts a
  timer. A state change and its consequences belong to the object that owns the state.
* **A service the system provides has a contract; the logic behind a capability does not.** What the module offers as
  a service to the hosts and to other modules lives in **Services/** as a contract and its implementation
  (`IAnticheatReporter`); a helper that only serves one layer is not a service and stays with what implements it.
  Internal capability helpers never receive live objects or publish events. A system-facing service normally returns
  its result without publishing; `IAnticheatReporter` is the explicit exception because reporting a live game's flags
  is the system operation and it is the single producer of anticheat events. Object-free logic such as account checks,
  beatmapset indexing and score validation stays `internal` in the folder of the capability or object it serves;
  objects reach it through `InternalsVisibleTo`.
  Plain data that both layers use in public signatures (login, registration and score submission inputs, import and
  scan results) lives in the Persistence folder. Callers reach privilege-dependent behaviour through
  `ScopeTo(connection)` and the server's own work through labelled system operations.
* **Object or service?** One question decides it: does the operation have a caller whose privileges it must
  check? Then it is behaviour of the object it changes, reached through `ScopeTo(connection)`. An operation that
  has no caller (the server's own work, a handler's reaction, startup) or that coordinates several objects
  (login, import, score submission) is the behaviour of the object that owns the identity, the collection or the
  actor, and it calls internal capability logic only for the part that needs no live object. A service in Services/
  is the explicit system-facing operation described above, not a replacement for that object behaviour.
* **Objects get their collaborators through a context.** An object takes a `…Context` record of ports (clock,
  event publisher, repositories, the ports of the objects it works with: `RoomContext`, `UserContext`, `LobbyContext`,
  `ChannelContext`, `ConnectionContext`, `BeatmapsetContext`, `ServerContext`) when it is created. The context, the
  object's constructor are `internal`; the registration builds them in factory lambdas, because the container only
  sees public constructors (a root object that two others need, such as the user registry for a session, is reached
  through a deferred port to avoid a construction cycle). A service class that the container creates by itself
  needs a `public` constructor even when the class is `internal`. An object calls a service for pure logic only; a
  port is implemented by whoever owns the other object (`Lobby` implements `IRoomLobbyPort`).
  Because the factories hide dependencies from `ValidateOnBuild`, verify the container by resolving every
  registration with the external storage ports stubbed (see Commands).
* **A scope is a snapshot: re-check after waiting.** A `ScopedX` is for one request or command and is never kept.
  The privilege snapshot is taken once and is not re-taken. A behaviour that awaits between its check and its change
  (a lookup, a repository call, the room gate) re-checks, inside the object's own scope or lock, the state and roles
  the wait could have changed (`IsOpen`, a block, the caller's role in the room, a closed room) before it changes
  anything; a referee removed while waiting for the gate must not act.
* **A refusal by role is not a lack of account privileges.** Privileges that come from a role in one object (creator,
  referee, host, player in a room) are decided by that object's own role rules and reported as an outcome named after the
  role (`NotManager`, `NotHost`); `Denied` is only for account-level privileges checked through `Lacks`.
* **System operations are labelled.** A public method on an object that skips the privilege check (`Lobby.Unwatch`,
  `Server.SayAsync`, `Server.WhisperAsync`, `Server.OpenChannel`, `Lobby.ReleaseAsync`, `Room.ReceiveScore`, `Server.Notify`) says in its
  XML summary that the server itself calls it and needs no privileges. A chat command, an API route or any other entry
  point that acts for a user never calls one; it uses the scope of the user's connection.
* **Capability ports.** A capability with business meaning whose implementation needs external libraries
  or resources is a public interface in a module's Ports folder (with the data it exchanges) that the module's Infrastructure project implements
  (`IBeatmapAnalyser`, `IBeatmapsetReader`, `IBeatmapAssets`, `IBeatmapsetMirror`, `IScreenshotInspector`), even
  when only the hosts use it. Pure plumbing (logging, diagnostics, metrics, SSE hub, TLS, mDNS, update checks,
  OpenAPI, the response envelope) gets no contract. Whatever kind of file a client sends, trust a known extension
  and probe the content only when it is unknown or when its size is needed: an image with ImageSharp
  (header only, never decoded; a screenshot is always identified because its pixel size is capped), audio with
  ffprobe through FFMpegCore (duration and bytes are read from the content); no part of an uploaded name reaches a
  temporary file name.
* **Contract names do not reveal the mechanism.** If the work moved from the file system to the network, or
  from one library to another, the name would not change: `ScanAsync`, not `ScanFileAsync`;
  `IBeatmapAssets.OpenAsync`, not `ReadFromDiskAsync`.
* **Registries live in their own folder.** A registry holds live runtime objects, so it is a public class of
  a module's Registry folder (`UserRegistry`, `Lobby`, `GeneralChannelRegistry`) that also owns the operations on what
  it holds (`UserRegistry` opens and closes connections and logs users in). Mutating registry members are `internal`.
  Persistent repositories, query records and paging belong to the module's Persistence folder, file-storage ports to
  its Storage folder. Other capability ports describe what lies outside the process: files, network, external
  computation and the clock (`TimeProvider`).
* **Outer layers use storage directly.** Infrastructure handlers read, list and search storage, and
  write persistent records whose write carries no rule or consequence (an admin renames a user, hides a
  beatmapset), directly through the repository, after the capability has checked the caller's privilege (today the hosts still do this themselves; target: they send a Query or Command). Services are only for actions; a service
  never wraps a plain read or write (a score service's `GetAsync` forwarding to `IScoreRepository.GetAsync` is
  wrong). Runtime state changes only through the behaviour of the object that owns that state; runtime setters
  are `internal`.
* **Each object enforces its own rules.** An operation keeps the owning object's parts consistent in that same
  operation (a room keeps its channel in step with its players). A consequence that crosses object boundaries
  (a closed connection removes memberships in rooms and spectator streams) is announced as an event; Infrastructure
  handlers call the operations owned by those objects.
* **Background work.** An object declares one run of a background job (`UserRegistry.CloseIdle()`,
  `Beatmapsets.ScanAsync()`); Infrastructure owns the loop, the trigger, the
  period and its configuration. A one-shot timer that is the consequence of an operation (an empty room's
  closing) belongs to the lobby that owns the room and runs on `TimeProvider`. A room's countdown belongs to
  BasilBot: it keeps the timer, listens to the room's events to cancel, announces the marks and sends the message that
  starts the round.
* **Derive whatever is derivable.** A property a runtime object co-owns with its domain object
  (truth in Domain) is a forwarding property (`Room.Name => Match.Name`).
* **A user is one `User`; each place they log in from is a `Connection`.**
  `User` (sealed) represents the account and, while it is online, everywhere they connect from: it holds what is
  shared (`AwayMessage`, the `Silence`) plus its connections as child data, keyed by
  `ConnectionType` (`Stable`, `Tourney`, `Irc`, `Api`), with typed accessors (`Stable`, `Irc`, `Tourneys`,
  `Apis`); how many connections of each type one user may hold is a per-type limit the user registry enforces (target;
  see [`identity-privileges.md`](identity-privileges.md)).
  `Connection` is an abstract class (`User`, `Record`, `Login`, `Token`, `IsOpen`, `Type`) with `StableConnection`,
  `TourneyConnection`, `IrcConnection`, `ApiConnection`; each holds only what exists for that kind of client
  (`StableConnection.Status`). Rooms, channels and the lobby hold connections, not users' sessions.
  Chat keeps a PM channel per online user; it is opened when the user is first needed and closed when they go offline.
  Spectating is a relation on the spectated osu! client (`StableConnection.Spectators`), changed through
  `host.ScopeTo(actor).SpectateAsync()` (what only an osu! client has lives under `StableConnection`:
  spectating and its presence status). Chat keeps the client's spectator channel in step by reacting to the
  spectator events (the first spectator brings the host in, the last one out), so membership of that channel is
  eventually consistent with them; the channel never stands for who is spectating.
* **Identify by the basis object**, not its id (`Room` by `Match`, `GeneralChannel` by `Name`). Runtime
  objects that can be recreated with the same user or name compare **by reference**: a connection
  is one login, a `User` instance is one online period (`UserRegistry.Online` holds it; an offline account gets a transient instance that is never registered), a
  runtime channel is one opening (`#mp_5` is reused when a room id is reused). Cleanup that carries
  an old object can never touch its replacement. "Is this user already here?" compares the user
  and is a separate check from object identity. Ids are for repository lookups only. Choose object vs identifier
  deliberately (`Room.Beatmap` object, `PlayerStatus` beatmap `Md5?`); never `string` for an MD5.
* **Relations are references** in `ConcurrentSet<T>`, never `ConcurrentDictionary<T, byte>`.
* **`User` or a connection?** If the information is gone once the user goes offline, store the
  connection (`StableConnection` as slot occupant or host); otherwise the `User` (creator, referees,
  bans).
* **The acted-on object owns the interaction.** An actor (a user, a session, any active object)
  only *calls* an action. The relation is stored on the object acted on (`Channel.Members`,
  `Room.Slots`), and its short-lived scope exposes the operation (`channel.ScopeTo(actor).Join()`,
  `room.ScopeTo(actor).Members.JoinAsync(password)`). The event belongs to that object's category
  (`ChannelMemberJoined` is a `ChannelEvent`). The
  session does **not** keep a `Channels` set, does **not** have a `Join(Channel)` method, and nothing
  emits events on its behalf. Otherwise every new joinable kind (channel, room, spectator stream, …) forces a
  new `Join*` method and a new set on the user. "Which channels is X in?" is a query over channels, not state
  on X. Ask "who is acted on?" for every relation, member and event.
* **Parts are reached through the owning object's scope.** `room.ScopeTo(actor)` exposes `.Slots`, `.Rounds`,
  `.Settings`, `.Authority` and `.Members`; `users.Of(record).ScopeTo(actor)` exposes the user's silence,
  relationships and account operations. Channels, sessions, connections, the lobby, beatmapsets and the server
  follow the same pattern. There are no child service trees for object behaviour; coordination that spans objects
  is a method of the object that is acted on (or of the registry that holds them).
* **Each owner manages its own kind, and reacts to other kinds only through events.** The user registry
  opens and closes connections and announces `UserConnectionOpened`/`UserConnectionClosed`; its responsibility
  ends there. Leaving rooms and parting channels when a connection closes is done by Infrastructure handlers
  that receive the event and call the owning object (`Lobby.ReleaseAsync`, `GeneralChannelRegistry.PartAll`). When a second connection of the same kind arrives
  for a user an owner still holds, the owner **asks the old connection who it is** (`connection.IsOpen`):
  still open, refuse the new one; already closed (cleanup not run yet, or failed), remove the old one as an
  ordinary leave and admit the new one. Cleanup operations must be safe to run again.
* **Peers talk through events; owners call their parts directly.** Two operations are peers when neither
  object owns the other and neither's rule depends on the other (scores and rooms, the anticheat and rooms): the
  acting operation publishes its event, and the shared event queue delivers it to the peer's handler
  (`ScoreSubmitted` → `ScoreHandler` → `Room.ReceiveScore`, `AnticheatPlayerFlagged` reaches clients that
  watch it, such as BasilBot). An object calls another directly only for a clear ownership relation or to keep
  a rule that must hold within the same operation: objects use storage ports; Chat keeps the channel of each open room,
  of each online user (PM) and of each watched client (spectator); a room tells the lobby when it empties or fills;
  logging in opens the connection; deleting a user closes their connections at once, a security rule that
  must not wait for a handler.
* **Application publishes events and handles the ones that concern its own module; Infrastructure only ships them**
  (the handlers are in `Infrastructure.<Module>/Handlers`; contracts and event records stay in Application). The base event,
  `IEventPublisher<T>` and `EventQueue` live in Application.Common; the feature event records live in the Objects
  folder of their module (`Objects/Events/`) and its handlers in the Handlers folder of its Infrastructure project. An object or the
  bot's voice publishes an event through `IEventPublisher<T>` (`Publish`): it stamps `Timestamp`, queues the delivery
  and returns at once. **A notification is never awaited under a lock**: the object enqueues and the dispatcher publishes outside it. A handler is a class implementing `Mediator.INotificationHandler<TEvent>` for one concrete
  event type (`ConnectionHandler`, `SessionEndHandler`, `ScoreHandler`, the `Room*Recorder` handlers in the Handlers folders of their
  modules; `RoomAnnouncer`, `AnticheatAnnouncer`, `ChatHandler` in `Basil.Infrastructure.Bot`). The shared queue (`EventQueue`) is run by
  `EventDispatcher` in Infrastructure.Common's Workers folder and delivers the events one at a time in the order they
  were published,
  across all event types; a failing handler is logged and the others still run. The mediator resolves handlers at
  first publish, not when the publisher is built (a handler reaches objects that publish). Persistence and internal
  capability helpers never publish; a system-facing service publishes only when it is the explicitly declared single
  source of that system event (currently, `IAnticheatReporter`). Other services return what happened and the owning
  object publishes it. One operation publishes one event
  carrying its whole consequence. Events are `sealed record`s in a category tree (`Event` <- `RoomEvent` <-
  `RoomSlotEvent` <- `RoomSlotTeamChanged`), but a handler serves one concrete type. A handler only reacts to an
  event by calling the owning object's operation; event -> client-notification routing belongs to the hosts.
* **Handlers are quick; slow work goes to its own queue.** The queue awaits its handlers one after another, so a
  handler that waits (a lookup, a room, the store) holds up every later event of the server. A handler that may
  wait only queues the work on a bounded queue of its own, with a cap per user and a timeout per item, and returns
  (`CommandWorker` of the bot). A dropped item is logged, never silent.
* **A consequence reaches everyone, through every path.** What a change does to the people it concerns it does
  whether they are online or not (removing a referee removes an offline referee from the open rooms too), and every
  path that grants the same authority applies the same gate (`ScopedRoom` and `ChannelRules.InRoom` agree on
  `Tournament` and `Player`).
* **Naming: Domain record `XRecord` (with `XData`), the Application object `X`, the object that holds the live
  ones `XRegistry`; an internal capability helper is an internal `XService` with no contract, while a system-facing
  service has a public `IXService` contract and an implementation in Services/.** There is no `*Session`
  suffix: the Application object is the thing itself, and Domain only stores records. No suffixes such as
  "Definition". `GeneralChannelRecord` → `GeneralChannel` → `GeneralChannelRegistry` (it holds only general
  channels); `UserRecord` → `User` → `UserRegistry`. The osu! terms win for matches: `MatchRecord` → `Room` →
  `Lobby`. The room's channel is named after the room (`RoomChannel`, kept by `RoomChannels`) because the room is where things happen and
  `MatchRecord` is only the record. `Channel` shares its simple name with the static class
  `System.Threading.Channels.Channel`: a file that uses both qualifies the .NET one.
* **Event names are `{Group}{Subject}{PastTenseVerb}`.** The group is the category the event belongs
  to (`Room`, `Channel`, `User`, `Lobby`); when the subject is the group itself it is written once
  (`ChannelOpened`, `UserSilenced`). A subject that belongs to another is written `{Parent}{Child}`
  (`RoomRoundPlayerLoaded`, `RoomSlotTeamChanged`, `UserConnectionOpened`). The abstract category
  records follow the same rule (`RoomRoundEvent`, `ChannelMembershipEvent`).
* **Chat channels.** Domain stores only what is persisted: `GeneralChannelRecord` (identity: the name with its
  `#`) and `GeneralChannelData` (`Topic`, `ReadPrivileges`, `WritePrivileges`, `AutoJoin`, `Visible`); messages
  are the record `Message`. Application: abstract `Channel` (`Name`, `Topic`, members, IRC convention) with
  `GeneralChannel` (wraps the record), `RoomChannel`, `SpectatorChannel`, `PmChannel`; each names itself
  (`#mp_5`, `#spec_7`, the owner's name for a PM channel). **Each channel lives with the object that manages
  it**: `GeneralChannelRegistry` holds the general channels, `RoomChannels` the channel of each open room, `PmChannels` the PM
  channel of each online user, `SpectatorChannels` the spectator channel of each watched `StableConnection`. Each kind of
  channel decides who may read and write it. A private message is a post into the recipient's PM channel.
  Access follows osu!: a user may join a channel (including `/join` over IRC) only if an osu! client doing the
  matching in-game action would have access. General channels are created, configured and deleted at runtime through
  `Server` (`ServerConfigure`); configuring a channel removes the members who may no longer read it.
* **Assets are referred to by `ContentReference`** (Domain), never by `string` or `Uri`: either the relative path of a
  file the server stores under its data directory (`Menu/Banners/x.png`, valid on every machine) or an absolute
  http/https address of an asset hosted elsewhere, kept as given and never copied. The value is canonical (its
  parsing and normalization live on the type). File storages give out the reference of what they store; the hosts
  serve stored files under a fixed public path per asset directory (static file provider rooted at each asset
  directory, never at the whole data directory) and only distribute them. Replays are named
  `{ScoreId} {Player} - {Artist} - {Title} [{Version}] ({yyyy-MM-dd}) {Mode}.osr`, as osu! names an exported
  replay, and hold a complete `.osr` (the header is built when the score is stored, with the client version that
  played it); `osu-getreplay.php` serves only the LZMA frames inside it and the API serves the whole file;
  screenshots get a UUIDv7 name.
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
  the beatmapset, so the root object is `Beatmapsets` and the contracts are `IBeatmapsetReader`, `IBeatmapsetStorage`,
  `IBeatmapsetMirror` and `BeatmapsetImportResult`. A byte storage is `I{Owner}{Thing}Storage`
  (`IUserAvatarStorage`, `IMenuBannerStorage`, `IMenuSeasonalsStorage`); medium words (`Image`,
  `Background`, `Archive`) and redundant qualifiers (`Server` in `ISettingsRepository`) stay out of names.
* **Knowledge about a Domain value lives on that value.** A fixed set of flags is a member of its `{Enum}Groups`
  class (`StableFlagsGroups.CheatSigns`, `UserPrivilegesExtensions.Chat`) or of the owning type (`UserPrivilegesExtensions.Default`),
  never a combination member of the enum itself; a per-mode factory is a static member of its type
  (`BeatmapObjects.NewFrom(mode)`);
  services use them instead of keeping their own copies.
* **One concept, one name.** Use the same name for the same concept in every model, parameter,
  event and API: the moment something happened is `Timestamp`, a span is `StartedAt`/`EndedAt`, a
  future window is `StartsAt`/`EndsAt`, a stored record's lifecycle is
  `CreatedAt`/`UpdatedAt`/`DeletedAt`, the caller of an operation is `actor`. Do not introduce
  `OccurredAt`, `When`, `Since`, `LoginTime` or similar synonyms.


### BasilBot replies

* Everything the bot says is text the operator can edit: one JSON file, `Configurations/Templates/replies.json`, keys to ICU
  MessageFormat strings with named placeholders (`{room}`, plural selectors). One language. The defaults are embedded
  in the assembly and written to the configuration folder when the file does not exist. The file is watched and reloaded
  while the server runs; an invalid file keeps the last good set and logs a warning; a missing key falls back to its
  default. Reply keys are typed constants owned by the bot; tests reference the keys, never copy the default text.
* A reply to a command refused with `Denied` does not name the privileges that were lacking.
