# Logging

## Why

The old server logged every packet and request, which filled memory and made the log unreadable. Basil's log is a
record of the server's **lifecycle**: what started and stopped, who signed in, which room or round opened or closed,
moderation actions, console commands and failures. Per-request and per-packet work belongs to metrics and the
diagnostics, not to the log.

## What is logged

* start, stop and each phase of shutdown, including timeouts and a forced exit;
* login and logout of every connection type;
* a room opened or closed and a round started or ended;
* restrictions and other moderation actions;
* console commands (with passwords and keys masked);
* errors and warnings from any component.

Nothing logs each Bancho packet, IRC line or REST request. Those are counted by diagnostics, which the dashboard and
`/v1/diagnostics` read.

## How it is built

`src/Hosts/Host/Logging/ServerLogging.cs` configures [Serilog](https://serilog.net/):

* one rolling file, `Logs/basil-*.log`, UTF-8, rolled daily or at 256 MB and limited by `Logging:RetentionPeriod`
  (swept at startup and every six hours; a file is kept while it was written within the period);
* a console sink only in `--nogui` mode, plain text without colour;
* a dashboard log panel, a fixed ring buffer, in the full and `--noconsole` modes;
* optional OpenTelemetry log export when `Telemetry:Logs` is on and an endpoint is set.

Every sink is written by one background thread behind a bounded queue (`Logging:QueueCapacity`, 10 000 by default).
When the queue is full the newest event is dropped and counted; the count is exposed in diagnostics. Logging therefore
never blocks the thread that produced the event and never grows without bound.

`Logging:MinimumLevel` sets the lowest level; `Logging:Overrides` raises or lowers it for a source prefix, and the
longest matching prefix wins. Both are read on every event, so a change to the file applies at once.

## Logging a business event

Log once, where the state changes, and let the event handlers do it. `LifecycleLog.cs` contains the handlers for user
connections, rooms, rounds and restrictions; a new lifecycle event gets a handler there, not a copy in each transport.
A transport may log a transport-specific failure, but not the business event again.

Use the level that matches the meaning: `Information` for lifecycle, `Warning` for an abnormal condition that continues,
`Error` for a failed operation, `Fatal` when the server cannot continue. A message that fires on every request or packet
belongs at `Debug` (or is a metric) even when it helps during an investigation.

## Exceptions

An exception in a connection is isolated: it is logged, counted in diagnostics, and the connection is closed if its
state is unknown; the server keeps running. Thrown from a REST route, it becomes a `500` envelope without a stack.
A background service logs the failure and continues its loop.

## Invariants

* The log carries lifecycle events, not routine traffic.
* Every sink is behind the bounded async queue; a full queue drops and counts, never blocks or grows.
* No log message contains a password, a token, an IRC key or a certificate password.
* File retention follows `Logging:RetentionPeriod`; the default is 14 days.
* A lifecycle event is logged once, by its handler, not by every entry point.

## Related code

* `src/Hosts/Host/Logging/ServerLogging.cs` — sinks, queue, level policy.
* `src/Hosts/Host/Logging/LifecycleLog.cs` — the lifecycle event handlers.
* `src/Hosts/Host/Logging/LogRetention.cs` — retention sweep.
* `src/Hosts/Host/Logging/TelemetrySetup.cs` — optional OpenTelemetry.
* `src/Hosts/Cli/Dashboard/` — the log panel buffer.

## See also

* [`../for-technicians/logging.md`](../for-technicians/logging.md) — log files, levels, retention and troubleshooting
* [`configuration.md`](../for-technicians/configuration.md) — `Logging` settings
* [`diagnostics.md`](diagnostics.md) — metrics and the dropped-event count
