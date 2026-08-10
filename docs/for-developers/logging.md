# Logging

## Overview

Basil uses [Serilog](https://serilog.net/) as its application logging pipeline.

Logging is configured in code in `Basil.Web/Program.cs`. Application logs are enriched with a category and, where applicable, request or connection scopes so that developers can trace a business operation across the layers that handle it.

The logging system has two distinct responsibilities:

* provide useful human-readable logs for development and operation;
* attach structured context so individual requests, packets, connections, and domain resources can be correlated.

Operational details such as log locations and changing the minimum level are documented in [`logging.md`](../for-technicians/logging.md).

## Logging configuration

Most logging decisions are intentionally code-defined.

The configured sinks, rolling policy, file layout, category rules, and enrichment are part of the application's implementation rather than `appsettings.json`.

The only logging behavior exposed through configuration is the minimum log level:

```json
{
  "Basil": {
    "Logging": {
      "MinimumLevel": "Information"
    }
  }
}
```

This setting controls the minimum level written to stdout and the full application log.

The error log is independent of this setting and always receives `Error` and `Fatal` events.

Keeping the rest of the configuration in code makes changes to logging behavior explicit and reviewable alongside the implementation that produces the logs.

## Sinks

Basil writes to three primary destinations:

| Sink           | Minimum level | Purpose                             |
| -------------- | ------------- | ----------------------------------- |
| Console        | Configurable  | Development and live process output |
| `Logs/full/`   | Configurable  | Complete application log            |
| `Logs/errors/` | `Error`       | Errors and fatal failures only      |

The file sinks roll daily and retain 30 days of logs.

Two fixed filenames provide stable paths to the currently active files:

```text
Logs/latest.log
Logs/errors_latest.log
```

These are hardlinks to the corresponding currently-open dated log files.

A consumer can therefore tail `latest.log` without having to determine the current date or track log rotation itself.

## Categories

Every log event is assigned a category by `CategoryEnricher`.

The category is derived from the namespace of the class producing the log rather than being manually specified at every call site.

The currently recognized application categories include:

* `Mapsets`
* `Matches`
* `Scores`
* `Online`
* `IRC`
* `Database`
* `Cache`
* `Host`
* `Api`

Unclassified application code falls back to `App`.

`App` is intentionally treated more conservatively and defaults to Warning-and-above. This prevents unrelated framework or infrastructure messages from filling the Information-level application log without an explicit category decision.

The important rule for new code is therefore:

**A new subsystem should either naturally map to an existing category or have its category explicitly added to `CategoryEnricher`.**

Do not introduce ad-hoc category names at individual logging call sites.

## Structured scopes

Basil uses logging scopes to attach correlation information to all events produced during an operation.

### HTTP requests

Every HTTP request receives a `RequestId` scope.

Logs produced while handling the request therefore contain the same identifier, allowing a request to be followed across middleware, routing, application services, and infrastructure code.

### Bancho packets

A dispatched Bancho packet carries relevant context such as:

* `UserId`
* `PacketType`
* `MatchId`, when applicable

This makes it possible to distinguish concurrent packets that otherwise produce identical log messages.

For example, a generic packet-processing message becomes useful when combined with its structured context:

```text
UserId=42 PacketType=SendMessage MatchId=7
```

The packet handler itself should not need to manually repeat these values in every message.

### IRC connections

A live IRC TCP connection receives a `ConnectionId` scope.

All messages produced while processing that connection can therefore be correlated without embedding the connection identifier into every log string.

## Domain-event markers

Logs that represent a resource lifecycle transition use a prefix marker:

| Marker | Meaning  |
| ------ | -------- |
| `+`    | Created  |
| `-`    | Removed  |
| `~`    | Modified |

Examples include:

```text
+ Mapset ...
~ Match ...
- User ...
```

These markers are intended to make state transitions easy to find with ordinary text search.

They should be used for **business state changes**, not for every informational log line.

For example, successfully handling an HTTP request is not itself a domain event. Creating a match or ingesting a beatmapset is.

## Logging business events

A business event should generally be logged at the shared service method where the state transition occurs.

For example:

```text
HTTP route
    \
Bancho packet ---> MatchControlService ---> state change
    /                    |
!mp command              +-- log once
```

The service is the correct logging boundary because all entry points converge there.

Do not log the same state transition independently in:

* an HTTP route;
* a packet handler;
* a command handler;
* the shared application service.

Otherwise one action can produce several apparently independent events.

The preferred rule is:

> **Log a business event once, at the layer where the business state actually changes.**

Entry points may still log transport-specific failures or diagnostics when those details are useful, but they should not duplicate the business event.

## Log levels

Choose the level according to what the event means, not according to which subsystem produced it.

Typical usage is:

* `Trace`: extremely detailed execution information that is normally too noisy to retain.
* `Debug`: diagnostic information useful when investigating a specific behavior.
* `Information`: normal application lifecycle and business events.
* `Warning`: an abnormal condition that does not prevent the operation from continuing.
* `Error`: an operation failed and requires attention.
* `Fatal`: the application cannot continue safely.

The configured minimum level may be lowered during development or troubleshooting.

Do not compensate for a missing log level by logging everything at `Information`. A noisy Information log makes the normal application lifecycle harder to understand and reduces the value of the category and scope system.

## Logging and exceptions

Logs should provide enough context to understand **what operation failed** and **which resource was involved**.

Prefer structured scope data and concise messages over constructing large strings containing every available value.

For example, a service handling a match operation should rely on its existing `MatchId` scope where available rather than repeatedly embedding the match id into unrelated log messages.

When an exception represents a failure that the application is already handling, log it at the appropriate boundary rather than repeatedly logging the same exception as it propagates through the layers.

This follows the same one-event principle used for business events.

## Hardlinked current-log files

The `latest` files are maintained by `HardLinkFileLifecycleHooks`.

They are filesystem links to the active rolling file rather than separately written copies:

```text
Logs/
├── full/
│   ├── 2026-08-10.log
│   └── ...
├── errors/
│   ├── 2026-08-10.log
│   └── ...
├── latest.log
└── errors_latest.log
```

A hardlink gives both filenames access to the same underlying file.

This avoids writing each log event twice merely to maintain a convenient stable filename.

The lifecycle hook updates the link when the active rolling file changes.

## Invariants

The logging implementation relies on several architectural rules:

* Application logging goes through Serilog.
* The minimum level for stdout and the full log comes from `Basil:Logging:MinimumLevel`.
* The errors log always records `Error` and `Fatal`, regardless of that configured minimum.
* Categories are assigned centrally by `CategoryEnricher`.
* Request, packet, and IRC correlation data is carried through logging scopes.
* Domain lifecycle changes use `+`, `-`, or `~` markers.
* A business state transition is logged once at the shared service boundary rather than once per transport.
* Derived context should come from structured properties and scopes where possible instead of being duplicated in message text.
* `latest.log` and `errors_latest.log` are filesystem links, not independently written log streams.

## Related code

* [`Basil.Web/Program.cs`](../../src/Basil.Web/Program.cs) (`ConfigureSerilog`): Serilog pipeline and sink configuration
* [`Basil.Web/Logging/CategoryEnricher.cs`](../../src/Basil.Web/Logging/CategoryEnricher.cs): category assignment
* [`Basil.Web/Logging/HardLinkFileLifecycleHooks.cs`](../../src/Basil.Web/Logging/HardLinkFileLifecycleHooks.cs): active-file hardlink lifecycle
* [`Basil.Infrastructure/System/HardLink.cs`](../../src/Basil.Infrastructure/System/HardLink.cs): filesystem hardlink implementation

## See also

* [`../for-technicians/logging.md`](../for-technicians/logging.md): log locations, retention, and operational configuration
* [`architecture.md`](architecture.md): application-layer boundaries and service responsibilities
* [`testing.md`](testing.md): testing conventions for behavior that may otherwise be over-coupled to log text
