# Logging

## Overview

Every log line Basil writes goes through [Serilog](https://serilog.net/), configured entirely in code rather than through configuration files, and tagged with a category so a reader can filter "just multiplayer" or "just the database" out of a busy console.

## Why configure logging in code?

Logging setup rarely changes at runtime: the sinks, the file layout, and the category rules are fixed decisions, not something an operator needs to tweak per deployment. The one thing that *is* worth exposing is the minimum level, so that's the only setting that lives in `appsettings.json`; everything else stays in `Program.cs` where it's reviewed like any other code change instead of drifting silently in a config file nobody reads.

## Contract

- **Two file sinks besides the console**, both daily-rolling with 30 days of retention: `Logs/full/` (Information and above) and `Logs/errors/` (Error and Fatal only, regardless of the configured minimum level).
- **`Logs/latest.log` and `Logs/errors_latest.log` are hardlinks**, not copies, always pointing at whichever dated file is currently open. A reader can tail one fixed filename without knowing today's date.
- **The minimum level for stdout and the full file is configurable** via `Basil:Logging:MinimumLevel` in `appsettings.json` (see [`run-deployment.md`](run-deployment.md)); the errors file is never affected by that setting.
- **Domain-event lines are prefixed `+`/`-`/`~`** (created/removed/modified) for anything tracking a resource's lifecycle: a mapset ingested, a match closed, a user logging in. Grepping for these markers finds state changes without depending on log level alone.

## Concepts

- **Categories** are derived automatically from the logging class's name, not pushed manually per call site. `Mapsets`, `Matches`, `Scores`, `Online`, `IRC`, `Database`, `Cache`, `Host`, and `Api` each cover a fixed set of namespaces; anything else falls back to `App` and is demoted to Warning-and-above by default, since unclassified framework chatter isn't worth Information-level noise.
- **Scopes** are correlation ids attached to a request or packet as it flows through the system: `RequestId` for every HTTP request, `UserId`/`PacketType`/`MatchId` for a dispatched bancho packet, `ConnectionId` for a real IRC TCP connection. They render as extra structured properties on any line logged while that scope is active, so a support request ("what happened around request X") can be answered by grepping one id.
- **One log line per business event**, not one per layer that touches it. A multiplayer state change is logged once, inside the shared service method every caller (a packet handler, a `!mp` command, and an HTTP route) converges on, rather than being logged again at each entry point.

## Design

**Why hardlink the "latest" pointer instead of copying it?** A copy would double the disk writes for every log line: write to the dated file, then write the same bytes again to the pointer. A hardlink is the same file under two names, so there's nothing to keep in sync and no extra write cost.

**Why keep the errors file always at Error-level regardless of the configured minimum?** Lowering the minimum level is meant for surfacing extra detail during troubleshooting (`Debug`-level cache hits, per-packet dispatch, and so on). That's exactly the kind of noise the errors file exists to stay free of, so it doesn't inherit the configured level at all.

## Related code

- `Basil.Web/Program.cs` (`ConfigureSerilog`)
- `Basil.Web/Logging/CategoryEnricher.cs`
- `Basil.Web/Logging/HardLinkFileLifecycleHooks.cs`
- `Basil.Infrastructure/Logging/HardLink.cs`

## See also

- [`run-deployment.md`](run-deployment.md): where the log files land and how to change the minimum level
