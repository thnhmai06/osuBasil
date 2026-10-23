# Architecture: layers and events

This is the architecture thnhmai06 uses for a backend with a domain, live runtime state, persistent storage
and several transports. It puts the principles of [design.md](design.md) into projects: models are valid,
storage stores, services act, and peers talk through events. The examples continue the online meeting
platform of design.md. For a small library or tool, keep the roles and the dependency direction; you don't
need every project.

## Contents

- [Projects and dependencies](#projects-and-dependencies)
- [What each layer may hold](#what-each-layer-may-hold)
- [The event pipeline](#the-event-pipeline)
- [Skeleton](#skeleton)
- [Rules](#rules)
- [Scaling down](#scaling-down)

## Projects and dependencies

Arrows point to what a project references.

```text
Domain                                business models and their own validity       → nothing
Application.Storage.Contracts         persistent ports, runtime models, registry    → Domain
                                      contracts, query records, paging
Application.Storage.Implementations   in-memory registries (internal)               → Storage.Contracts
Application.Services.Contracts        IXService contracts, capability ports,        → Storage.Contracts
                                      events, inputs and results
Application.Services.Implementations  services (internal)                           → Services.Contracts
                                                                                      (never Storage.Implementations)
Infrastructure.Storage                database, files, migrations                   → Storage.Contracts
Infrastructure.Services               capability ports built on external libraries  → Services.Contracts
Infrastructure.Runtime                event pumps and handlers, background loops,   → Services.Contracts
                                      startup work
Protocol.<Wire>                       wire formats to C# models and back            → external libraries only
Host.<Transport>                      one transport each (HTTP API, realtime hub,   → Services.Contracts, both
                                      a binary client protocol)                       Implementations (to register
                                                                                      them), Infrastructure, its Protocol
Host                                  composition root, configuration, logging      → everything
Clients (bots, tools, overlays)       features built on top, signing in as users    → Domain, or the API only
```

- **Implementations are `internal` and joined only by dependency injection.** Each one registers itself
  (`AddApplicationStorage()`, `AddApplicationServices()`, `AddInfrastructureRuntime()`). Everything else uses
  services and registries through their contracts.
- **Infrastructure references no Implementations project.** The hosts reference them only to call the `Add…()`
  methods.
- **The same feature folders recur in every project** (`Users/`, `Chat/`, `Meetings/`, `Recordings/`,
  `Billing/`). Namespace = project + folder.
- **Configuration is bound only in the composition root.** Infrastructure and transports receive
  `IOptions<T>` of an options type they own. Application reads no configuration.
- **Protocol is not a peer of Domain.** It turns wire bytes and strings into C# models of primitives and back,
  including undoing the client's own encoding. It doesn't validate and gives no business meaning. Only the
  transport hosts reference it, so a wire change touches only Protocol.
- **Features are clients, not the server.** Chat commands, announcements and overlays are separate programs
  that sign in as ordinary users and use the API. The server only emits events.
- Enforce the arrows with architecture tests.

## What each layer may hold

| Layer                    | Holds                                                                                                                                                                                                                                                         | Never holds                                                      |
|--------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------|
| Domain                   | Models that outlive the process (`Meeting`, `User`, `Recording`). Each guarantees it is valid and carries knowledge about its own values.                                                                                                                      | Runtime state, events, services, configuration                   |
| Storage.Contracts        | Repository and byte-storage contracts (`IMeetingRepository`, `IRecordingStorage`). Runtime models as concrete classes with `internal set` (`Room`, `UserSession`, `ChannelSession`). Registry contracts with `internal` mutators. Query records.                | Rules, events, timers, service calls                             |
| Storage.Implementations  | In-memory registries: data, lookups, concurrency scopes                                                                                                                                                                                                       | Anything outside the process                                     |
| Services.Contracts       | One `IXService` per kind of object, listing every action and extending `IEventPublisher<XEvent>`. The event category trees. Capability ports (`IRecordingAnalyser`). Inputs and outcomes.                                                                     | Plain reads or writes wrapped as "services"                      |
| Services.Implementations | Every action: authority, timing, domain rules, consequences, one event per operation                                                                                                                                                                         | Event handlers, dispatchers, notification routing, configuration |
| Infrastructure.Runtime   | One event pump per service stream; handlers that turn an event into a call on another contract, a stored record, or cleanup; the loops that run background jobs                                                                                             | Business rules                                                   |
| Hosts                    | Transport, authentication of the caller, mapping to and from Protocol, client notifications from events                                                                                                                                                      | Authority decisions by connection kind                           |

Runtime models change only through services. Make their setters `internal` and give both Implementations
projects `InternalsVisibleTo`.

## The event pipeline

```text
  caller ──▶ IRoomService.Participants.JoinAsync(room, by, passcode)
                 │  inside room.EnterAsync() scope: check, change state, then Emit
                 ▼
          RoomEventStream ── Channel<RoomEvent> (one per service, shared with its child services)
                 │  exposed as IRoomService.Events (IEventPublisher<RoomEvent>)
                 ▼
          EventPump<RoomEvent> (Infrastructure.Runtime, one BackgroundService per stream)
                 │  await foreach over ReadAllAsync, fan out to every IEventHandler<RoomEvent>
                 ▼
   MeetingHistoryRecorder (stores what happened)   ...   transport notifier (pushes to clients)

  Peer reaction:   IRoomService emits RoomClosed
                   → EventPump<RoomEvent> → BillingHandler → IBillingService.RecordUsageAsync(...)
```

1. **Event base and publisher contract** live in `Services.Contracts/Events`: `abstract record Event` and
   `IEventPublisher<T> { ChannelReader<T> Events { get; } }`.
2. **Each feature defines its category tree** next to its service contract: `RoomEvent : Event` with the
   `Timestamp`, then `RoomParticipantEvent : RoomEvent`, then sealed records such as
   `RoomParticipantJoined`. Names follow `{Group}{Subject}{PastTenseVerb}`. Each record carries the whole
   consequence of the operation (the new host after a leave, the duration when a room closes) and its actor
   `By` when someone other than the subject acted.
3. **Each service owns one stream**, an unbounded `Channel<T>` wrapped in a small `XEventStream` that its child
   services share. `Emit` stamps the `Timestamp` from `TimeProvider`. The operation emits after the state
   change, inside the object's scope, so the order of events is the order of changes.
4. **Infrastructure runs one pump per stream.** `EventPump<T>` reads the stream even when no handler is
   registered, so events don't pile up. It calls every handler of that category. A failing handler is logged
   and doesn't stop the others.
5. **Handlers react.** `IEventHandler<in T> where T : Event` receives every event of its category and
   switches on the concrete type. A handler calls another service's contract (a peer reaction), writes a
   record through a repository, or cleans up. When one handler serves two categories, register it once and
   expose it under both handler interfaces.
6. **Transports subscribe the same way** to turn events into client notifications. That routing lives in the
   host, never in Application.

## Skeleton

Adapt the names to the project.

```csharp
// Services.Contracts/Events
public abstract record Event;

public interface IEventPublisher<T> where T : Event
{
    ChannelReader<T> Events { get; }
}

// Services.Contracts/Meetings/Events
public abstract record RoomEvent(Room Room) : Event
{
    public DateTimeOffset Timestamp { get; init; }
}

public abstract record RoomParticipantEvent(Room Room) : RoomEvent(Room);

public sealed record RoomParticipantJoined(Room Room, Connection Participant) : RoomParticipantEvent(Room);

public sealed record RoomClosed(Room Room, TimeSpan Duration) : RoomEvent(Room);

// Services.Contracts/Meetings
public interface IRoomService : IEventPublisher<RoomEvent>
{
    IRoomParticipantService Participants { get; }
}

// Services.Implementations/Meetings
internal sealed class RoomEventStream(TimeProvider time)
{
    private readonly Channel<RoomEvent> _events = Channel.CreateUnbounded<RoomEvent>();

    internal ChannelReader<RoomEvent> Reader => _events.Reader;

    internal void Emit(RoomEvent @event) => _events.Writer.TryWrite(@event with { Timestamp = time.GetUtcNow() });
}

// Infrastructure.Runtime/Events
public interface IEventHandler<in T> where T : Event
{
    ValueTask HandleAsync(T @event, CancellationToken cancellationToken);
}

internal sealed class EventPump<T>(
    IEventPublisher<T> source,
    IEnumerable<IEventHandler<T>> handlers,
    ILogger<EventPump<T>> logger) : BackgroundService where T : Event
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var @event in source.Events.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        foreach (var handler in handlers)
            try
            {
                await handler.HandleAsync(@event, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A handler for {Category} events failed", typeof(T).Name);
            }
    }
}

// Infrastructure.Runtime/Billing: a peer reaction
internal sealed class BillingHandler(IBillingService billing) : IEventHandler<RoomEvent>
{
    public async ValueTask HandleAsync(RoomEvent @event, CancellationToken cancellationToken)
    {
        if (@event is RoomClosed closed)
            await billing.RecordUsageAsync(closed.Room.Meeting, closed.Duration, cancellationToken).ConfigureAwait(false);
    }
}

// Infrastructure.Runtime/DependencyInjection.cs
services.AddSingleton<IEventHandler<RoomEvent>, BillingHandler>();
services.AddSingleton<MeetingHistoryRecorder>();
services.AddSingleton<IEventHandler<RoomEvent>>(sp => sp.GetRequiredService<MeetingHistoryRecorder>());
services.AddSingleton<IEventHandler<ChannelEvent>>(sp => sp.GetRequiredService<MeetingHistoryRecorder>());
AddPump<IRoomService, RoomEvent>(services);
AddPump<IChannelService, ChannelEvent>(services);

static void AddPump<TService, TEvent>(IServiceCollection services)
    where TService : class, IEventPublisher<TEvent>
    where TEvent : Event
{
    services.AddSingleton<IHostedService>(sp => new EventPump<TEvent>(
        sp.GetRequiredService<TService>(),
        sp.GetServices<IEventHandler<TEvent>>(),
        sp.GetRequiredService<ILogger<EventPump<TEvent>>>()));
}
```

A state transition on a shared runtime object holds that object's own scope from the check to the emit:

```csharp
var scope = await room.EnterAsync(cancellationToken).ConfigureAwait(false);
if (scope is null)
    return JoinResult.RoomClosed;

await using (scope.ConfigureAwait(false))
{
    // check authority and rules, change the room, then emit one event
}
```

## Rules

- **Application emits events; it never consumes them.** Storage never emits.
- **One operation emits one event for its change**, carrying the consequences that belong to it. A milestone
  the operation also reaches, with subscribers of its own (a game that ends with a move), is a second event
  emitted right after.
- **Peers talk only through events.** A service calls another directly only for a parent/child or ownership
  relation, or for a rule that must hold within the same operation. See
  [design.md](design.md#peers-talk-through-events-parents-call-children).
- **One stream per service, one pump per stream.** Each item of a `Channel<T>` reaches one reader, so nothing
  else reads `Events` directly. Fan-out happens in the pump.
- **Handlers are safe to run again** and guard each step, so one failed cleanup step doesn't skip the rest.
- **A contract declares one run of a background job** (`CloseIdle()`, `ScanAsync()`). Infrastructure.Runtime
  owns the loop, the period and its configuration. A one-shot timer that is the consequence of an operation
  stays with the operation, on `TimeProvider`.
- **Child services share their parent's stream** and may call a sibling's `internal` members directly, since
  they act on the same object. Callers reach a child only through the parent contract.
- **Outer layers read and write plain records through storage directly,** after the host has checked the
  caller's permission. Services are only for actions.
- **Every action has a real actor `by`,** and authority comes from the actor's effective permissions, never
  from the kind of connection.

## Scaling down

Not every project needs this many projects. Keep what carries meaning:

- **One process, few features:** a single project with feature folders. Keep the roles as types (model,
  repository, `XService`) and keep the dependency direction inside it (services never reference file-system
  or database types directly).
- **Peers that must react to each other:** keep the event stream, the pump and the handlers even in one
  project, because the alternative is services calling each other.
- **A library:** Domain, contracts and their implementation, plus a `DependencyInjection` class with
  `AddX()`. No runtime layer unless something runs in the background.

Microsoft: [Common web application architectures](https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
(clean architecture: the core defines interfaces, infrastructure implements them),
[System.Threading.Channels](https://learn.microsoft.com/dotnet/core/extensions/channels),
[Background tasks with hosted services](https://learn.microsoft.com/aspnet/core/fundamentals/host/hosted-services).
