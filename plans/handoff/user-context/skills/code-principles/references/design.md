# Design principles

These principles describe how thnhmai06 designs domains and applications. The examples use one neutral
domain throughout, an online meeting platform: users sign in from several devices (connections), meet in
live rooms, chat in channels and keep recordings. Treat the examples as illustrations, not names to copy.

## Contents

- [Models, storage and services](#models-storage-and-services)
- [The acted-on object owns the interaction](#the-acted-on-object-owns-the-interaction)
- [Peers talk through events, parents call children](#peers-talk-through-events-parents-call-children)
- [Child services by part](#child-services-by-part)
- [Events](#events)
- [Setters and operations](#setters-and-operations)
- [Derive what is derivable](#derive-what-is-derivable)
- [Identity and references](#identity-and-references)
- [Knowledge lives on the value](#knowledge-lives-on-the-value)
- [Naming](#naming)
- [Contracts, interfaces and abstractions](#contracts-interfaces-and-abstractions)
- [Errors](#errors)
- [Concurrency](#concurrency)
- [The general case](#the-general-case)
- [Permissions and restrictions](#permissions-and-restrictions)
- [Background work and timers](#background-work-and-timers)
- [Imported content](#imported-content)
- [User-visible text is a contract](#user-visible-text-is-a-contract)

## Models, storage and services

Three roles, never mixed:

- **A model represents.** A domain model guarantees that **it is valid**: values in range, its own text
  format, values derived purely from its own data. It holds no runtime-only state, emits no events, calls no
  service.
- **Storage stores.** Repositories, registries and storages hold data and provide lookups and concurrency
  scopes. They never decide a rule, emit an event, call a service or start a timer. Runtime models (live
  objects that exist only while the process runs, such as a room or a session) are data too: they live with
  storage and only services change them.
- **A service acts.** `XService` holds every action on its kind of object and guarantees the change **is
  meaningful**: authority, timing, domain rules, not tampered with, and the consequences of the change.

Consequences:

- A class named like a model (`Room`, `Inbox`) holds no logic.
- Services are only for actions. Code outside the core (infrastructure, hosts, other clients) reads, lists,
  searches and writes plain records **directly through storage**, after checking the caller's permission. A
  service method that only forwards to a repository (`RecordingService.GetAsync` calling
  `IRecordingRepository.GetAsync`) is wrong.
- Runtime state changes only through services. Runtime setters are `internal` to the service layer.
- The service of an object's kind enforces its rules in the same operation. A rule of that same kind of
  object (a room closes when its last participant leaves) is enforced by the operation that causes it, never
  left to a hook that might not be installed.
- A change to **another kind** of object is a reaction, not a rule of this one, even when it must always
  happen: player ratings after a game ends, a usage record after a room closes. It goes through an event and
  a handler (see the next sections). The runtime module always registers its handlers and the pump always
  delivers, so the reaction isn't optional; the handler just has to be safe to run again.

## The acted-on object owns the interaction

An actor (a user, a session, any active object) only *calls* an action. For every relation, member and
event, ask **"who is acted on?"**:

- The relation is stored on the object acted on (`ChatChannel.Members`, `Room.Participants`).
- The operation lives in the service of that object's kind (`channels.Join(channel, by)`,
  `rooms.Participants.JoinAsync(room, by, passcode)`).
- The event belongs to that object's category (`ChannelMemberJoined` is a channel event, emitted by the
  channel service).

So the user does **not** keep a `Channels` set and does **not** have a `Join(ChatChannel)` method.
Otherwise every new joinable kind (channel, room, live stream…) forces a new `Join*` method and a new set on
the user. "Which channels is X in?" is a query over channels, not state on X.

## Peers talk through events, parents call children

- Two services are **peers** when neither owns the other and neither's rule depends on the other (rooms and
  billing, moderation and chat). The acting service only emits its event. A dispatcher outside the core
  delivers it to the peer by calling the peer's contract: the room service emits `RoomClosed`, and a handler
  calls `IBillingService.RecordUsageAsync`.
- A service calls another directly only for a clear parent/child or ownership relation (a service and its
  storage, a room and its own chat channel), or to keep a rule that must hold within the same operation
  (deleting a user closes their connections at once, as a security rule that must not wait for a handler).
- **Each service manages its own kind and reacts to other kinds only through events.** The session service
  opens and closes connections and announces that. Leaving rooms when a connection closes is a handler's job,
  and that handler calls the room service.
- Cleanup is safe to run twice. When a second instance arrives while an owner still holds an old one, the
  owner **asks the old object whether it is still alive** (`connection.IsOpen`): if it is, refuse the new one;
  if not, remove the old one as an ordinary leave and admit the new one.

## Child services by part

**A service whose object has parts gets one child service per part.** "If it has children inside, split it."

- The parent contract exposes each child as a property named after the part: `IRoomService.Participants`,
  `.Settings`, `.Recordings`. Callers reach a child only through the parent.
- **The object stays a parameter of every operation**: `rooms.Recordings.StartAsync(room, by)`. Don't build an
  object-bound handle (`rooms.For(room).Recordings.Start()`).
- Name a child after the parent and the part: `IRoomRecordingService`. Children share the parent's one event
  stream, and may call a sibling's `internal` members directly, because they act on the same object.
- **A service whose object has no parts it acts on stays flat.** Size alone is no reason to split: a long
  service with one responsibility is still one service.
- The same applies one level down: a child may have a child of its own (`ISessionService.Connections` with
  its own `Presence`).

## Events

- The core emits events; it does not consume them. Handlers, dispatchers and event-to-notification routing
  live outside the core.
- Each service is one event source. When the platform already provides the storage and the reading API
  (`Channel<T>` with `ChannelReader<T>.ReadAllAsync`), use it directly. Don't wrap it in an `EventLog` class or
  read it with a polling loop. One dispatcher per stream fans out to its handlers.
- **One operation emits one event for its change, carrying the consequences that belong to that change.**
  Don't emit a generic `SettingsChanged` plus three detail events. When a leave also hands the host role to
  someone else, emit one `RoomParticipantLeft` that carries the new host, not `RoomParticipantLeft` followed by
  `RoomHostChanged`.
- **A milestone reached by the operation is an event of its own.** When the operation also reaches a state
  that other handlers subscribe to in its own right, emit that event too, right after the first: the move that
  ends a game emits `GameMovePlayed`, then `GameEnded`. A later, separate operation (closing the empty room
  after a delay) emits its own event at its own time.
- Events form a nested category tree (`Event`, then `RoomEvent`, then `RoomParticipantEvent`, then
  `RoomParticipantMuted`), so a consumer can subscribe to a whole category.
- Name events `{Group}{Subject}{PastTenseVerb}`. When the subject is the group itself, write it once
  (`ChannelOpened`, `UserRestricted`). A subject that belongs to another is written `{Parent}{Child}`
  (`RoomRecordingStarted`, `UserConnectionOpened`). Abstract category records follow the same rule
  (`RoomParticipantEvent`).
- An event carries its `Timestamp`, and its actor `By` when someone other than its subject acted (a kick
  carries `By`; a participant who joins on their own is the subject and the actor at once).
- The streams, pumps, handlers and their registration are shown in [architecture.md](architecture.md).

Microsoft: [System.Threading.Channels](https://learn.microsoft.com/dotnet/core/extensions/channels). These
domain events are records on a channel, not C# `event` members, so
[Event design](https://learn.microsoft.com/dotnet/standard/design-guidelines/event) applies only to UI and
component events.

## Setters and operations

- **A setter only assigns and validates.** It stores the value and throws if the value is invalid. It never
  emits an event or changes anything else.
- **A change with consequences is a method that performs a real operation.** A consequence is an event,
  other fields changing, an authority check, or several fields grouped into one event. Restrict the setter so
  callers can't go around the method.
- **Don't build wrapper methods around a single field.** If changing a value has no consequence, make the
  property settable and set it. Write no `Change*`, `Rename` or `With*` method that only sets one field, and
  no immutable copy-with method to change a live object. A method earns its place by being a real operation.
- When a compile error names a "missing" method on a model, first check whether a property with the same
  meaning already exists, and use it.

Microsoft: [Property design](https://learn.microsoft.com/dotnet/standard/design-guidelines/property). It agrees
that a setter validates and keeps the previous value when it throws, and that properties are smart fields
without side effects.

## Derive what is derivable

- A value that can be computed from another object is not stored again. A runtime object that co-owns a
  property with its domain object forwards it (`Room.Title => Meeting.Title`). Members only the inner object
  owns (`Meeting.Id`, `CreatedAt`) are read through it (`room.Meeting.Id`).
- **A value computed only from an object's own data is a derived property of that object**, even when it
  encodes a business threshold: `Order.IsBig => Total > BigOrderThreshold`, `Order.Discount`. It needs
  nothing outside the object, so it belongs with the object's validity, not in a service. A service that is
  left with no state and no dependencies after such moves becomes a `static class`.
- **Text follows the same test.** A fixed format that the business defines is a derived value and may live
  on the model: an order line `{CustomerName} - {Total}`, or a badge `{Name} ({Age})` that the product
  defines as is. A natural-language message that only tells a person something ("An error occurred, please
  try again.") never lives on the model. It belongs to the surface that shows it, as a named constant or a
  localized resource, so its wording and language can change without touching the model (see
  [User-visible text is a contract](#user-visible-text-is-a-contract)).
- Don't copy ids or fields from a referenced object. Hold the reference.

## Identity and references

- **Identify by the basis object, not its id.** A room is identified by its meeting, a session by its user,
  a channel by its name. Ids are for repository lookups only (id, then repository, then object).
- **Inside the system, operations take objects; ids belong at the API boundary.** An operation that other
  modules of the same process call takes the objects (`rooms.Participants.KickAsync(room, participant, by)`).
  A member that faces the outside (an HTTP endpoint, a remote API, a public entry point other processes call)
  may take ids. It resolves them into objects through storage, answers "not found" there, and works with
  objects from then on.
- **Runtime objects that can be recreated compare by reference.** A connection is one sign-in, a session is
  one period online, a runtime channel is one opening. Cleanup that carries an old object can then never
  touch its replacement. "Is this user already here?" compares the user, which is a separate check from
  object identity.
- **Relations are references** in a set: a concurrent set type, or a `HashSet<T>` guarded by its owner's
  lock. Never a `ConcurrentDictionary<T, byte>` used as a set, and never a list of ids.
- **Choose what to hold by asking about lifetime.** If the information is gone once the subject goes away
  (offline), store the short-lived object (the connection: the current presenter, a participant's place). If
  it survives, store the long-lived one (the user: the organizer, invitees, bans).
- **Owned parts are created and destroyed with their owner.** A session's private-message channel isn't a
  relation; it is part of the session.
- **A user online is one session, and each place they sign in from is a connection.** The session
  represents the user wherever they connect and holds what is shared (away message, private channel, active
  restrictions). Its connections are child data keyed by kind (`Web`, `Mobile`, `Kiosk`, `Api`), each holding
  only what exists for that kind of client. Rooms and channels hold connections, not sessions. A kind that
  allows several connections per user is a rule the session service owns. Each connection is one sign-in with
  an opaque token (kept in memory, no absolute expiry) and closes after a kind-specific idle period.
- **Keep types typed.** Never a `string` where a `ContentHash` exists. Choose object or identifier
  deliberately: a room holds the `Recording` it plays, while a client status holds the recording's
  `ContentHash?` because the server may not have that file.

## Knowledge lives on the value

Knowledge about a domain value lives on that value, and services use it instead of keeping their own copy:

- A fixed set of flags is a member of its enum (`Permissions.SuspendedByMute`, `ReportReason.NeedsReview`).
- A per-variant factory is a static member of its type (`RoomLayout.DefaultFor(kind)`).
- Key normalization lives on the value (`UserData.NormalizedName`). Storage writes the normalized key it
  looks records up by.

## Naming

- **One concept, one name**, in every model, parameter, event and API:

  | Concept                                  | Name                                    |
  |------------------------------------------|-----------------------------------------|
  | The moment something happened            | `Timestamp`                             |
  | A span that happened                     | `StartedAt` / `EndedAt`                 |
  | A future window                          | `StartsAt` / `EndsAt`                   |
  | A stored record's lifecycle              | `CreatedAt` / `UpdatedAt` / `DeletedAt` |
  | The caller of an operation               | `by`                                    |

  Never introduce synonyms such as `OccurredAt`, `When`, `Since` or `LoginTime`.
- **Name a contract after the object it acts on.** If the unit that is uploaded, stored, read and mirrored
  is the recording, the contracts are `IRecordingService`, `IRecordingReader`, `IRecordingStorage`,
  `IRecordingMirror`, and the result is `RecordingImportResult`.
- **Contract names don't reveal the mechanism.** If the work moved from the file system to the network, or
  from one library to another, the name would stay the same. Use `ScanAsync`, not `ScanFileAsync`, and
  `IRecordingAssets.OpenAsync`, not `ReadFromDiskAsync`. An implementation may name its mechanism
  (`FileHelpArticleStorage`, `PostgresUserRepository`).
- **No medium words and no redundant qualifiers.** A byte storage is `I{Owner}{Thing}Storage`
  (`IUserAvatarStorage`, `IRoomBackgroundStorage`), not `IUserAvatarImageStorage`. Use
  `ISettingsRepository`, not `IServerSettingsRepository`. No `Definition`, `Info` or `Data` suffix unless it
  carries meaning.
- **Name a family of types consistently.** For example: the domain model `X`, the runtime model `XSession`,
  the holder of live sessions `XRegistry`, and the contract `IXService` with its implementation `XService`
  (`Channel`, `ChannelSession`, `ChannelRegistry`, `IChannelService`). The domain's own terms win over
  generic ones.
- **A small, fixed set of verbs per kind of contract**, used everywhere. For repositories:
  `CreateAsync(XData)` returning `X` (the store assigns the identity) with `UpdateAsync(X)` for a record
  created that way (nothing is created under an id the store hasn't given out), `GetAsync(id)`,
  `GetByYAsync(y)` for a unique key, `ListAsync(query, page)` returning a `Page<X>`, `DeleteAsync(X)`, and
  `RecordAsync` for appends. `CreateOrUpdateAsync(X)` is only for a record keyed by what it is about
  (credentials by user, a rating by user and game mode). For byte storages: `SaveAsync`, `OpenAsync` and
  `DeleteAsync`, plus `ListAsync` and `RenameAsync` where the content is managed. `OpenAsync` serves the
  system's own reads, never clients, who get stored assets from the asset host. For text storages:
  `ReadAsync`, which returns a `StreamReader`, and `CreateOrUpdateAsync`, which returns a `StreamWriter`
  whose text replaces the entry when it is disposed. Soft deletion is a service setting `DeletedAt` and
  calling `UpdateAsync`.
- **One repository contract per model** (`IMeetingRepository`, `IRecordingRepository`), declaring the
  operations its callers need. A relation between two models that is stored on its own gets its own
  repository rather than a column on either (`IRoundScoreRepository` relates a round to a score and keeps the
  team it was set for).
- **Key normalization and uniqueness.** Normalization lives on the Domain value, and storage writes the
  normalized key it looks records up by. A value unique across a table (a user's normalized name, a content
  hash) is rejected with one exception type (`AlreadyExistsException`; a rename returns `false`) when
  memory already holds it, and otherwise by the database's unique violation.
- **A reference-carrying name drops the type suffix**: `From`, not `FromUser` or `FromUserId`.
- Use full words (`Privilege`, not `Priv`) and favour readability over brevity.

Microsoft: [Naming guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/naming-guidelines),
[General naming conventions](https://learn.microsoft.com/dotnet/standard/design-guidelines/general-naming-conventions),
[Names of type members](https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-type-members).

## Contracts, interfaces and abstractions

- **No interface with only one implementation**, with two deliberate exceptions:
  - **Capability contracts across a module boundary.** When other modules or projects use a service, it gets
    a contract `IXService` that lists every public action of the service and its event stream. The contracts
    are the list of capabilities the module offers, independent of how they are implemented. Inside a single
    project, where nothing crosses a module boundary, a service is a `sealed class` used directly.
  - **Ports to what lies outside the process**: storage, files, network, external computation, and the clock
    (`TimeProvider`). A capability with business meaning whose implementation needs an external library or
    resource gets a contract (`IRecordingAnalyser`, `IRecordingMirror`), even if only the hosts use it.
- Pure plumbing gets no contract: logging, diagnostics, metrics, TLS, update checks, OpenAPI, response
  envelopes.
- Implementations behind contracts are `internal sealed` and registered by their module's `AddX()`. Callers
  depend on the contract. Coordination that isn't a public capability stays an `internal` member, called
  inside the module through the concrete class.
- **Don't wrap what the platform already does.** `Channel<T>` already stores events and is thread-safe, so
  an `EventLog` class around it is redundant. Respect a removal the user already made: don't recreate a type
  they deleted to fix a compile error.
- Build no parallel generic ports (`ICreatable<TData, TValue>`) and no generic query system (`Query<T>`,
  `SortOptions`). Each repository declares the operations its callers need. Extract a shared interface only
  when several repositories truly share an operation.
- A query record (the filter a repository applies) is data and implements `IParsable<T>`, so the search text
  is its syntax. It filters only by explicit criteria, and every criterion, a state included, is written in
  the search text (`deleted=1`, `hidden=0`, `ended=0`, `private=1`, flag masks as numbers such as
  `permission=4`). A state is a `bool?`: `null` (written `*`) means both, and is the default when the text
  has no token for it. Every caller narrows the query to the asker before using it,
  `UserQuery.Parse(text).LimitTo(effective)`: `LimitTo` sets each state the asker may not see to what a user
  without that permission sees (`Deleted = false`, `Hidden = false`). A query used without `LimitTo` shows
  everything, so only the system's own work (a scan, startup recovery) skips it.

Microsoft: [Interface design](https://learn.microsoft.com/dotnet/standard/design-guidelines/interface),
[Architectural principles](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/architectural-principles)
(dependency inversion, explicit dependencies),
[Dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines).

## Errors

Follow Microsoft's Framework Design Guidelines on
[exception throwing](https://learn.microsoft.com/dotnet/standard/design-guidelines/exception-throwing) and
[exceptions and performance](https://learn.microsoft.com/dotnet/standard/design-guidelines/exceptions-and-performance).
Look them up when in doubt.

- **Exceptions report execution failures and usage errors.** A member throws when it can't do what its name
  promises because of an invalid argument, a broken invariant, an environment failure or a bug. Document the
  exceptions that are part of a member's contract.
- **Don't use exceptions for the normal flow of control.** When failure is a *common, expected outcome*
  (wrong password, a full room, a name already taken, a refused join), design the member so ordinary callers
  never need `try`/`catch`:
  - the **Try pattern**: `bool TryX(..., out T result)`, or `bool` from an operation such as a rename that
    returns `false` when the name is taken;
  - the **Tester-Doer pattern**: a cheap check before the operation;
  - an **outcome** type (`JoinResult`, `ChannelJoinResult`, `LoginAttempt` with a `LoginFailure` reason) when
    the caller must branch on *why*, for example because the protocol answers each reason differently. The
    outcome is part of the member's meaning, like the `bool` of a Try method. It is not a generic error
    code.
- The Try or outcome form covers only the well-defined expected failure. Any other failure still throws.
- Never return or `out` an exception object. Never make a member throw or not throw depending on an option.
- Tell "suspended" apart from "never granted": an outcome distinguishes `Muted` from `NotAuthorized`.

## Concurrency

- Shared mutable state has **one** owner of its synchronization (for example a room-scoped lock obtained
  from `room.EnterAsync()`, which returns `null` once the room is closed). Never add a second mechanism for
  the same state.
- **The lock belongs to the object whose state it guards.** Each room has its own scope. Don't put one global
  lock in a service to guard many independent objects, because it serializes unrelated rooms.
- A lock is runtime state, so it lives on the runtime object (the live room) or on the registry or repository
  that holds the live objects (`games.EnterAsync(game)`), never inside a domain model.
- Hold that scope across a complete read-then-mutate transition, including the work the mutation implies.
  Never hold it across long-lived or unrelated waits.
- Operations that may race with cleanup are idempotent. Check-then-act on shared state happens inside the
  scope.

## The general case

- Build the most general case, not a pile of special-case fixes. A special identity (a fixed system id, a
  "system" actor, a magic connection kind) is a smell. Model it as an ordinary participant with ordinary
  permissions.
- **Every action has a real actor** (`by`). It is never `null` and never a stand-in for "the server".
- Decide authority from what the caller may do (their effective permissions at `now`), never from the kind
  of client or transport they came through. The kind of client only decides what that client can technically
  do.
- Check the effective state (granted permissions minus active suspensions), not the stored grant.
- Expiry needs no timer when you compute the state at `now`.

## Permissions and restrictions

- **Permissions are the source of truth for what an account may do.** They are fine-grained actions grouped
  by the coarse privilege each group shows as to clients (`ParticipantCreateRoom`, `ParticipantChat`,
  `ModeratorMute`, `OwnerManageAccounts`; each group also has a mask member named after it). Only
  high-level management permissions may be broad (`RoomManageAny`). Don't add a permission that is
  merely a consequence of another. The privileges shown to clients are never stored or configured: derive
  them from the effective permissions (a bit is on when any permission of its group is).
- **A larger permission contains the smaller ones it implies, as bits,** not as a separate grant: a manage
  permission includes the matching view permission, even across groups (`RoomObserveAll` holds
  `ParticipantWatch`). A group's mask then keeps only its own group's bits, so holding the smaller permission
  never shows as the larger group's privilege. A check asks for the smallest permission that allows the
  action, so the manager and a user granted only that permission both pass. Never write
  `Allows(A) || Allows(B)` for a pair where one contains the other.
- **A check requires every bit** of what it asks for (`Permissions.Allows`); an empty requirement is always
  satisfied. Checks use the **effective** permissions: the granted ones minus those suspended by the
  user's active restrictions at `now`.
- **A restriction is a timed record, not an account field.** It suspends a mask of permissions from
  `StartsAt` to `EndsAt`, open-ended until lifted. Lifting sets `EndsAt` and keeps the history. A mute is a
  restriction of the permissions that let a user speak. Restrictions never touch management permissions: a
  mask containing one is invalid, and a manager is withdrawn by changing their permissions. A restriction may
  suspend a smaller permission a management permission contains, except from a user who holds that management
  permission in full. A result tells "suspended by a restriction" (`Muted`) apart from "never granted"
  (`NotAuthorized`).
- **Hierarchy.** A staff action aimed at another user (mute, restrict, lift, set permissions, set password,
  revoke sessions, delete) requires the actor's granted permissions to be a strict superset of the target's.
  Equals can't act on each other. Acting on oneself is not subject to this, except that nobody restricts,
  mutes or lifts a restriction of themselves. Granting gives only bits the actor holds.
- **Delegation is scope-based.** An account with a delegation permission may act for another online user: the
  operation runs with that user's identity and is attributed to them, but only their permissions within the
  delegation's scope count, and it joins no channel. The caller's rank doesn't matter. The service enforces
  the scope; the host only forwards the delegated connection.
- **Permissions are never implicit.** Nobody is granted them for being first or for any other side effect; a
  new account gets the default permissions, and more only by being granted.
- **Never decide authority by connection kind** (`ConnectionType`, `is MobileConnection`). The kind of
  client only decides what that client can technically do.

## Background work and timers

- A contract declares **one run** of a background job (`CloseIdle()`, `ScanAsync()`). The service implements
  that run. The host or infrastructure owns the loop, the trigger, the period and its configuration.
- A one-shot timer that is the consequence of an operation (closing an empty room after a delay, a countdown)
  stays with that operation, on `TimeProvider`, so tests can control time.

Microsoft: [What is TimeProvider?](https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview).

## Imported content

Content that arrives as a file (an uploaded recording archive, a dropped folder) involves three kinds of
type with three owners:

- **The storage holds raw content** (`RecordingSource`: an archive, or a folder of one recording's files).
  It identifies content by a fingerprint whose cheap part (size, modification time) is known without reading
  it. The expensive part (the hash) is taken only when the cheap part differs from the one recorded.
- **A reader understands it** (`IRecordingReader`, producing `RecordingContent` whose files open on demand,
  with no `byte[]` copies) and owns the input checks. Only the recording service uses it.
- **Repositories hold the records** (`Recording`, `Take`), created from a data record and stored together in
  one transaction.

Rules:

- **One import is one unit** (`ImportAsync` takes one archive). A folder placed by hand is packed into an
  archive first. Anything else placed by hand (a loose file, an unknown archive type) isn't a source and is
  left as it is, as is an invalid source. A rejected upload leaves nothing behind. Two sources of one scan
  never become the same unit or share a member: the first found wins.
- **Reading never makes a file** (a missing one reads as absent). Derived files (a preview, a repacked
  archive) are made at index time, and the originals are kept exactly as given: the archive as given is the
  truth about the unit.
- **A startup scan runs before clients are served.** It checks every entry (cheap fingerprint first, then the
  hash), indexes what changed, makes missing derived files again and stores every record change in one
  transaction. Afterwards a watcher reports each change to the same scan for one entry.
- **Names written from untrusted input** avoid the names Windows keeps for devices (`CON`, `NUL.txt`, `COM1`);
  a derived file gets a `_` prefix. Anything being written (an upload, a derived file, a folder before it
  moves into place) lies under a top-level `.incoming_` name, which is never listed or watched, so moves stay
  on one volume. A folder being deleted is renamed with `.deleted_` first.
- **Understanding a file once is enough.** The analyser reads a content item once and keeps the understood
  file as long as that model instance lives (again when its hash changes), so changing a parameter reads no
  file. The Domain holds no such runtime content, and metadata stays in the database.

## User-visible text is a contract

- Text a user sees (chat replies, protocol messages) is public behaviour. Each surface owns its strings in one
  place (`CommandReplies`, `ProtocolMessages`), or in localized resources when the text is translated. Don't
  scatter literals across handlers, and don't put natural-language messages on models or in services.
- Changing the wording is a contract change. Update the named constant and its tests deliberately.
- Logs and internal diagnostics are not subject to this rule.
