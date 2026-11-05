# Diagnostics

## Overview

Basil runs as a private tournament server, usually operated by one person watching a match rather than by a fleet behind
an APM vendor. When something looks wrong during a live tournament -- the process using more memory than expected,
requests slowing down, a match stuck -- there is no external monitoring stack to check. Diagnostics let an operator (or a
client built on Basil, or the terminal dashboard) ask the running process directly.

It is deliberately not a general-purpose metrics or tracing system. It reports the process's own resource usage and Basil's
own live counts, nothing more. Exporting metrics to an external collector is a separate, optional feature (the `Telemetry`
settings of [`configuration.md`](../for-technicians/configuration.md)).

## What it provides

The diagnostics are streamed over Server-Sent Events, as every live route is (see [`sse.md`](sse.md)):

* `GET /v1/diagnostics` -- every category.
* `GET /v1/diagnostics/{category}` -- one category.
* `POST /v1/diagnostics/{category}/{action}` -- a maintenance action: `gc/collect`, `caches/clear`, `exceptions/reset`.

The first event of a stream is a full sample; after that a bundle arrives every second with the newest sample. The generated
API reference describes the routes; this page explains the design behind them.

Access needs the permission `ServerManageDiagnostics` of the signed-in user. A caller without a token is refused with
`401`, and one without the permission with `403`.

The categories are:

| Category | What it reports |
|---|---|
| `process` | CPU use, working set, private memory, handles and threads of the process |
| `gc` | heap size, generation sizes, collection counts, pause time and allocation rate |
| `threadpool` | thread count, pending work items and completion rate |
| `runtime` | version, operating system, build, uptime, CPU count and dropped log events |
| `exceptions` | first-chance exceptions per second, their total and the most recent ones |
| `http` | request statistics per host label (`c`, `osu`, `api`, ...) |
| `application` | users online, open rooms, rounds in progress, open live streams, open general channels and diagnostics watchers |
| `sessions` | open connections by type (osu, tourney, irc, api, ...) |
| `osu-stable`, `irc` | open connections of that protocol |
| `database`, `events` | not measured yet: they answer `available: false` |

## Why a shared sample

Many watchers must not multiply the cost of looking. The process is measured once a second; the sample is serialized once
and every stream of the same category receives the same bytes, so a tenth subscriber costs the sending of bytes, not
another measurement. The terminal dashboard (`Basil.Host.Cli`) draws the same sample, so what the operator sees on the
machine and what the API serves cannot disagree.

## Design

Diagnostics is a module, `Application.Diagnostics` and `Infrastructure.Diagnostics`, that depends on Users and Common. It
offers two services.

* **Intake** (`IDiagnosticsIntake`) receives measurements and turns them into a `DiagnosticsSample`. There are three
  sources: .NET `Meter` instruments (the request durations of the hosts are picked up by their agreed instrument name,
  and the process measurements by the runtime), contributions that modules answer when asked (`CollectDiagnostics`), and a
  Mediator pipeline behavior that records how long every request takes on the `Basil.Diagnostics` meter
  (`basil.mediator.message.duration`, tagged with the message type). Telemetry export sends every `Basil.*` meter to the
  collector when it is switched on.
* **Feed** (`IDiagnosticsFeed`) distributes samples: `Watch` returns an asynchronous sequence of immutable
  `DiagnosticsSample` values. A background run takes the sample once a second and gives it to the feed. Each watcher gets its
  own `Channel` of capacity one with `DropOldest`, so a slow watcher sees the newest sample and never delays the others,
  and a watcher receives nothing taken before it started. The feed is **distribution, not storage**: it keeps no history.

The HTTP stream shares one sample per second differently: the SSE hub polls the intake once per root and second, serializes
the result once and hands the same bytes to every stream of the root, so the cost does not grow with the watchers.

The contribution contracts (`CollectDiagnostics`, `IDiagnosticsCollector`, `IClearTally`, `ICacheCleaner`,
`DiagnosticsValue`, `DiagnosticsSample`) live in Application.Common so every module can contribute without referencing
Diagnostics. `CollectDiagnostics` is a question, not an event: the intake hands it directly to the handlers (each module
has one) and waits for the answers, so it never goes through the event queue.

Every module's Infrastructure project that has handlers owns a `Meter` named `Basil.<Module>` and a handler for
`CollectDiagnostics`; an architecture test fails when one is missing. Users reports the users online and the open
connections of every type (`stable`, `stabletourney`, `irc`, `api`), counting a type with no connection as zero and leaving
out the bot's own server-owned connections. Multiplayer reports rooms and rounds, Chat the open general channels
(`application.channels`) and Diagnostics its own watchers (`application.diagnosticsWatchers`); the API host reports the open
live streams. Modules without live state of their own (Beatmaps, Scores, Servers) answer with nothing. Rejected connections
are counted by type and reason (`basil.connections.rejected`). Labels have a bounded set of values and never carry an id or
a name.

The maintenance actions (`caches/clear`, `exceptions/reset`, `gc/collect`) are Mediator requests that carry the sender's
connection and run through `ScopedDiagnostics`, which checks `ServerManageDiagnostics` before anything is changed. The routes
and the console hold no permission rule of their own, and the console is the one entry point that skips the check. The
thirty-second minimum between `gc/collect` calls is still enforced by the API route with the gateway's rate limiter, after
the permission check and only for the API; the console is not limited. Making it an admission policy of the gateway
([`gateway.md`](gateway.md)) is not done yet.

Request durations come from one instrument, `basil.http.request.duration`, which the API host records for every web host,
tagged with the host label and the status class. Exceptions are counted from the runtime's first-chance notifications.

## Testing

`tests/Hosts/Rest/Sse/DiagnosticsTests.cs` covers the HTTP contract: the refusals without a token (`401`) and without the
permission (`403`), a stream's self-seeded first event for every category with one category filtered, the bundle each
second and the supported actions (an unknown action is `404`). `tests/Infrastructure/Diagnostics` covers the feed (the
newest sample, dropped older ones, a slow watcher beside a fast one), the intake's merging, the sampler, the request
timing and the refusal of every maintenance request with its missing permission; `tests/Application/Diagnostics` covers the
capability. `tests/Infrastructure/Users/Handlers/CollectDiagnosticsHandlerTests.cs` pins that every connection type is
reported, including zero. `tests/Architecture/DiagnosticsContributionTests.cs` checks that every module with handlers has its
meter and handler (with a positive control), and the dashboard's use of the sample is tested under `tests/Hosts/Cli/Dashboard`.

## Related code

* [`DiagnosticsRoutes.cs`](../../src/Hosts/Rest/Sse/DiagnosticsRoutes.cs): the routes and the actions
* [`ScopedDiagnostics.cs`](../../src/Application/Diagnostics/Capabilities/ScopedDiagnostics.cs): the permission gate of the actions
* [`DiagnosticsIntake.cs`](../../src/Infrastructure/Diagnostics/Services/DiagnosticsIntake.cs) and [`DiagnosticsFeed.cs`](../../src/Infrastructure/Diagnostics/Services/DiagnosticsFeed.cs): the two services
* [`RuntimeSampler.cs`](../../src/Infrastructure/Diagnostics/Services/RuntimeSampler.cs): process and runtime measurements
* [`DiagnosticsSample.cs`](../../src/Application/Common/Diagnostics/DiagnosticsSample.cs): the sample of every category and the contribution contracts beside it

## See also

* [`sse.md`](sse.md): the live-updates pipeline diagnostics build on
* [`gateway.md`](gateway.md): the admission policies that count rejections
* [`configuration.md`](../for-technicians/configuration.md): telemetry export and the permissions that open diagnostics
