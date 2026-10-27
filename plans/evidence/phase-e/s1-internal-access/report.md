# Internal access inventory: Basil.Application.Runtime and Basil.Application.Capabilities

Spike S1 of Phase E (`plans/phase-e-plan-20261028.md`). Read-only analysis: it names every
assembly-visible member (internal, protected internal, private protected) that is used from a
different module, so that after the split each one is either made public, moved, or listed.

## What was scanned

| item | value |
|---|---|
| assemblies | `Basil.Application.Runtime.dll`, `Basil.Application.Capabilities.dll` (Debug build) |
| method bodies scanned | 6873 |
| member and type references reported | 226 inside the two assemblies, 176 from the assemblies built outside them |
| distinct members and types | 76 |
| other assemblies scanned for references | `Basil.Bot`, `Basil.Testing`, `Basil.Application.Runtime.Tests` |

Method: the IL of every method body is walked instruction by instruction; `call`, `callvirt`, `newobj`,
`ldfld`, `ldflda`, `stfld`, `ldsfld`, `stsfld`, `ldftn`, `ldvirtftn`, `ldtoken` and every type in a
method, field or local signature (including generic instantiations) are resolved to a definition in the
two assemblies. A reference is reported when its definition is assembly-visible and its declaring type's
module differs from the module of the method that names it. Modules are the first namespace segment
under `Basil.Application.Runtime` / `Basil.Application.Capabilities`; `Connections` is counted as `Users`
(plan E4: one project). Two labels are not modules: `Root(Runtime)` is the composition in
`src/Application/Runtime/DependencyInjection.cs`, which stays a composing project (E6), and
`Root(Capabilities)` is `src/Application/Capabilities/DependencyInjection.cs`, which is split into one
registration method per module (E3..E5). `Generated` marks types the Mediator source generator or the C#
compiler emitted into the assembly (`Mediator.*`, `Microsoft.Extensions.DependencyInjection.*`,
`System.Text.RegularExpressions.Generated.*`, `<PrivateImplementationDetails>`): they belong to no
module and are regenerated into whichever assembly compiles them.

## InternalsVisibleTo today

| assembly | grants internals to | declared in |
|---|---|---|
| `Basil.Application.Runtime` | `Basil.Application.Runtime.Tests` | `src/Application/Runtime/Basil.Application.Runtime.csproj` |
| `Basil.Application.Capabilities` | `Basil.Application.Runtime` | `src/Application/Capabilities/Basil.Application.Capabilities.csproj` |

`Basil.Bot` and `tests/Support/Basil.Testing` are not in either list, and the scan of their built
assemblies finds no reference to an assembly-visible member. `Basil.Application.Runtime.Tests` is
the one consumer that does see internals today; every such use is listed under
"References from outside the two assemblies", with the module that declares the member, because
after the split that module's project must carry the grant.

## Summary

### Uses per module pair

| declaring module | used from module | distinct members | uses |
|---|---|---:|---:|
| Anticheat | Root(Runtime) | 1 | 1 |
| Beatmaps | Root(Runtime) | 5 | 9 |
| Beatmaps | Root(Capabilities) | 8 | 8 |
| Chat | Servers | 7 | 17 |
| Chat | Root(Runtime) | 5 | 12 |
| Chat | Generated | 16 | 49 |
| Events | Chat | 1 | 1 |
| Events | Multiplayer | 2 | 4 |
| Events | Users | 1 | 3 |
| Events | Root(Runtime) | 3 | 7 |
| Events | Generated | 2 | 10 |
| Multiplayer | Chat | 2 | 3 |
| Multiplayer | Root(Runtime) | 5 | 13 |
| Multiplayer | Generated | 4 | 28 |
| Scores | Root(Runtime) | 2 | 2 |
| Scores | Root(Capabilities) | 1 | 1 |
| Servers | Root(Runtime) | 3 | 7 |
| Users | Chat | 1 | 1 |
| Users | Servers | 6 | 17 |
| Users | Root(Runtime) | 6 | 15 |
| Users | Root(Capabilities) | 1 | 1 |
| Users | Generated | 2 | 6 |
| Root(Capabilities) | Root(Runtime) | 1 | 1 |

Modules with no cross-module use of an assembly-visible member: `Common`, `Content`.

### 15 most used members

| member | module | kind | uses | from modules |
|---|---|---|---:|---|
| `Basil.Application.Runtime.Multiplayer.MatchRecorder` | Multiplayer | type | 19 | Generated |
| `Basil.Application.Runtime.Chat.ChannelContext` | Chat | type | 18 | Servers, Root(Runtime) |
| `Basil.Application.Runtime.Connections.ConnectionContext` | Users | type | 16 | Servers, Root(Runtime) |
| `Basil.Application.Runtime.Events.QueuedNotificationPublisher` | Events | type | 10 | Root(Runtime), Generated |
| `Basil.Application.Runtime.Beatmaps.BeatmapsetContext` | Beatmaps | type | 5 | Root(Runtime) |
| `Basil.Application.Runtime.Events.EventPublisher` | Events | type | 5 | Root(Runtime) |
| `Basil.Application.Runtime.Events.HandlerStep.Run` | Events | method | 5 | Chat, Multiplayer, Users |
| `Basil.Application.Runtime.Multiplayer.LobbyContext` | Multiplayer | type | 5 | Root(Runtime) |
| `Basil.Application.Runtime.Multiplayer.Rooms.RoomBlocks` | Multiplayer | type | 5 | Root(Runtime) |
| `Basil.Application.Runtime.Servers.ServerContext` | Servers | type | 5 | Root(Runtime) |
| `Basil.Application.Runtime.Users.UserContext` | Users | type | 5 | Root(Runtime) |
| `Basil.Application.Runtime.Chat.ConnectionChannelHandler` | Chat | type | 4 | Generated |
| `Basil.Application.Runtime.Chat.ConnectionSpectatorChannelHandler` | Chat | type | 3 | Generated |
| `Basil.Application.Runtime.Chat.LobbyConnectionReleasedHandler` | Chat | type | 3 | Generated |
| `Basil.Application.Runtime.Chat.LobbyRoomClosedHandler` | Chat | type | 3 | Generated |

### Proposed dispositions

| disposition | members |
|---|---:|
| move | 48 |
| public | 28 |

`move` means the behaviour exercised at the call site belongs to the module that owns the member:
it moves there, the call disappears and the member stays internal. `public` means the member is
part of the module's contract for a caller that stays where it is. `ivt` means the member stays
internal and the consumer is granted internals; no member needs it, because every cross-module use
below is either a registration that moves into the owning module or a contract that becomes public.

### Uses that are not composition or code generation

The tables above also count registrations (`Root(Runtime)`, `Root(Capabilities)`) and the code the
Mediator generator emits (`Generated`). These rows are what one module really reaches for in the
module that declares the member.

| member | module | used from | disposition |
|---|---|---|---|
| `Basil.Application.Runtime.Chat.Channel.PostMessage` | Chat | Servers | move |
| `Basil.Application.Runtime.Chat.ChannelContext` | Chat | Servers | move |
| `Basil.Application.Runtime.Chat.ChannelContext.Channels` | Chat | Servers | move |
| `Basil.Application.Runtime.Chat.ChannelContext.Events` | Chat | Servers | move |
| `Basil.Application.Runtime.Chat.ChannelContext.Registry` | Chat | Servers | move |
| `Basil.Application.Runtime.Chat.GeneralChannel..ctor` | Chat | Servers | move |
| `Basil.Application.Runtime.Chat.GeneralChannelRegistry.Add` | Chat | Servers | move |
| `Basil.Application.Runtime.Events.HandlerStep.Run` | Events | Chat, Multiplayer, Users | public |
| `Basil.Application.Runtime.Events.HandlerStep.RunAsync` | Events | Multiplayer | public |
| `Basil.Application.Runtime.Multiplayer.Rooms.Room.Enter` | Multiplayer | Chat | move |
| `Basil.Application.Runtime.Multiplayer.Rooms.Room.Involves` | Multiplayer | Chat | public |
| `Basil.Application.Runtime.Connections.ConnectionContext` | Users | Servers | move |
| `Basil.Application.Runtime.Connections.ConnectionContext.Clock` | Users | Servers | move |
| `Basil.Application.Runtime.Connections.ConnectionContext.Events` | Users | Servers | move |
| `Basil.Application.Runtime.Connections.ConnectionContext.Registry` | Users | Servers | move |
| `Basil.Application.Runtime.Connections.ConnectionRules.AllowsMany` | Users | Chat | public |
| `Basil.Application.Runtime.Users.UserRegistry.OpenBanchoClient` | Users | Servers | move |
| `Basil.Application.Runtime.Users.UserRegistry.OpenBanchoClients` | Users | Servers | move |

## Members used across modules

One row per member. `access` is the declared accessibility of the member and of its declaring type.
`used by` lists the module and the method that names it, with the number of references in that method.
Counts include signature references and the synthesized members of a record (`Equals`, `GetHashCode`,
`Deconstruct`, property accessors), so a context held as a field of a record counts once per synthesized
member; the call site, not the count, decides the disposition.
`disposition` is one word, then the reason.

### Anticheat (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Runtime.Anticheat.AnticheatReporter..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — Registered by AddApplication; after E6 the Anticheat module registers its own reporter and the constructor stays internal. |

### Beatmaps (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetFileAudit` | type | internal | Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 1 | move — Registered only by AddApplicationCapabilities; the Beatmaps registration (E3) takes it over. |
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetFileWriter` | type | internal | Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 1 | move — Registered only by AddApplicationCapabilities; the Beatmaps registration (E3) takes it over. |
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetImporter` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1)<br>Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 2 | move — Registered by both compositions; both registrations move into Beatmaps (E3, E6). |
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetMatcher` | type | internal | Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 1 | move — Registered only by AddApplicationCapabilities; the Beatmaps registration (E3) takes it over. |
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetPlanner` | type | internal | Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 1 | move — Registered only by AddApplicationCapabilities; the Beatmaps registration (E3) takes it over. |
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetScanner` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1)<br>Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 2 | move — Registered by both compositions; both registrations move into Beatmaps (E3, E6). |
| `Basil.Application.Capabilities.Beatmaps.BeatmapsetStorer` | type | internal | Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 1 | move — Registered only by AddApplicationCapabilities; the Beatmaps registration (E3) takes it over. |
| `Basil.Application.Capabilities.Beatmaps.DifficultyAnalyser` | type | internal | Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 1 | move — Registered only by AddApplicationCapabilities; the Beatmaps registration (E3) takes it over. |
| `Basil.Application.Runtime.Beatmaps.BeatmapsetContext..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; the Beatmaps module constructs its own context after E6. |
| `Basil.Application.Runtime.Beatmaps.BeatmapsetContext` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (4) | 5 | move — Only the composition names the type; its registration moves into Beatmaps. |
| `Basil.Application.Runtime.Beatmaps.Beatmapsets..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; the Beatmaps module constructs it after E6. |

### Chat (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Runtime.Chat.Channel.PostMessage` | method | internal | Servers: `Basil.Application.Runtime.Servers.Server.SayAsync` (1) | 1 | move — Server.SayAsync posts as the bot into a channel it found; the permission-free say belongs to Chat, which should expose it instead of letting another module post as any user. |
| `Basil.Application.Runtime.Chat.ChannelContext..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Chat constructs its own context after E6. |
| `Basil.Application.Runtime.Chat.ChannelContext` | type | internal | Servers: `Basil.Application.Runtime.Servers.ScopedServerChannels.<chat>5__2` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext..ctor` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.<Chat>k__BackingField` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.Deconstruct` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.Equals` (2)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.GetHashCode` (2)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.get_Chat` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.set_Chat` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (7) | 18 | move — Servers holds it only to open and find channels; that behaviour moves into Chat and ServerContext keeps public collaborators. |
| `Basil.Application.Runtime.Chat.ChannelContext.Channels` | property | internal | Servers: `Basil.Application.Runtime.Servers.ScopedServerChannels.CreateAsync` (1) | 1 | move — ScopedServerChannels.CreateAsync opens a channel; opening moves into Chat. |
| `Basil.Application.Runtime.Chat.ChannelContext.Events` | property | internal | Servers: `Basil.Application.Runtime.Servers.Server.OpenChannel` (1) | 1 | move — Server.OpenChannel publishes ChannelOpened; Chat owns that event. |
| `Basil.Application.Runtime.Chat.ChannelContext.Registry` | property | internal | Servers: `Basil.Application.Runtime.Servers.Server.FindChannel` (1)<br>Servers: `Basil.Application.Runtime.Servers.Server.OpenChannel` (1) | 2 | move — Server.FindChannel and Server.OpenChannel look channels up; the lookup moves into Chat. |
| `Basil.Application.Runtime.Chat.ConnectionChannelHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (4) | 4 | public — Mediator emits the registration for every handler type it sees, and the composing project registers handlers from another assembly (S2 decides the boundary). |
| `Basil.Application.Runtime.Chat.ConnectionSpectatorChannelHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.GeneralChannel..ctor` | constructor | internal | Servers: `Basil.Application.Runtime.Servers.Server.OpenChannel` (1) | 1 | move — Server.OpenChannel builds the channel; opening a channel belongs to Chat. |
| `Basil.Application.Runtime.Chat.GeneralChannelRegistry.Add` | method | internal | Servers: `Basil.Application.Runtime.Servers.Server.OpenChannel` (1) | 1 | move — Server.OpenChannel registers the channel; that belongs to Chat. |
| `Basil.Application.Runtime.Chat.LobbyConnectionReleasedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.LobbyRoomClosedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.LobbyRoomOpenedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.PmChannels..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Chat constructs its own channels after E6. |
| `Basil.Application.Runtime.Chat.PmConnectionClosedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.PmConnectionOpenedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomChannels..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Chat constructs it after E6. |
| `Basil.Application.Runtime.Chat.RoomObserverJoinedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomObserverLeftHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomPlayerBannedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomPlayerJoinedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomPlayerKickedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomPlayerLeftHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.RoomSlotLockChangedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.SpectatorChannels..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Chat constructs it after E6. |
| `Basil.Application.Runtime.Chat.SpectatorJoinedHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Chat.SpectatorLeftHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |

### Events (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Runtime.Events.EventPublisher..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (1) | 1 | move — AddEvents constructs it; Events registers its own publisher after E6. |
| `Basil.Application.Runtime.Events.EventPublisher` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddCategory` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (3) | 5 | move — Only the composition names it; its registration moves into Events. |
| `Basil.Application.Runtime.Events.HandlerStep.Run` | method | internal | Chat: `Basil.Application.Runtime.Chat.ConnectionChannelHandler.Handle` (1)<br>Multiplayer: `Basil.Application.Runtime.Multiplayer.ConnectionLobbyHandler.Handle` (1)<br>Users: `Basil.Application.Runtime.Connections.ConnectionHandler.Handle` (1)<br>Users: `Basil.Application.Runtime.Connections.RestrictionHandler.Handle` (2) | 5 | public — Shared kernel helper the Chat, Multiplayer and Users handlers call for the same step reporting; it is stateless and its contract is the same in every module (the type becomes public with it). |
| `Basil.Application.Runtime.Events.HandlerStep.RunAsync` | method | internal | Multiplayer: `Basil.Application.Runtime.Multiplayer.ConnectionLobbyHandler.Handle` (1)<br>Multiplayer: `Basil.Application.Runtime.Multiplayer.RestrictionLobbyHandler.Handle` (2) | 3 | public — Same shared kernel helper as HandlerStep.Run (the type becomes public with it). |
| `Basil.Application.Runtime.Events.QueuedNotificationPublisher` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (1)<br>Generated: `Mediator.Internals.ContainerMetadata..ctor` (1)<br>Generated: `Mediator.Internals.ContainerMetadata.NotificationPublisher` (1)<br>Generated: `Mediator.Internals.NotificationHandlerWrapper`1._publisher` (1)<br>Generated: `Mediator.Mediator._notificationPublisher` (1)<br>Generated: `Mediator.Mediator.get_NotificationPublisher` (2)<br>Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 10 | public — Mediator stores the configured publisher by concrete type in generated code that lands outside Common after the split. |
| `Basil.Application.Runtime.Events.QueuedNotificationPublisher.Publish` | method | internal | Generated: `Mediator.Internals.NotificationHandlerWrapper`1.Handle` (1) | 1 | public — Called by generated Mediator code compiled into the consuming assembly. |

### Multiplayer (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Runtime.Multiplayer.ConnectionLobbyHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Multiplayer.Lobby..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Multiplayer constructs it after E6. |
| `Basil.Application.Runtime.Multiplayer.LobbyContext..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Multiplayer constructs it after E6. |
| `Basil.Application.Runtime.Multiplayer.LobbyContext` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (4) | 5 | move — Only the composition names the type; its registration moves into Multiplayer. |
| `Basil.Application.Runtime.Multiplayer.MatchRecorder` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (19) | 19 | public — Mediator emits a registration for each message it observes (19 references); the type must be public when registered from another assembly. |
| `Basil.Application.Runtime.Multiplayer.RestrictionLobbyHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Multiplayer.Rooms.Room.Enter` | method | internal | Chat: `Basil.Application.Runtime.Chat.RoomChannels.Of` (1) | 1 | move — RoomChannels.Of takes the room lock and then joins members, taking channel locks inside it; the room should hand out a participant snapshot instead. |
| `Basil.Application.Runtime.Multiplayer.Rooms.Room.Involves` | method | internal | Chat: `Basil.Application.Runtime.Chat.RoomChannel.CanRead` (1)<br>Chat: `Basil.Application.Runtime.Chat.RoomChannel.CanWrite` (1) | 2 | public — A question the room answers about a connection; Chat asks it in RoomChannel.CanRead and CanWrite. |
| `Basil.Application.Runtime.Multiplayer.Rooms.RoomBlocks..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Multiplayer constructs it after E6. |
| `Basil.Application.Runtime.Multiplayer.Rooms.RoomBlocks` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (4) | 5 | move — Only the composition names the type; its registration moves into Multiplayer. |
| `Basil.Application.Runtime.Multiplayer.ScoreHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |

### Scores (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Capabilities.Scores.ScoreRecorder` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1)<br>Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 2 | move — Registered by both compositions; both registrations move into Scores (E3, E6). |
| `Basil.Application.Runtime.Scores.ScoreSubmitter..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Scores constructs it after E6. |

### Servers (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Runtime.Servers.Server..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Servers constructs it after E6. |
| `Basil.Application.Runtime.Servers.ServerContext..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Servers constructs it after E6. |
| `Basil.Application.Runtime.Servers.ServerContext` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (4) | 5 | move — Only the composition names the type; its registration moves into Servers. |

### Users (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Capabilities.Users.AccountService` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1)<br>Root(Capabilities): `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` (1) | 2 | move — Registered by both compositions; both registrations move into Users (E3, E6). |
| `Basil.Application.Runtime.Connections.ConnectionContext..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Users constructs it after E6. |
| `Basil.Application.Runtime.Connections.ConnectionContext` | type | internal | Servers: `Basil.Application.Runtime.Servers.Server.NotifyCore` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext..ctor` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.<Connections>k__BackingField` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.Deconstruct` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.Equals` (2)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.GetHashCode` (2)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.get_Connections` (1)<br>Servers: `Basil.Application.Runtime.Servers.ServerContext.set_Connections` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (5) | 16 | move — Servers holds it only to notify open clients; that behaviour moves into Users and ServerContext keeps a TimeProvider and public collaborators. |
| `Basil.Application.Runtime.Connections.ConnectionContext.Clock` | property | internal | Servers: `Basil.Application.Runtime.Servers.ScopedServer..ctor` (1)<br>Servers: `Basil.Application.Runtime.Servers.Server.SayAsync` (1) | 2 | move — ScopedServer and Server.SayAsync read the clock through it; after the move ServerContext holds a TimeProvider directly. |
| `Basil.Application.Runtime.Connections.ConnectionContext.Events` | property | internal | Servers: `Basil.Application.Runtime.Servers.Server.NotifyCore` (1) | 1 | move — Server.NotifyCore publishes UserNotificationSent; Users owns that notification. |
| `Basil.Application.Runtime.Connections.ConnectionContext.Registry` | property | internal | Servers: `Basil.Application.Runtime.Servers.Server.NotifyCore` (2) | 2 | move — Server.NotifyCore finds open clients; finding them belongs to Users. |
| `Basil.Application.Runtime.Connections.ConnectionHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Connections.ConnectionRules.AllowsMany` | method | internal | Chat: `Basil.Application.Runtime.Chat.ScopedChannel.Join` (1) | 1 | public — A rule of the connection kind that both login and channel membership ask (the type becomes public with it). |
| `Basil.Application.Runtime.Connections.RestrictionHandler` | type | internal | Generated: `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` (3) | 3 | public — Mediator registration, as ConnectionChannelHandler. |
| `Basil.Application.Runtime.Users.UserContext..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Users constructs it after E6. |
| `Basil.Application.Runtime.Users.UserContext` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (4) | 5 | move — Only the composition names the type; its registration moves into Users. |
| `Basil.Application.Runtime.Users.UserRegistry..ctor` | constructor | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — AddApplication constructs it; Users constructs it after E6. |
| `Basil.Application.Runtime.Users.UserRegistry.OpenBanchoClient` | method | internal | Servers: `Basil.Application.Runtime.Servers.Server.NotifyCore` (1) | 1 | move — Server.NotifyCore selects open clients; that loop belongs to Users. |
| `Basil.Application.Runtime.Users.UserRegistry.OpenBanchoClients` | method | internal | Servers: `Basil.Application.Runtime.Servers.Server.NotifyCore` (1) | 1 | move — Server.NotifyCore selects open clients; that loop belongs to Users. |

### Root(Capabilities) (declaring module)

| declaring type.member | kind | access | used by (module, type.method) | count | disposition |
|---|---|---|---|---:|---|
| `Basil.Application.Capabilities.DependencyInjection.AddApplicationCapabilities` | method | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddApplication` (1) | 1 | move — E3..E5 split it into one registration per module; AddApplication calls those instead. |

### Declaring types that must become public with a member

A `public` member is only legal when its declaring type is at least as accessible. These types are
`internal` today and declare a member marked `public` above, so they become `public` in the same
change.

| declaring type | module | access | public members |
|---|---|---|---|
| `Basil.Application.Runtime.Connections.ConnectionRules` | Users | internal | `AllowsMany` |
| `Basil.Application.Runtime.Events.HandlerStep` | Events | internal | `RunAsync`, `Run` |

## Types emitted into the assembly (not module members)

These are referenced from a module but are not declared by a module: the Mediator source generator and
the C# compiler emit them into whichever assembly compiles them, so the split regenerates them instead of
moving them. They are listed so that nothing is hidden, and carry no disposition.

| emitted type.member | kind | access | used by (module, type.method) | count |
|---|---|---|---|---:|
| `<PrivateImplementationDetails>.972A49A5FD8DD7AFABC951852FDF589EAB3AE256E489E3BABC59054113DD520E` | field | internal | Multiplayer: `Basil.Application.Runtime.Multiplayer.Rooms.RoomCountdown.MarksFor` (1) | 1 |
| `<PrivateImplementationDetails>.3AF3CEE73C2C497D47A1E8EF84B803E0DAA52F2DBF96904A4173CECA31564C2B` | field | internal | Multiplayer: `Basil.Application.Runtime.Multiplayer.Rooms.RoomCountdown.MarksFor` (1) | 1 |
| `Mediator.MediatorOptions.Mediator.MediatorOptions` | type | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (2)<br>Root(Runtime): `Basil.Application.Runtime.DependencyInjection (cached delegate)` (1) | 3 |
| `Mediator.MediatorOptions.ServiceLifetime` | property | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (1) | 1 |
| `Mediator.MediatorOptions.GenerateTypesAsInternal` | property | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (1) | 1 |
| `Mediator.MediatorOptions.NotificationPublisherType` | property | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (1) | 1 |
| `Microsoft.Extensions.DependencyInjection.MediatorDependencyInjectionExtensions.AddMediator` | method | internal | Root(Runtime): `Basil.Application.Runtime.DependencyInjection.AddEvents` (1) | 1 |
| `System.Text.RegularExpressions.Generated.<RegexGenerator_g>F807836F86EFFFE49C3D96548E6A01C3BFD9D8755636E53D6860BE12D68977C8B__ReservedPattern_0.Instance` | field | internal | Beatmaps: `Basil.Application.Capabilities.Beatmaps.StoredFileNames.ReservedPattern` (1) | 1 |

## Internal types that implement an interface from another module

None: no assembly-visible type of one module implements an interface declared in another module.

## References from outside the two assemblies

| consumer assembly | uses | distinct members |
|---|---:|---:|
| `Basil.Application.Runtime.Tests` | 176 | 63 |
| `Basil.Bot` | 0 | 0 |
| `Basil.Testing` | 0 | 0 |

A consumer sees an internal member only through an `InternalsVisibleTo` grant. After the split the
grant moves to the module project that declares the member, so the module column names the project
that must grant it to that consumer.

Not every member in these tables appears in the inventory above: a consumer also uses internal
members that no module uses across a boundary (a finder, a queue drain, a score entry point), and
the module that ends up declaring those must grant them too.

### Basil.Application.Runtime.Tests

| declaring type.member | module | kind | access | uses |
|---|---|---|---|---:|
| `Basil.Application.Runtime.Chat.ConnectionChannelHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.ConnectionChannelHandler` | Chat | type | internal | 5 |
| `Basil.Application.Runtime.Chat.ConnectionChannelHandler.Handle` | Chat | method | internal | 2 |
| `Basil.Application.Runtime.Chat.ConnectionSpectatorChannelHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.ConnectionSpectatorChannelHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.LobbyConnectionReleasedHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.LobbyConnectionReleasedHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.LobbyRoomClosedHandler..ctor` | Chat | constructor | internal | 3 |
| `Basil.Application.Runtime.Chat.LobbyRoomClosedHandler.Handle` | Chat | method | internal | 3 |
| `Basil.Application.Runtime.Chat.LobbyRoomOpenedHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.LobbyRoomOpenedHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.PmChannels.Find` | Chat | method | internal | 5 |
| `Basil.Application.Runtime.Chat.PmConnectionClosedHandler..ctor` | Chat | constructor | internal | 3 |
| `Basil.Application.Runtime.Chat.PmConnectionClosedHandler.Handle` | Chat | method | internal | 3 |
| `Basil.Application.Runtime.Chat.PmConnectionOpenedHandler..ctor` | Chat | constructor | internal | 2 |
| `Basil.Application.Runtime.Chat.PmConnectionOpenedHandler.Handle` | Chat | method | internal | 2 |
| `Basil.Application.Runtime.Chat.RoomChannels.Find` | Chat | method | internal | 2 |
| `Basil.Application.Runtime.Chat.RoomObserverJoinedHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomObserverJoinedHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomObserverLeftHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomObserverLeftHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomPlayerBannedHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomPlayerBannedHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomPlayerJoinedHandler..ctor` | Chat | constructor | internal | 2 |
| `Basil.Application.Runtime.Chat.RoomPlayerJoinedHandler.Handle` | Chat | method | internal | 2 |
| `Basil.Application.Runtime.Chat.RoomPlayerKickedHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomPlayerKickedHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomPlayerLeftHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomPlayerLeftHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomSlotLockChangedHandler..ctor` | Chat | constructor | internal | 1 |
| `Basil.Application.Runtime.Chat.RoomSlotLockChangedHandler.Handle` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.SpectatorChannels.Remove` | Chat | method | internal | 1 |
| `Basil.Application.Runtime.Chat.SpectatorJoinedHandler..ctor` | Chat | constructor | internal | 4 |
| `Basil.Application.Runtime.Chat.SpectatorJoinedHandler` | Chat | type | internal | 3 |
| `Basil.Application.Runtime.Chat.SpectatorJoinedHandler.Handle` | Chat | method | internal | 4 |
| `Basil.Application.Runtime.Chat.SpectatorLeftHandler..ctor` | Chat | constructor | internal | 2 |
| `Basil.Application.Runtime.Chat.SpectatorLeftHandler.Handle` | Chat | method | internal | 2 |
| `Basil.Application.Runtime.Events.EventPublisher` | Events | type | internal | 3 |
| `Basil.Application.Runtime.Events.EventPublisher.Publish` | Events | method | internal | 8 |
| `Basil.Application.Runtime.Events.EventQueue.DrainAsync` | Events | method | internal | 2 |
| `Basil.Application.Runtime.Events.EventQueue.Enqueue` | Events | method | internal | 9 |
| `Basil.Application.Runtime.Events.IEventDelivery` | Events | type | internal | 3 |
| `Basil.Application.Runtime.Multiplayer.ConnectionLobbyHandler..ctor` | Multiplayer | constructor | internal | 1 |
| `Basil.Application.Runtime.Multiplayer.ConnectionLobbyHandler` | Multiplayer | type | internal | 5 |
| `Basil.Application.Runtime.Multiplayer.ConnectionLobbyHandler.Handle` | Multiplayer | method | internal | 2 |
| `Basil.Application.Runtime.Multiplayer.MatchRecorder..ctor` | Multiplayer | constructor | internal | 1 |
| `Basil.Application.Runtime.Multiplayer.MatchRecorder` | Multiplayer | type | internal | 2 |
| `Basil.Application.Runtime.Multiplayer.MatchRecorder.Handle` | Multiplayer | method | internal | 10 |
| `Basil.Application.Runtime.Multiplayer.RestrictionLobbyHandler..ctor` | Multiplayer | constructor | internal | 1 |
| `Basil.Application.Runtime.Multiplayer.RestrictionLobbyHandler` | Multiplayer | type | internal | 7 |
| `Basil.Application.Runtime.Multiplayer.RestrictionLobbyHandler.Handle` | Multiplayer | method | internal | 4 |
| `Basil.Application.Runtime.Multiplayer.Rooms.Room.ReceiveScore` | Multiplayer | method | internal | 27 |
| `Basil.Application.Runtime.Multiplayer.Rooms.RoomBlocks` | Multiplayer | type | internal | 3 |
| `Basil.Application.Runtime.Multiplayer.Rooms.RoomBlocks.BlocksAsync` | Multiplayer | method | internal | 5 |
| `Basil.Application.Runtime.Multiplayer.ScoreHandler..ctor` | Multiplayer | constructor | internal | 2 |
| `Basil.Application.Runtime.Multiplayer.ScoreHandler` | Multiplayer | type | internal | 1 |
| `Basil.Application.Runtime.Multiplayer.ScoreHandler.Handle` | Multiplayer | method | internal | 2 |
| `Basil.Application.Runtime.Connections.ConnectionHandler..ctor` | Users | constructor | internal | 1 |
| `Basil.Application.Runtime.Connections.ConnectionHandler` | Users | type | internal | 1 |
| `Basil.Application.Runtime.Connections.ConnectionHandler.Handle` | Users | method | internal | 1 |
| `Basil.Application.Runtime.Connections.RestrictionHandler..ctor` | Users | constructor | internal | 1 |
| `Basil.Application.Runtime.Connections.RestrictionHandler` | Users | type | internal | 3 |
| `Basil.Application.Runtime.Connections.RestrictionHandler.Handle` | Users | method | internal | 2 |

## What this scan cannot see

* **Reflection.** `Type.GetType`, `GetMethod`, `Activator.CreateInstance`, `MakeGenericType` and
  `Assembly.GetTypes` name a type in a string or a `Type` value whose use is invisible in IL. The
  solution builds a container with `ValidateOnBuild`, so a missing registration would surface at run
  time, not here; `typeof(` and `nameof(` on internal types need a separate grep.
* **Expression trees.** A lambda converted to `Expression<...>` compiles to data, not to `call`
  instructions; `Expression.Property`, `Expression.Call` and the `Expression<T>` body are invisible.
* **Generated code that is not in the assembly.** The Mediator generator's output is scanned because it
  ends up in the assembly, but the generator's own resolution rules (which handler a message maps to,
  and which assembly's `AddMediator` wins) are not visible; `GenerateTypesAsInternal` is a build-time
  decision, and the split changes which assembly generates the registrations (spike S2).
* **`dynamic`, `DispatchProxy`, `Reflection.Emit` and serializers** that construct members from metadata
  at run time (System.Text.Json with a non-public constructor or setter, for example).
* **`nameof` and string literals** naming a member, and XML docs, are not references.
* **Delegates.** `ldftn` is attributed to the method that creates the delegate, which is where the
  compile error appears; the later invocation is not attributed to the declaring module.
* **A use from a type the scan cannot place.** Types outside both project namespaces are reported as
  `Generated`; a third-party type compiled into the assembly would be hidden there.
* **Attributes and attribute arguments** are metadata, not instructions: a type or member named only in
  an attribute is not reported.
* **`InternalsVisibleTo` consumers outside the build** (the Bot and host test projects, load tests) are
  only scanned when their assemblies are built; the hosts do not build today, so `src/Hosts` is not
  covered.
* **The inverse direction of a signature**: a public member that would leak an internal type is a
  compiler error at split time, not a use, so it is not counted here.
* **Whether a disposition compiles.** A `move` is a reading of the call site, not a built result: it
  names where the behaviour belongs, and E3..E6 must verify that the receiving module can expose it
  without a new internal use in the other direction.

## How to regenerate

The project is not in `Basil.slnx`. Build the assemblies whose references are read, one at a time,
then run the tool; it rewrites this file from `dispositions.tsv`.

```bash
dotnet build src/Application/Runtime --nologo -v q
dotnet build src/Application/Capabilities --nologo -v q
dotnet build src/Bot --nologo -v q
dotnet build tests/Support/Basil.Testing --nologo -v q
dotnet build tests/Application/Runtime --nologo -v q
dotnet run --project spikes/InternalAccess
```

