# Application design rules

Rules for Domain and Application objects, scopes, events and naming. Moved out of `AGENTS.md`; the rules are binding. Read this before touching `src/Domain`, `src/Application` or `src/Bot`.

### Scopes and results (decided 2026-10-21; supersedes any older wording)

* **Every `ScopedX` derives from one abstract `Scoped<TRights> where TRights : struct, Enum`.** The base holds the connection
  (`By`) and `TRights Rights { get; }`, the rights the connection held when `ScopeTo(connection)` created the scope, and
  offers `protected TRights? Require(TRights required)`: `null` means the requirement is met, otherwise it returns what
  is missing. A scope lives for one request or command and is then dropped; it is never stored. The comparison follows
  the enum, found by reflection: a `[Flags]` enum is met when every required bit is held (the result is the missing
  bits), any other enum is ordered levels and is met when the held value is greater than or equal to the required one
  (the result is the required level). Most scopes are `Scoped<Permissions>` (`Rights` = the caller's **effective**
  permissions) and pass the missing mask to `Result<TOutcome>.Denied`; `ScopedRoom` is `Scoped<RoomPermissions>`
  (`Rights` = the rights its roles give, computed once from creator, referee, host and player roles and the global
  permissions) and a role refusal is the outcome `RoleDenied`, or a denial naming `RoomJoin` when the caller may not join
  rooms at all. A scope over `UserPrivileges` fits the same way. An implementation never compares rights itself: it states
  the requirement of each behaviour through `Require` and returns the denial. Each scope exposes its owner as a property
  named after it (`Channel`, `Room`, `User`, …).
* **Every behaviour that can be refused returns `Result<TOutcome>`**, a `readonly record struct (TOutcome Outcome,
  Permissions? Missing)` shared by all objects. Each object owns one outcome enum (`RoomOutcome`, `ChannelOutcome`,
  `UserOutcome`, `BeatmapsetOutcome`, …) with `Ok = 1` and `0` meaning "not set" (so a denied result, whose outcome is
  `default`, can never read as success); the other members are `NotFound` and the object's own reasons. There is no
  `NotAuthorized`, `Restricted`, `Silenced`, `NoPermission` or `NotPermitted` member and no `bool` or `*Failure`
  stand-in. A denial is `Missing is not null`. The caller never learns whether a bit was never granted or is suspended
  by a restriction; it only learns which permissions are missing. Callers test `result.IsOk`.
* **Granting uses effective permissions, ranking uses granted ones.** An actor can grant only bits that are currently
  in effect for them; the strict-superset hierarchy test compares the permissions the two accounts were granted, so
  rank does not change with a temporary restriction.
* **`ConfigureAwait` is not used** anywhere in the project's source (a server has no synchronization context). An
  architecture test fails on `ConfigureAwait(` in `src/`. This overrides the `code-principles` skill until the skill
  is changed.

### Domain and Application

* **Domain** holds business models that exist beyond runtime (`Match`, `Round`, `MatchSettings`,
  `User`, `Beatmap`, `Login`, …). A model guarantees that **it is valid**: field values
  in range, its own text format, values derived purely from its own data. No runtime-only state, no events.
* **Behaviour belongs to the object that owns it, and is reached through that object.** Every concept with state, invariants and
  rights is an **Application object** (`User`, `Connection`, `Room`, `RoomSlot`, `Lobby`, the
  channels, `Beatmapset`, and the root `Server`) in Application.Runtime. It owns its state,
  keeps its invariants, and offers its behaviour; its state changes only inside its own behaviour. Its persistent
  value is a Domain record named `XRecord` with the plain value `XData` (`UserRecord`, `BeatmapsetRecord`,
  `MatchRecord`); the object wraps it. A persistent value that has no live object of its own, such as a
  restriction, is a plain Domain class named after the concept (`UserRestriction`). A part is reached through its owner
  (`room.Slots[3]`), never through an outside object that takes the owner as a parameter.
  **`x.ScopeTo(connection)`** returns `ScopedX`, a short-lived capability for one request or command (see "Scopes and
  results" below). It snapshots the caller's effective permissions once, and each behaviour checks that snapshot through
  the base class and returns a `Result<XOutcome>` carrying the missing permissions before it takes the object's lock,
  keeps the invariants, acts and emits the event. The caller never passes `connection`, permissions or an actor into a
  call. **Anything whose actions
  depend on permissions or privileges is scoped; only a system that needs no check is not** (the server's own work:
  Runtime handlers, startup, the bot's announcements, public reads).
  Services keep only application logic that needs no live object (account checks, beatmapset indexing, score
  validation and recording); coordination between objects belongs to the object that is acted on
  (`UserRegistry` for logins and connections, `Lobby` for what happens across rooms, `ScoreSubmitter` for a
  game client's score, `AnticheatReporter` for its anticheat flags, `Beatmapsets` for imports). Five
  anti-patterns to reject in review: a **God Actor** that gathers every behaviour (every former service ending up
  as `Server.X`); **service-centric procedural
  design** (a service takes an object and does its behaviour for it); **permission plumbing** (rights passed
  through many layers and methods); **exposing mutable state** (callers change internals past the invariants);
  **fake scoping** (`ScopeTo` only wraps while behaviour and checks stay in a central service). The shared
  authority primitives (`Access`, effective permissions, hierarchy, "a block yields to a higher right") are a
  library each `ScopeTo` calls, not a layer to pass through. A domain record and its Application object never
  share a name.
* **Persistence holds the ports; runtime objects and registries live in Runtime.** Application.Persistence holds
  the persistent ports (repositories, byte storages), query records and paging. Runtime objects (`Room`, `RoomSlot`, `User`,
  connections and channels), registries (`UserRegistry`, `GeneralChannelRegistry`) and their scoped
  behaviour live in Application.Runtime, as public classes; the online user and its registry are in `Users/`, and
  its connections with their context are in `Connections/`. Persistence
  never decides a business rule, emits an event, calls a service or starts a timer. A state change and its
  consequences belong to the object that owns the state.
* **Services have no public contract.** A service is an `internal` class of Application.Capabilities that Runtime calls
  directly; there is no `IXService` and no registry contract. A service never sees a live object (`Room`, `User`,
  `Connection`), never publishes an event (events and `IEventPublisher<T>` belong to Runtime, so the service returns
  what happened and the object publishes it) and holds no running state. Runtime objects reach it through
  `InternalsVisibleTo`; plain data that both Services and Runtime use in public signatures (login, registration and
  score submission inputs, import and scan results) lives in Application.Persistence. Callers reach permission-dependent
  behaviour through `ScopeTo(connection)` and the server's own work through labelled system operations.
* **Object or service?** One question decides it: does the operation have a caller whose rights it must
  check? Then it is behaviour of the object it changes, reached through `ScopeTo(connection)`. An operation that
  has no caller (the server's own work, a handler's reaction, startup) or that coordinates several objects
  (login, import, score submission) is the behaviour of the object that owns the identity, the collection or the
  actor, and it calls a service only for the part that needs no live object.
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
  The permission snapshot is taken once and is not re-taken. A behaviour that awaits between its check and its change
  (a lookup, a repository call, the room gate) re-checks, inside the object's own scope or lock, the state and roles
  the wait could have changed (`IsOpen`, a block, the caller's role in the room, a closed room) before it changes
  anything; a referee removed while waiting for the gate must not act.
* **A refusal by role is not a missing permission.** Rights that come from a role in one object (creator, referee,
  host, player in a room) are decided by that object's own role rules and reported as an outcome named after the role
  (`NotManager`, `NotHost`) with `Missing` left `null`; `Missing` names only account-level permissions (`RoomJoin`,
  `RoomCreate`, `ChatPost`, `ScoreSubmit`) checked through `Require`.
* **System operations are labelled.** A public method on an object that skips the rights check (`Lobby.Unwatch`,
  `Server.SayAsync`, `Server.WhisperAsync`, `Server.OpenChannel`, `Lobby.ReleaseAsync`, `Room.ReceiveScore`, `Server.Notify`) says in its
  XML summary that the server itself calls it and needs no rights. A chat command, an API route or any other entry
  point that acts for a user never calls one; it uses the scope of the user's connection.
* **Capability ports.** A capability with business meaning whose implementation needs external libraries
  or resources is a public interface in Application.Capabilities (with the data it exchanges) that Infrastructure.Capabilities implements
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
* **Registries belong with runtime objects.** A registry holds live runtime objects, so it is a public class of
  Application.Runtime (`UserRegistry`, `GeneralChannelRegistry`) that also owns the operations on what it holds
  (`UserRegistry` opens and closes connections and logs users in). Mutating registry members are `internal`.
  Persistent repositories, storage ports, query records and paging belong to Application.Persistence. Other
  capability ports describe what lies outside the process: files, network, external computation and the clock
  (`TimeProvider`).
* **Outer layers use storage directly.** Infrastructure and the hosts read, list and search storage, and
  write persistent records whose write carries no rule or consequence (an admin renames a user, hides a
  beatmapset), directly through the repository, after the host has checked the caller's permission. Services are only for actions; a service
  never wraps a plain read or write (a score service's `GetAsync` forwarding to `IScoreRepository.GetAsync` is
  wrong). Runtime state changes only through the behaviour of the object that owns that state; runtime setters
  are `internal`.
* **Each object enforces its own rules.** An operation keeps the owning object's parts consistent in that same
  operation (a room keeps its channel in step with its players). A consequence that crosses object boundaries
  (a restriction removes memberships in rooms and spectator streams) is announced as an event; Infrastructure
  handlers call the operations owned by those objects.
* **Background work.** An object declares one run of a background job (`UserRegistry.CloseIdle()`,
  `Beatmapsets.ScanAsync()`); Infrastructure owns the loop, the trigger, the
  period and its configuration. A one-shot timer that is the consequence of an operation (an empty room's
  closing) belongs to the lobby that owns the room and runs on `TimeProvider`. A room's countdown belongs to
  its scoped round behaviour; aborting a round cancels a pending start. BasilBot announces the countdown marks.
* **Derive whatever is derivable.** A property a runtime object co-owns with its domain object
  (truth in Domain) is a forwarding property (`Room.Name => Match.Name`).
* **A user is one `User`; each place they log in from is a `Connection`.**
  `User` (sealed) represents the account and, while it is online, everywhere they connect from: it holds what is
  shared (`AwayMessage`, the active `Restrictions`) plus its connections as child data, keyed by
  `ConnectionType` (`Bancho`, `Tourney`, `Irc`, `Api`), with typed accessors (`Bancho`, `Irc`, `Tourneys`,
  `Apis`); only `Tourney` and `Api` allow several connections per user, a rule the user registry owns.
  `Connection` is an abstract class (`User`, `Record`, `Login`, `Token`, `IsOpen`, `Type`) with `BanchoConnection`,
  `TourneyConnection`, `IrcConnection`, `ApiConnection`; each holds only what exists for that kind of client
  (`BanchoConnection.Status`). Rooms, channels and the lobby hold connections, not users' sessions.
  Chat keeps a PM channel per online user; it is opened when the user is first needed and closed when they go offline.
  Spectating is a relation on the spectated osu! client (`BanchoConnection.Spectators`), changed through
  `host.ScopeTo(by).SpectateAsync()` (what only an osu! client has lives under `BanchoConnection`:
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
  connection (`BanchoConnection` as slot occupant or host); otherwise the `User` (creator, referees,
  bans).
* **The acted-on object owns the interaction.** An actor (a user, a session, any active object)
  only *calls* an action. The relation is stored on the object acted on (`Channel.Members`,
  `Room.Slots`), and its short-lived scope exposes the operation (`channel.ScopeTo(by).Join()`,
  `room.ScopeTo(by).Members.JoinAsync(password)`). The event belongs to that object's category
  (`ChannelMemberJoined` is a `ChannelEvent`). The
  session does **not** keep a `Channels` set, does **not** have a `Join(Channel)` method, and nothing
  emits events on its behalf. Otherwise every new joinable kind (channel, room, spectator stream, …) forces a
  new `Join*` method and a new set on the user. "Which channels is X in?" is a query over channels, not state
  on X. Ask "who is acted on?" for every relation, member and event.
* **Parts are reached through the owning object's scope.** `room.ScopeTo(by)` exposes `.Slots`, `.Rounds`,
  `.Settings`, `.Authority` and `.Members`; `users.Of(record).ScopeTo(by)` exposes the user's restrictions,
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
  a rule that must hold within the same operation: objects use storage ports; a room manages its own channel, Chat
  a user's PM channel and each client's spectator channel; a room tells the lobby when it empties or fills;
  logging in opens the connection; deleting a user closes their connections at once, a security rule that
  must not wait for a handler.
* **Application publishes events and handles the ones that concern its own module; Infrastructure only ships them**
  (plan `module-events-plan-20261027.md`, D5-D8, supersedes "Application does not consume events"). An object or the
  bot's voice publishes an event through `IEventPublisher<T>` (`Publish`): it stamps `Timestamp`, queues the delivery
  and returns at once. A handler is a class implementing `Mediator.INotificationHandler<TEvent>` for one concrete
  event type (`ConnectionHandler`, `RestrictionHandler`, `ScoreHandler`, `MatchRecorder` in Application.Runtime;
  `RoomAnnouncer`, `AnticheatAnnouncer`, `ChatHandler` in `Basil.Bot`). One shared queue (`EventQueue`, owned by Application and run by
  `EventDispatcher`, the one hosted service Infrastructure.Runtime adds for it) delivers the events one at a time in the order they were published,
  across all event types; a failing handler is logged and the others still run. The mediator resolves handlers at
  first publish, not when the publisher is built (a handler reaches objects that publish). Persistence and Services
  never publish (a service returns what happened and the object publishes it). One operation publishes one event
  carrying its whole consequence. Events are `sealed record`s in a category tree (`Event` <- `RoomEvent` <-
  `RoomSlotEvent` <- `RoomSlotTeamChanged`), but a handler serves one concrete type. A handler only reacts to an
  event by calling the owning object's operation; event -> client-notification routing belongs to the hosts.
* **Handlers are quick; slow work goes to its own queue.** The queue awaits its handlers one after another, so a
  handler that waits (a lookup, a room, the store) holds up every later event of the server. A handler that may
  wait only queues the work on a bounded queue of its own, with a cap per user and a timeout per item, and returns
  (`CommandWorker` of the bot). A dropped item is logged, never silent.
* **A consequence reaches everyone, through every path.** What a change does to the people it concerns it does
  whether they are online or not (a restriction removes an offline referee from the open rooms too), and every
  path that grants the same authority applies the same gate (`ScopedRoom` and `ChannelRules.InRoom` agree on
  `RoomManageAny` and `RoomJoin`).
* **Naming: Domain record `XRecord` (with `XData`), the Application object `X`, the object that holds the live
  ones `XRegistry`; a service is an internal `XService` with no contract.** There is no `*Session`
  suffix: the Application object is the thing itself, and Domain only stores records. No suffixes such as
  "Definition". `GeneralChannelRecord` → `GeneralChannel` → `GeneralChannelRegistry` (it holds only general
  channels); `UserRecord` → `User` → `UserRegistry`. The osu! terms win for matches: `MatchRecord` → `Room` →
  `Lobby`. The room's channel is named after the room (`RoomChannel`) because the room is where things happen and
  `MatchRecord` is only the record. `Channel` shares its simple name with the static class
  `System.Threading.Channels.Channel`: a file that uses both qualifies the .NET one.
* **Event names are `{Group}{Subject}{PastTenseVerb}`.** The group is the category the event belongs
  to (`Room`, `Channel`, `User`, `Lobby`); when the subject is the group itself it is written once
  (`ChannelOpened`, `UserRestricted`). A subject that belongs to another is written `{Parent}{Child}`
  (`RoomRoundPlayerLoaded`, `RoomSlotTeamChanged`, `UserConnectionOpened`). The abstract category
  records follow the same rule (`RoomRoundEvent`, `ChannelMembershipEvent`).
* **Chat channels.** Domain stores only what is persisted: `GeneralChannelRecord` (identity: the name with its
  `#`) and `GeneralChannelData` (`Topic`, `ReadPrivileges`, `WritePrivileges`, `AutoJoin`, `Visible`); messages
  are the record `Message`. Application: abstract `Channel` (`Name`, `Topic`, members, IRC convention) with
  `GeneralChannel` (wraps the record), `RoomChannel`, `SpectatorChannel`, `PmChannel`; each names itself
  (`#mp_5`, `#spec_7`, the owner's name for a PM channel). **Each channel lives with the object that manages
  it**: `GeneralChannelRegistry` holds the general channels, a `Room` its room channel, `PmChannels` the PM
  channel of each online user, `SpectatorChannels` the spectator channel of each watched `BanchoConnection`. Each kind of
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
  replay; screenshots get a UUIDv7 name.
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
  class (`BanchoFlagsGroups.CheatSigns`, `UserPermissionsGroups.Chat`) or of the owning type (`UserRestriction.Silence`),
  never a combination member of the enum itself; a per-mode factory is a static member of its type
  (`BeatmapObjects.NewFrom(mode)`);
  services use them instead of keeping their own copies.
* **One concept, one name.** Use the same name for the same concept in every model, parameter,
  event and API: the moment something happened is `Timestamp`, a span is `StartedAt`/`EndedAt`, a
  future window is `StartsAt`/`EndsAt`, a stored record's lifecycle is
  `CreatedAt`/`UpdatedAt`/`DeletedAt`, the caller of an operation is `by`. Do not introduce
  `OccurredAt`, `When`, `Since`, `LoginTime` or similar synonyms.


### BasilBot replies

* Everything the bot says is text the operator can edit: one JSON file, `Data/Bot/replies.json`, keys to ICU
  MessageFormat strings with named placeholders (`{room}`, plural selectors). One language. The defaults are embedded
  in the assembly and written to the data directory when the file does not exist. The file is watched and reloaded
  while the server runs; an invalid file keeps the last good set and logs a warning; a missing key falls back to its
  default. Reply keys are typed constants owned by the bot; tests reference the keys, never copy the default text.
* A reply to a refused command names the missing permissions from `Result<TOutcome>.Missing`.
