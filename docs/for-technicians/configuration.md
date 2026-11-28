# Configuration

This page is the authoritative reference for configuring and maintaining a Basil server. It covers the two
configuration planes, the folders beside the executable, and how to move an installation.

If this document conflicts with another document, **this document takes precedence**.

## Two configuration planes

Basil separates settings by who owns them and when they may change:

| Plane | Lives in | Changed by | Applies |
|---|---|---|---|
| Deployment | `Data/appsettings*.json` | the operator, on the machine | at start, or at once for `Logging:LogLevel` and `Basil:Updates` |
| Policy | `ServerSettings` in the database | the API, the console | at once |

Deployment settings describe this machine: the domain, ports, trusted proxies, database and log files. Policy describes
how the running server behaves for users: locks, rate limits, upload limits, session lifetimes, the bot prefix, CORS
and the time allowed to calculate a score's pp. Policy is edited through the generated API (`GET /v1/settings`, `GET`/`PATCH
/v1/settings/{category}`) or the console (`settings <category> show`, `settings <category> set --field value`) and needs
no restart.

## Deployment configuration

### Files and precedence

The files sit in `Data/`, the one folder a backup needs besides the database:

```text
Data/
├── appsettings.json
├── appsettings.{Environment}.json   optional overlay
├── appsettings.schema.json
├── tuisettings.json                 the dashboard's preferences (its colour theme)
├── metadata.json                    the layout version of Data/
└── Localizations/                   bot.json (BasilBot's wording), irc.json (the IRC server's wording)
```

Basil ships a read-only copy of its own files in `Defaults/Data/`. At every start it copies each file that is missing
from `Data/` and never overwrites one, so an operator's edit survives an upgrade and a deleted file comes back with its
default content.

Sources, later winning over earlier: `appsettings.json`, `appsettings.{Environment}.json` (selected by
`ASPNETCORE_ENVIRONMENT`), environment variables (`ConnectionStrings__Write`, `Basil__Server__Domain`: a double
underscore stands for `:`), command-line arguments (`--Basil:Server:Domain=...`). The file is strict JSON: no comments and
no trailing commas. `appsettings.schema.json` describes every key.

Relative paths (`Logging:File:Location`) are relative to the executable. The folders below always sit beside the executable and are not configurable.

### Available settings

Kestrel, logging, connection strings and OpenTelemetry use the standard .NET sections; everything Basil-specific is
under `Basil`.

| Setting | Meaning |
|---|---|
| `Kestrel:Endpoints:<name>:Url` | where the web server listens, default `http://0.0.0.0:8080`: plain HTTP behind nginx, which ends TLS (Basil holds no certificate); port `0` picks a free port |
| `Logging:LogLevel` | lowest level written: `Default`, and one entry per source prefix (the longest prefix wins) |
| `Logging:File:Enabled`, `Location` | write log files, and the folder (default `Logs`); the system log also goes to standard output when the dashboard is off |
| `Logging:File:Rolling:Interval`, `MaxSize` | a new file starts after this interval (default `Day`) or at this size (default `100MB`) |
| `Logging:File:Retention:MaxAge`, `MaxFiles`, `MaxSize` | delete log files older than 14 days, beyond the newest 30, or beyond 1 GB in total; `0` or empty switches a limit off |
| `Logging:QueueCapacity` | events waiting for the writer before the newest are dropped, default 10000 |
| `ConnectionStrings:Write` | Npgsql connection string for storing changes and migrations |
| `ConnectionStrings:Read` | Npgsql connection string for reads (it may point at a replica); empty uses `Write` |
| `OpenTelemetry:OtlpEndpoint`, `Metrics`, `Traces`, `Logs` | OpenTelemetry export; off by default |
| `Basil:Server:Name` | the name the server calls itself (the OpenTelemetry service name), default `Basil` |
| `Basil:Server:Domain` | the public domain; every subdomain is derived from it |
| `Basil:Server:TrustedProxies` | IPs and CIDR ranges of the reverse proxy: only these may tell Basil the client address (`X-Real-IP`), country and scheme, and open IRC connections (with a PROXY protocol header); defaults to loopback, the Compose file sets the compose network |
| `Basil:Server:Irc:Port` | internal plain TCP port of IRC, default `6667`, reached only through nginx on `6697`; `0` picks a free port |
| `Basil:Server:Files:AccelPrefix` | the nginx location that serves the `Data` and `Cache` folders, each below it under its own name (the Compose file sets `/_files/`, so `/_files/Data/` and `/_files/Cache/`); when set, stored files are sent by nginx through `X-Accel-Redirect`, when empty Basil sends them itself (for `dotnet run` without nginx) |
| `Basil:Assets:CacheDirectory` | the directory for the temporary files made while audio previews are built (default `Cache/Previews` beside the executable) |
| `Basil:Database:Read:PoolSize`, `Write:PoolSize` | PostgreSQL is the only database; the connection strings are in `ConnectionStrings:Read` and `Write`; migrations run against the write endpoint; `Read:PoolSize` caps the reading pool (`0` keeps the driver's default), `Write:PoolSize` is the number of simultaneous writing connections, default `8` |
| `Basil:Updates:CheckOnStartup`, `Source` | startup check of the latest release of the GitHub repository in `Source`; it only reports a newer version |

The connection strings are deployment values and secrets, so the shipped file has none: set the environment variables
`ConnectionStrings__Write` and, to read from a replica, `ConnectionStrings__Read` (the Compose file builds both from
`POSTGRES_PASSWORD` in `.env`). Without `Write`, Basil refuses to start and names the variable. `Basil:Server:Domain`,
`Basil:Server:TrustedProxies` and `Basil:Server:Files:AccelPrefix` are deployment values too (the Compose file sets the
last two from the environment). The shipped `appsettings.json` holds only how the application behaves and is safe to
publish: it never holds a secret, and a deployment value in it is a default that an environment variable overrides. The
environment variable names (`ConnectionStrings__Write`, `Basil__Server__Domain` and the rest) are as frozen at 1.0 as
the keys they spell.

Pool size and timeouts are Npgsql settings and belong in the connection string, from `.env`: `Maximum Pool Size`,
`Timeout` (seconds to open a connection) and `Command Timeout` (seconds per command), for example
`Host=postgres;Database=basil;Username=basil;Password=...;Maximum Pool Size=40;Timeout=15;Command Timeout=30`. There is no
Basil setting for them beyond `Basil:Database:Read:PoolSize` and `Write:PoolSize` above.

### Applying changes

The server watches the configuration files.

* `Logging:LogLevel` and `Basil:Updates` apply while the server runs.
* `Kestrel`, `ConnectionStrings`, `OpenTelemetry`, `Logging:File`, `Basil:Server`, `Basil:Database` and `Basil:Assets`
  are read once at start. A change is logged as "applied after the server restarts" and takes effect at the next start.

No rebuild is required.

### Command line

```text
Basil                 start the server
Basil --console 1     dashboard and command box (the default)
Basil --console 0     plain log lines, no dashboard
Basil --nocolor       monochrome dashboard (also when NO_COLOR is set)
Basil --ascii         dashboard in ASCII characters only
Basil <command>       run one console command (in the running server, or offline)
```

When stdin or stdout is redirected, the server falls back to `--console 0`. A setting can be overridden per run with
`--Kestrel:Endpoints:Http:Url=http://0.0.0.0:8081`.

## Name resolution on a LAN

Every client must resolve the domain and each served subdomain to the server:

```text
<domain>   c.<domain>   ce.<domain>  c4.<domain>  c5.<domain>  c6.<domain>
osu.<domain>   b.<domain>   a.<domain>   api.<domain>   assets.<domain>   irc.<domain>
```

`irc.<domain>` is needed only by IRC clients. Basil does not advertise these names: add them to the hosts file of each
client (below) or to your DNS.

For the hosts file, add one line per name on each client and on the server, for example:

```text
192.168.1.10  basil.local
192.168.1.10  c.basil.local
192.168.1.10  api.basil.local
192.168.1.10  irc.basil.local
```

See [`https.md`](https.md) for the certificate those names require.

## Updates

The startup check reports whether a newer release exists. It never downloads or installs anything, gives up after 15
seconds, and does not delay the server. A feed that cannot be reached is a warning, not a failure. Basil does not
restart itself: update the image as [`docker.md`](docker.md#updating-a-docker-deployment) describes.

## Reply text

The wording of BasilBot and of the IRC server lives in `Data/Localizations/bot.json` and `Data/Localizations/irc.json`, JSON objects that map a reply name to an ICU MessageFormat
string with named placeholders such as `{room}`. The server writes the file with the default wording when it is
missing and reloads it while running. A missing key keeps its default; an unreadable file leaves the last good wording
in use; one invalid text falls back to its default. See [`basil-bot.md`](../for-client/basil-bot.md).

## Folders

Basil creates these beside the executable (in the container, under `/app`). Only `Data/` needs a backup.

| Path | Contents | Safe to delete? |
|---|---|---|
| `Data/appsettings.json`, `appsettings.schema.json`, `tuisettings.json` | deployment configuration and the dashboard's preferences | **No**: a deleted shipped file is written again with its default content |
| `Data/metadata.json` | the layout version of `Data/` | **No** |
| `Data/Localizations/` | the wording of BasilBot and the IRC server | Optional: the default wording returns |
| `Data/Rulesets/<version>/` | the osu! calculation stack (installed with `ruleset update`, checked at every start) | Optional: install it again, or copy it from another install |
| `Data/Replays/` | submitted replays | **No**, unless you mean to lose them |
| `Data/Avatars/` | user avatars | **No**, unless you mean to lose them |
| `Data/Beatmapsets/` | one folder per set: archive, difficulties, derived files | **No**, unless you mean to remove beatmaps |
| `Data/Menu/` | banners, seasonals and the main-menu icon | Optional |
| `Data/Faqs/` | text used by `!faq <entry>` | Optional |
| `Data/Screenshots/` | uploaded screenshots | Optional |
| `Cache/` | covers, difficulty attributes, audio previews: everything rebuilt on demand | **Yes**, while the server is stopped |
| `Logs/` | the system log and `Sessions/`, one file per connection session | Optional |
| `Temp/` | the control socket | Not by hand |

### Layout version and refusing to start

`Data/metadata.json` holds `{ "layout": 1 }`. At start, before the settings are read, Basil reads it. A `Data/` without
the file is taken as layout 0 and brought forward by the steps in order; each step can run again without harm, and the
version is written only after the steps succeed. A failed step stops the start and names the step; the file keeps the old
version. Layout 0 to 1 deletes what moved to `Cache/` (the covers in the set folders, `Data/Rulesets/Attributes`,
`Data/Cache`), because it is rebuilt.

Basil refuses to start, with one sentence saying why, when `Data/` has a layout newer than the program knows, or when the
database has applied a migration the program does not know (the sentence names the module and the migration). Both mean
an older Basil was started on the data of a newer one; start the newer image again, or restore the backup.

### Database

Basil stores its records in PostgreSQL, configured by `ConnectionStrings:Write` and `Read` and by `Basil:Database`. One
database belongs to one Basil process; nothing else should write to it. Migrations run when the server starts. Back up
the database with `pg_dump` and `Data/` together, because records and files refer to each other.

### Beatmapsets

Drop a `.osz`, or a folder of loose set files, into `Data/Beatmapsets/` while the server runs; the watcher imports it
within about two seconds, and a full reconciliation runs at startup. Deleting a set's `archive.osz` removes the set.
A standalone `.osu` file is not imported. Uploads go through `POST /v1/beatmapsets`. See
[`beatmap-ingestion.md`](../for-developers/beatmap-ingestion.md).

## Upgrading a server

Basil migrates the database and `Data/` forward at start and does not back them up first. Before each
update:

1. Dump the database: `docker compose exec postgres pg_dump -U basil basil > basil.sql`.
2. Copy the `Data/` volume (`docker-data/Data`) while Basil is stopped.
3. Update the image as [`docker.md`](docker.md#updating-a-docker-deployment) describes and start it, then check the
   startup log.

To go back, stop Basil, restore both, and start the previous image. `Cache/` and `Logs/` need no backup.

**Never replace `Data/` with an empty folder during an upgrade.** It holds the stored files and the configuration.

## See also

* [`https.md`](https.md) — certificates and TLS names
* [`logging.md`](logging.md) — log files, levels and retention
* [`console.md`](console.md) — console commands
* [`docker.md`](docker.md) — container deployment
* [`../for-developers/architecture.md`](../for-developers/architecture.md) — where configuration is bound
