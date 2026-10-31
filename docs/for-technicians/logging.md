# Logging for operators

## Overview

Basil writes operational log files to `Logs/` next to the executable and shows the same events in the dashboard (or on
stdout in `--nogui` mode). In Docker, stdout is also available through `docker compose logs`.

The folder is created when the server starts.

## Log files

| Path | Contents |
|---|---|
| `Logs/basil-YYYYMMDD.log` | the log written on that day, or the next file of that day after a size roll |
| `Logs/basil-*.log` | older files kept for the retention period |

A file rolls at the next day or at **256 MB**, whichever comes first, so a busy server may produce several files in one
day. There is no separate error file and no `latest.log` link: search the files of the day.

Files are deleted after `Logging:RetentionPeriod` (14 days by default) with no write, checked at startup and every six
hours.

## Log levels

The minimum level is `Basil:Logging:MinimumLevel` in `Configurations/appsettings.json`:

```json
{
	"Basil": {
		"Logging": {
			"MinimumLevel": "Information",
			"Overrides": { "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Information" }
		}
	}
}
```

`Overrides` keys are source prefixes; the longest matching prefix wins. Supported levels are `Verbose`, `Debug`,
`Information`, `Warning`, `Error` and `Fatal`.

`Logging` applies **while the server runs**: edit the file, and the new level is used from the next event, on all sinks.
No restart or rebuild is required.

`Debug` produces significantly more output and is the level to use while investigating packet handling, room changes or
cache behaviour. Return it to `Information` afterwards.

## Reading the log

The log is for lifecycle events, not traffic:

* start, stop and shutdown phases, with timings and failures;
* login and logout of each connection type;
* rooms opened and closed, rounds started and ended;
* moderation and restrictions, console commands;
* errors and warnings.

Individual packets, IRC lines and API requests are metrics, visible in the dashboard and `/v1/diagnostics`, not in the
log. An endpoint that answers `500` also appears in the log with its exception; the response itself carries no stack.

Each message starts with a UTC offset timestamp, a level and the source class, followed by structured values such as the
user id, room id or connection type. Search for the user id, room id or match id to follow one incident.

## Dropped events

The file writer runs behind a bounded queue (`Logging:QueueCapacity`, 10 000 by default). If the disk cannot keep up,
the newest events are dropped rather than blocking the server; the dashboard and diagnostics show how many were lost.
A non-zero count means the log is incomplete for that period.

## Troubleshooting

### Logs are missing

1. The server can create and write `Logs/` next to the executable.
2. You are looking beside the executable, not in the source tree.
3. In Docker, `Logs/` is bind-mounted if you need the files after the container is removed.
4. `docker compose logs -f` shows stdout.

### There is not enough detail

Set `Logging:MinimumLevel` to `Debug` (and, if wanted, an override for one source). The change applies at once.
Reproduce the problem, collect the files, then restore `Information`.

### The log is taking too much disk space

Retention is 14 days, rolled daily or at 256 MB. Lower `Logging:RetentionPeriod` to keep fewer days, or send stdout to
an external log collector.

## See also

* [`logging.md`](../for-developers/logging.md) — logging internals and lifecycle events
* [`configuration.md`](configuration.md) — all deployment settings
* [`troubleshooting.md`](troubleshooting.md) — operational troubleshooting
