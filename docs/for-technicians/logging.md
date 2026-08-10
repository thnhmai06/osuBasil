# Logging for operators

## Overview

Basil writes operational logs to the `Logs/` directory next to the executable.

Logs are also written to standard output (`stdout`). This is especially useful for Docker deployments, where the same
logs can be viewed with `docker compose logs`.

The log directory is created automatically when Basil starts.

## Log files

The default layout is:

| Path                      | Contents                               |
|---------------------------|----------------------------------------|
| `Logs/full/basil-*.log`   | All logs from `Information` and above. |
| `Logs/latest.log`         | Current day's full log.                |
| `Logs/errors/basil-*.log` | `Error` and `Fatal` logs only.         |
| `Logs/errors_latest.log`  | Current day's error log.               |

Logs roll daily and are retained for **30 days**.

`latest.log` and `errors_latest.log` provide stable filenames for monitoring tools and manual troubleshooting without
needing to know the current date. They are hard-linked to the latest log file.

### Docker

In Docker deployments, logs are also available through the container runtime:

```bash
docker compose logs -f
```

The default Compose configuration persists `Logs/` under:

```text
docker-data/Logs/
```

If using Docker without a bind mount, logs remain inside the container and will be lost when the container is removed.

## Log levels

The minimum log level is controlled by:

```text
Basil:Logging:MinimumLevel
```

in [`appsettings.json`](../../src/Basil.Web/appsettings.json).

Example:

```json
{
	"Basil": {
		"Logging": {
			"MinimumLevel": "Information"
		}
	}
}
```

Supported levels are:

```text
Verbose
Debug
Information
Warning
Error
Fatal
```

The default is:

```text
Information
```

### Increase logging for troubleshooting

When investigating a problem, temporarily change the level to:

```json
{
  "Basil": {
    "Logging": {
      "MinimumLevel": "Debug"
    }
  }
}
```

Then restart Basil.

`Debug` produces significantly more output and is useful when investigating packet handling, cache behaviour, room
changes, and other operational problems.

After troubleshooting, change the level back to `Information` and restart Basil.

The minimum level affects the normal log output and `Logs/full/`.

`Logs/errors/` always contains `Error` and `Fatal` events regardless of the configured minimum level.

## Collecting logs

When reporting a problem, collect the relevant logs from:

```text
Logs/full/
Logs/errors/
```

For a recent problem, start with:

```text
Logs/latest.log
Logs/errors_latest.log
```

Also collect the corresponding Docker output when Basil is running in Docker:

```bash
docker compose logs --no-color > basil-docker.log
```

Avoid sending only the error log. Many problems are explained by earlier `Information` or `Warning` messages in the full
log.

### What to include in a report

When reporting an operational issue, provide:

* the approximate time the problem occurred;
* the affected hostname or service;
* the relevant section of `Logs/latest.log`;
* `Logs/errors_latest.log`;
* Docker output, if applicable;
* the Basil version;
* any configuration change made immediately before the problem.

Do not include secrets such as the admin key, certificate password, or user passwords.

## Request and connection identifiers

Basil includes identifiers in log messages to help correlate related events.

Depending on the service involved, logs may include:

* `RequestId` identifies an HTTP request;
* `UserId` identifies the affected user;
* `PacketType` identifies an osu! protocol packet;
* `MatchId` identifies a multiplayer match;
* `ConnectionId` identifies an IRC TCP connection.

When troubleshooting a specific event, searching the log for one of these identifiers can reveal the related operations.

## Troubleshooting

### Basil is running but logs are missing

Check:

1. Basil has permission to create and write to the `Logs/` directory.
2. You are checking the directory next to the executable.
3. For Docker, `Logs/` is correctly bind-mounted if persistent logs are expected.
4. The container logs with:

```bash
docker compose logs -f
```

### There are not enough details in the logs

Temporarily set:

```text
Basil:Logging:MinimumLevel=Debug
```

in `appsettings.json` and restart Basil.

Reproduce the problem, collect the logs, then return the setting to `Information`.

### The error log is empty

`Logs/errors/` only contains `Error` and `Fatal` events.

Check `Logs/latest.log` as well. Problems that do not produce an `Error` or `Fatal` event may still be visible there as
`Warning` or `Information` messages.

### Logs are taking too much disk space

Basil automatically retains 30 days of daily log files.

If longer-term retention is required, send stdout or the log files to an external log collection system rather than
relying on Basil's local retention.

## See also

* [`logging.md`](../for-developers/logging.md): logging internals, categories, and scopes
* [`configuration.md`](configuration.md): `Basil:Logging:MinimumLevel`
* [`troubleshooting.md`](troubleshooting.md): operational troubleshooting
