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
| `application` | users online, open rooms, rounds in progress and open live streams |
| `sessions` | open connections by type (osu, tourney, irc, api, ...) |
| `bancho`, `irc` | open connections of that protocol |
| `database`, `events` | not measured yet: they answer `available: false` |

## Why a shared sample

Many watchers must not multiply the cost of looking. The process is measured once a second by `RuntimeSampler`; the
application counts are read from the live objects (`UserRegistry`, `Lobby`, `SseHub`) in the same step by
`DiagnosticsSample`. The sample is serialized once and every stream of the same category receives the same bytes, so a
tenth subscriber costs the sending of bytes, not another measurement. The terminal dashboard (`Basil.Host.Cli`) draws the
same sample, so what the operator sees on the machine and what the API serves cannot disagree.

Request durations come from one instrument, `basil.http.request.duration`, published by `BasilMeter` and tagged with the
host label and the status class; `RuntimeSampler` listens to it. Exceptions are counted from the runtime's first-chance
notifications.

## Access rules

`ScopedServerDiagnostics` (reached with `server.ScopeTo(connection).Diagnostics`) checks `ServerManageDiagnostics` before
anything is sampled, for the stream and for the actions alike. The routes hold no permission rule of their own, and the
console is the one entry point that skips the check.

## Testing

`tests/Hosts/Rest/Sse/DiagnosticsTests.cs` covers the HTTP contract: the refusals without a token (`401`) and without the
permission (`403`), a stream's self-seeded first event for every category with one category filtered, the bundle each
second and the supported actions (an unknown action is `404`). `tests/Hosts/Shared/RuntimeSamplerTests.cs` covers the
sampler, and the dashboard's use of the sample is tested under `tests/Hosts/Cli/Dashboard`.

## Related code

* [`DiagnosticsRoutes.cs`](../../src/Hosts/Rest/Sse/DiagnosticsRoutes.cs): the routes and the actions
* [`DiagnosticsSample.cs`](../../src/Hosts/Shared/Diagnostics/DiagnosticsSample.cs): the sample of every category
* [`RuntimeSampler.cs`](../../src/Hosts/Shared/Diagnostics/RuntimeSampler.cs): process and runtime measurements
* [`BasilMeter.cs`](../../src/Hosts/Shared/Diagnostics/BasilMeter.cs): the request-duration instrument
* [`ScopedServerDiagnostics.cs`](../../src/Application/Servers/Capabilities/ScopedServerDiagnostics.cs): the permission gate

## See also

* [`sse.md`](sse.md): the live-updates pipeline diagnostics build on
* [`configuration.md`](../for-technicians/configuration.md): telemetry export and the permissions that open diagnostics
