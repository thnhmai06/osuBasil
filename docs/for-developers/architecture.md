# Architecture

## Overview

Basil is a single deployable process organized into five projects with a strictly enforced dependency direction:

```text
Basil.Domain
     ↑
Basil.Application ← Basil.Protocol
     ↑
Basil.Infrastructure
     ↑
Basil.Web
```

More precisely:

```text
Domain      ← Application ← Infrastructure
Protocol    ← Application
Domain      ← Infrastructure

Domain      ← Web
Protocol    ← Web
Application ← Web
Infrastructure ← Web
```

`Basil.Web` is the composition root. It wires concrete Infrastructure implementations into Application abstractions and hosts the HTTP, Bancho, and IRC entry points.

The dependency direction is enforced by architecture tests and is part of the project's build contract.

---

## Projects and responsibilities

| Project                | References                                    | Responsibility                                                            |
| ---------------------- | --------------------------------------------- | ------------------------------------------------------------------------- |
| `Basil.Domain`         | None                                          | Domain types and pure business calculations                               |
| `Basil.Protocol`       | None                                          | Bancho wire-format packet encoding and decoding                           |
| `Basil.Application`    | Domain, Protocol                              | Application services, packet handlers, sessions, and Infrastructure ports |
| `Basil.Infrastructure` | Application, Domain                           | SQLite, filesystem, and external library implementations                  |
| `Basil.Web`            | Domain, Protocol, Application, Infrastructure | ASP.NET Core host, routing, transport integration, and composition root   |

### `Basil.Domain`

The innermost layer.

It contains concepts that do not require any application, transport, or infrastructure dependency:

* enums;
* records and value types;
* domain calculations;
* domain-level rules.

`Basil.Domain` must not reference another Basil project.

### `Basil.Protocol`

Contains the osu! Bancho wire-format implementation.

It is independent of the application and infrastructure layers so packet serialization can be tested without starting the server.

`Basil.Protocol` must not reference another Basil project.

### `Basil.Application`

Contains the server's application logic.

It includes:

* Bancho packet handlers;
* application services;
* session management;
* multiplayer logic;
* authentication;
* chat dispatch;
* interfaces representing Infrastructure capabilities.

Application code may depend on `Basil.Domain` and `Basil.Protocol`, but never directly on `Basil.Infrastructure` or `Basil.Web`.

When application code needs persistence, filesystem access, or another external capability, define or use an abstraction in `Basil.Application`.

### `Basil.Infrastructure`

Contains concrete implementations of Application-layer abstractions.

Examples include:

* SQLite persistence;
* Dapper repositories;
* filesystem storage;
* beatmap storage;
* other external-library integrations.

Infrastructure may depend on Application abstractions and Domain types, but Application must never depend on Infrastructure.

### `Basil.Web`

The outer host and composition root.

It contains:

* ASP.NET Core application startup;
* host and subdomain routing;
* Bancho HTTP transport;
* tournament API routes;
* IRC gateway integration;
* dependency-injection registration;
* configuration and host-level concerns.

Concrete Infrastructure services are wired to Application interfaces here.

---

## Dependency rule

The dependency direction is architectural, not merely a style preference.

The allowed direction is:

```text
Web
 ├── Application
 │    ├── Domain
 │    └── Protocol
 ├── Infrastructure
 │    ├── Application
 │    └── Domain
 ├── Protocol
 └── Domain
```

The following dependencies are prohibited:

```text
Domain      → Application / Infrastructure / Web
Protocol    → Application / Infrastructure / Web
Application → Infrastructure / Web
Infrastructure → Web
```

Do not bypass an Application abstraction simply because a concrete Infrastructure implementation is convenient.

For example, Application code must not directly create:

```text
SqlConnection
FileStream
Basil.Infrastructure repositories
```

Instead, the required capability belongs behind an Application-layer abstraction and is implemented by Infrastructure.

---

## Architecture tests

The dependency rule is enforced by:

```text
tests/Basil.ArchitectureTests
```

The tests use NetArchTest to verify project and namespace dependencies.

These tests run as part of CI. A new project reference that violates the dependency direction should therefore be treated as an architectural change, not as a normal implementation detail.

When adding a dependency, verify that it belongs in the layer being modified before changing the project references.

---

## Source layout

Basil is primarily organized by **feature**, rather than by technical type.

Namespaces follow the directory structure. A namespace therefore gives a reliable indication of where its implementation lives.

Important Application directories include:

| Directory       | Responsibility                                              |
| --------------- | ----------------------------------------------------------- |
| `Packets/`      | Bancho client packet types and handlers, grouped by feature |
| `Abstractions/` | Interfaces implemented by Infrastructure                    |
| `Sessions/`     | In-memory runtime state and session registries              |
| `Services/`     | Application/business logic grouped by feature               |

Examples of packet feature groups include:

```text
Packets/
├── Users/
├── Channels/
├── Spectating/
└── Multiplayer/
```

Runtime state is kept separate from persistent state:

```text
Sessions/
├── UserSession
├── GameSession
├── IrcSession
├── ChannelSession
└── MatchSession
```

Persistent state is handled through Infrastructure repositories and storage implementations rather than being placed inside these session objects.

---

## Sessions

Sessions represent runtime state and are not persisted.

The important distinction is between the different types of client session:

* [`UserSession`](../../src/Basil.Application/Sessions/UserSession.cs): base session state.
* [`GameSession`](../../src/Basil.Application/Sessions/GameSession.cs): an osu! game-client session.
* [`IrcSession`](../../src/Basil.Application/Sessions/IrcSession.cs): an IRC session.
* [`ChannelSession`](../../src/Basil.Application/Sessions/Channels/ChannelSession.cs): channel membership/runtime state.
* [`MatchSession`](../../src/Basil.Application/Sessions/Multiplayer/MatchSession.cs): multiplayer match state.

Do not introduce transport-specific state into a generic session when the state only exists for one transport.

In particular, IRC and game-client sessions have different lifecycles and should remain separate types.

See [`irc.md`](irc.md) for the dual-session model.

---

## Feature boundaries

When implementing a feature, keep its responsibilities in the appropriate layer.

A typical request path looks like:

```text
Transport
   ↓
Web / Protocol
   ↓
Application service or handler
   ↓
Application abstraction
   ↓
Infrastructure implementation
   ↓
External system
```

For example, a Bancho packet should not directly perform a SQL query:

```text
Packet handler
    ✗
    └── SqlConnection
```

Instead:

```text
Packet handler
    ↓
Application service
    ↓
Repository abstraction
    ↓
Infrastructure repository
    ↓
SQLite
```

This keeps transport handling independent from persistence.

---

## Adding features

### Adding a Bancho packet

Add the packet to the appropriate feature directory under:

```text
Basil.Application/Packets/
```

Then register it through the Application dependency-injection setup.

Composition-root tests verify that packet registrations are complete.

See [`bancho.md`](bancho.md) for packet dispatch.

### Adding persisted state

Prefer an existing Application abstraction when the required capability already exists.

For new persistence capabilities:

1. define the required interface under `Basil.Application/Abstractions/`;
2. implement it in Infrastructure;
3. register the implementation in the composition root;
4. keep database-specific code out of Application.

Repository implementations belong under:

```text
Basil.Infrastructure/Persistence/Repositories/
```

### Adding an HTTP endpoint

Add the route to the appropriate host group:

```text
Basil.Web/Routing/Bancho/
Basil.Web/Routing/Api/
Basil.Web/Routing/Assets/
```

Keep HTTP-specific concerns in Web and delegate application behaviour to Application services.

For the `api.` host, update the OpenAPI contract as part of the route implementation rather than maintaining a separate handwritten endpoint specification.

See [`response-envelope.md`](response-envelope.md) and the client-facing [`api.md`](../for-client/api/overview.md).

For an image-serving route, see [`assets.md`](assets.md) — `assets.<domain>` handles those through ImageSharp.Web rather than hand-written `Results.File` calls.

### Adding an IRC command

IRC commands are dispatched by:

```text
TcpIrcConnection.HandleRegisteredCommandAsync
```

The command should delegate to existing Application services where possible.

Do not move application/business rules into the IRC transport layer simply because the command originates there.

See [`irc.md`](irc.md).

### Adding chat routing logic

Chat routing belongs in:

```text
ChatDispatchService.SendPrivmsgAsync
```

Keep transport-specific encoding and connection handling outside the chat application logic.

See [`chat.md`](chat.md).

### Adding a new chat transport

Implement:

```text
IIrcConnection
```

The transport receives an [`IrcMessage`](../../src/Basil.Protocol/Irc/IrcMessage.cs) and is responsible for encoding it into its own wire representation.

Do not make [`ChatDispatchService`](../../src/Basil.Application/Services/Chat/ChatDispatchService.cs) aware of transport-specific protocols.

---

## Cross-cutting invariants

Several architectural rules are important enough to treat as implementation constraints:

* **Dependency direction is enforced by tests.**
* **Application does not reference Infrastructure.**
* **Domain and Protocol remain dependency-free.**
* **Runtime sessions are separate from persisted state.**
* **IRC and game-client sessions are separate types.**
* **Multiplayer read-modify-broadcast operations hold [`MatchSession.Lock`](../../src/Basil.Application/Sessions/Multiplayer/MatchSession.cs).**
* **Privilege is named `Privilege`, never `Priv`.**
* **User-visible reply strings are centralized rather than duplicated.**
* **PP calculation is deliberately not part of Basil's gameplay logic.**

See [`working-scopes.md`](working-scopes.md) for features that are intentionally outside Basil's scope.

---

## Where to go from here

| Topic                       | Documentation                                                                      |
| --------------------------- | ---------------------------------------------------------------------------------- |
| Bancho packet dispatch      | [`bancho.md`](bancho.md)                                                           |
| Authentication and login    | [`../for-client/bancho/authentication.md`](../for-client/bancho/authentication.md) |
| Multiplayer and match state | [`multiplayer.md`](multiplayer.md)                                                 |
| HTTP live updates           | [`sse.md`](sse.md)                                                                 |
| API response envelope       | [`response-envelope.md`](response-envelope.md)                                     |
| Database and persistence    | [`database.md`](database.md)                                                       |
| Beatmap ingestion           | [`beatmap-ingestion.md`](beatmap-ingestion.md)                                     |
| Chat                        | [`chat.md`](chat.md)                                                               |
| IRC architecture            | [`irc.md`](irc.md)                                                                 |
| Logging                     | [`logging.md`](logging.md)                                                         |
| Privileges                  | [`privileges.md`](privileges.md)                                                   |
| Development workflow        | [`development.md`](development.md)                                                 |
| Deployment                  | [`../for-technicians/deployment.md`](../for-technicians/deployment.md)             |
| Project scope               | [`working-scopes.md`](working-scopes.md)                                           |
