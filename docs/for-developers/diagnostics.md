# Diagnostics

## Overview

Basil runs as a private tournament server, usually operated by one person watching a match rather than by a fleet behind
an APM vendor. When something looks wrong during a live tournament -- the process using more memory than expected,
requests slowing down, a match stuck -- there is no external monitoring stack to check. Diagnostics let an operator (or a
client built on Basil, or the terminal dashboard) ask the running process directly.

It is deliberately not a general-purpose metrics or tracing system. It reports the process's own resource usage and Basil's
own live counts, nothing more. Exporting metrics to an external collector is a separate, optional feature (the `Telemetry`
settings of [`configuration.md`](../for-technicians/configuration.md)).

> **Migration status.** The public contract below is current. The design under "Design" is the target:
> `Application.Diagnostics` and `Infrastructure.Diagnostics` do not exist yet. Today `RuntimeSampler`, `BasilMeter`,
> `DiagnosticsSample` and `ICacheCleaner` live in `Basil.Host.Shared`, the sample reads the live objects directly, and the
> permission gate is `ScopedServerDiagnostics` in the Servers module.

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
| `application` | users online, open rooms, rounds in progress and open live streams |
| `sessions` | open connections by type (osu, tourney, irc, api, ...) |
| `osu-stable`, `irc` | open connections of that protocol |
| `database`, `events` | not measured yet: they answer `available: false` |

## Why a shared sample

Many watchers must not multiply the cost of looking. The process is measured once a second; the sample is serialized once
and every stream of the same category receives the same bytes, so a tenth subscriber costs the sending of bytes, not
another measurement. The terminal dashboard (`Basil.Host.Cli`) draws the same sample, so what the operator sees on the
machine and what the API serves cannot disagree.

## Design (target)

Diagnostics is a module, `Application.Diagnostics` and `Infrastructure.Diagnostics`, that depends on Users and Common. It
offers two services.

* **Intake** (`IDiagnosticsIntake`) receives measurements. There are three sources: .NET `Meter` instruments collected
  through OpenTelemetry, contributions that modules answer when asked (`CollectDiagnostics`), and a Mediator pipeline
  behavior that measures every message.
* **Feed** (`IDiagnosticsFeed`) distributes samples: `Watch` returns an asynchronous sequence of immutable
  `DiagnosticsSample` values. Each subscriber gets its own `Channel` of capacity one with `DropOldest`, so a slow watcher
  sees the newest sample and never delays the others. The feed is **distribution, not storage**: it keeps no history.

The contribution contracts (`CollectDiagnostics`, `IDiagnosticsCollector`, `IClearTally`, `ICacheCleaner`,
`DiagnosticsValue`, `DiagnosticsSample`) live in Application.Common so every module can contribute without referencing
Diagnostics.

Every module's Infrastructure project owns a `Meter` named `Basil.<Module>` and a handler for `CollectDiagnostics`. Counts of
connections are labelled by connection type (`stable`, `tourney`, `irc`, `api`) and are emitted where Users opens and
closes a connection. The bot's own server-owned connections are not counted as clients, and a type with zero connections still reports zero.
Rejected connections are counted by type and reason (`basil.connections.rejected`). Labels have a bounded set of values.

The maintenance actions (`caches/clear`, `exceptions/reset`, `gc/collect`) are Commands that run through the capability of
the Diagnostics module, which checks `ServerManageDiagnostics` before anything is sampled or changed. The routes hold no
permission rule of their own, and the console is the one entry point that skips the check. The thirty-second minimum between
`gc/collect` calls is an admission policy of the gateway ([`gateway.md`](gateway.md)).

Request durations come from one instrument, `basil.http.request.duration`, tagged with the host label and the status class.
Exceptions are counted from the runtime's first-chance notifications.

## Testing

`tests/Hosts/Rest/Sse/DiagnosticsTests.cs` covers the HTTP contract: the refusals without a token (`401`) and without the
permission (`403`), a stream's self-seeded first event for every category with one category filtered, the bundle each
second and the supported actions (an unknown action is `404`). `tests/Hosts/Shared/RuntimeSamplerTests.cs` covers the
sampler, and the dashboard's use of the sample is tested under `tests/Hosts/Cli/Dashboard`. Target tests: every
Infrastructure project has a `Basil.<Module>` meter and a `CollectDiagnostics` handler (with a positive control), and the
collector reports every connection type, including zero.

## Related code

* [`DiagnosticsRoutes.cs`](../../src/Hosts/Rest/Sse/DiagnosticsRoutes.cs): the routes and the actions
* [`DiagnosticsSample.cs`](../../src/Hosts/Shared/Diagnostics/DiagnosticsSample.cs): the sample of every category (moves to Diagnostics)
* [`RuntimeSampler.cs`](../../src/Hosts/Shared/Diagnostics/RuntimeSampler.cs): process and runtime measurements (moves)
* [`BasilMeter.cs`](../../src/Hosts/Shared/Diagnostics/BasilMeter.cs): the request-duration instrument (moves)
* [`ScopedServerDiagnostics.cs`](../../src/Application/Servers/Capabilities/ScopedServerDiagnostics.cs): today's permission gate (moves to Diagnostics)

## See also

* [`sse.md`](sse.md): the live-updates pipeline diagnostics build on
* [`gateway.md`](gateway.md): the admission policies that count rejections
* [`configuration.md`](../for-technicians/configuration.md): telemetry export and the permissions that open diagnostics
