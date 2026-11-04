# Configuration

This page is the authoritative reference for configuring and maintaining a Basil server. It covers the two
configuration planes, the files and directories beside the executable, and how to move an installation.

If this document conflicts with another document, **this document takes precedence**.

## Two configuration planes

Basil separates settings by who owns them and when they may change:

| Plane | Lives in | Changed by | Applies |
|---|---|---|---|
| Deployment | `Configurations/appsettings*.json` | the operator, on the machine | at start, or at once for `Logging:LogLevel` and `Basil:Updates` |
| Policy | `ServerSettings` in the database | the API, the console | at once |

Deployment settings describe this machine: the domain, ports, certificate, database and log files. Policy describes
how the running server behaves for users: locks, rate limits, upload limits, session lifetimes, the bot prefix, CORS
and the beatmap mirror. Policy is edited through the generated API (`GET /v1/settings`, `GET`/`PATCH
/v1/settings/{category}`) or the console (`settings <category> show`, `settings <category> set --field value`) and needs
no restart.

## Deployment configuration

### Files and precedence

The files sit next to the executable, not in `Data/`, so they travel with the binary:

```text
Configurations/
├── appsettings.json
├── appsettings.{Environment}.json   optional overlay
├── appsettings.schema.json
├── Templates/replies.json           BasilBot's wording; written with defaults when absent
└── basil.local.pfx                  the bundled development certificate
```

Sources, later winning over earlier: `appsettings.json`, `appsettings.{Environment}.json` (selected by
`ASPNETCORE_ENVIRONMENT`), environment variables (`ConnectionStrings__Write`, `Basil__Server__Domain`: a double
underscore stands for `:`), command-line arguments (`--Basil:Server:Domain=...`). The file is strict JSON: no comments and
no trailing commas. `appsettings.schema.json` describes every key.

Relative paths (the certificate, `Storage:Location`, `Logging:File:Location`) are relative to the executable.

### Available settings

Kestrel, logging, connection strings and OpenTelemetry use the standard .NET sections; everything Basil-specific is
under `Basil`.

| Setting | Meaning |
|---|---|
| `Kestrel:Endpoints:<name>:Url` | where the web server listens, default `https://0.0.0.0:443` (osu! connects to 443); an `http://` URL serves plain HTTP behind a proxy; port `0` picks a free port |
| `Kestrel:Certificates:Default:Path`, `Password` | PFX of the HTTPS endpoints |
| `Logging:LogLevel` | lowest level written: `Default`, and one entry per source prefix (the longest prefix wins) |
| `Logging:File:Enabled`, `Location` | write log files, and the folder (default `Logs`) |
| `Logging:File:Rolling:Interval`, `MaxSize` | a new file starts after this interval (default `Day`) or at this size (default `100MB`) |
| `Logging:File:Retention:MaxAge`, `MaxFiles`, `MaxSize` | delete log files older than 14 days, beyond the newest 30, or beyond 1 GB in total; `0` or empty switches a limit off |
| `Logging:QueueCapacity` | events waiting for the writer before the newest are dropped, default 10000 |
| `ConnectionStrings:Write` | Npgsql connection string for storing changes and migrations |
| `ConnectionStrings:Read` | Npgsql connection string for reads (it may point at a replica); empty uses `Write` |
| `OpenTelemetry:OtlpEndpoint`, `Metrics`, `Traces`, `Logs` | OpenTelemetry export; off by default |
| `Basil:Server:Name` | the name the server calls itself (the OpenTelemetry service name), default `Basil` |
| `Basil:Server:Domain` | the public domain; every subdomain is derived from it |
| `Basil:Server:TrustedProxies` | IPs and CIDR ranges whose forwarding headers are trusted; defaults to loopback |
| `Basil:Server:Irc:Port` | plain TCP port of IRC, default `6667`; `0` picks a free port |
| `Basil:Server:Multicast:Enabled`, `UseIPv4`, `UseIPv6`, `DnsTtl` | answer multicast DNS for the domain and its subdomains (off unless enabled), on the standard mDNS groups and port 5353; `UseIPv4` and `UseIPv6` (default `true`) switch one address family off when `false`; `DnsTtl` is how many seconds a client may keep an answer |
| `Basil:Storage:Location` | directory that holds stored files, default `Data` |
| `Basil:Assets:CacheDirectory`, `FfmpegFolder` | the directory for the temporary files made while audio previews are built (default `Cache/Previews` under the storage location), and the folder of the ffmpeg executable (default: found on the PATH) |
| `Basil:Database:Read:PoolSize`, `Write:PoolSize` | the database is always PostgreSQL; `Read:PoolSize` caps the reading pool (`0` keeps the driver's default), `Write:PoolSize` is the number of simultaneous writing connections, default `8` |
| `Basil:Updates:CheckOnStartup`, `Source`, `Channel` | startup update check, its feed, and the release channel (`stable`, or the name of another published channel) |
| `Basil:Bot:Replies:Path` | path of the replies file, default `Configurations/Templates/replies.json` |

Because the shipped file lists both connection strings, override both (`ConnectionStrings__Write` and
`ConnectionStrings__Read`) or edit the file; a variable for `Write` alone leaves `Read` pointing at the file's value.

### Applying changes

The server watches the configuration files.

* `Logging:LogLevel` and `Basil:Updates` apply while the server runs.
* `Kestrel`, `ConnectionStrings`, `OpenTelemetry`, `Logging:File`, `Basil:Server`, `Basil:Database`, `Basil:Storage` and `Basil:Assets`
  are read once at start. A change is logged as "applied after the server restarts" and takes effect at the next start.

No rebuild is required.

### Command line

```text
Basil                 start the server
Basil --noconsole     dashboard without the command box
Basil --nogui         plain log lines, no dashboard
Basil <command>       run one console command (in the running server, or offline)
```

When stdin or stdout is redirected, the server falls back to `--nogui`. A setting can be overridden per run with
`--Kestrel:Endpoints:Https:Url=https://0.0.0.0:8443`.

## Upgrading from v1.0.0-alpha.1

The settings moved to the standard .NET sections. Rename these keys in `Configurations/appsettings.json` (and in any
environment variable or command-line argument, where `:` is `__` in variables); the old keys are no longer read.

| Old | New |
|---|---|
| `Basil:Server:Bind`, `Basil:Server:Ports:Https` | the URL of `Kestrel:Endpoints:Https:Url`, for example `https://0.0.0.0:443` |
| `Basil:Server:Certificate:Path` | `Kestrel:Certificates:Default:Path` |
| `Basil:Server:Certificate:Password` | `Kestrel:Certificates:Default:Password` |
| an empty certificate path (plain HTTP behind a proxy) | an `http://` URL in `Kestrel:Endpoints:Https:Url` and no certificate |
| `--urls`, `ASPNETCORE_URLS` | `Kestrel:Endpoints:<name>:Url` (it now wins over `--urls` when both are set) |
| `Basil:Server:Ports:Irc` | `Basil:Server:Irc:Port` |
| `Basil:Server:LanAdvertise` (default `true`) | `Basil:Server:Multicast:Enabled` (default `false`) |
| (new) | `Basil:Server:Name`, `Basil:Server:Multicast:UseIPv4`, `UseIPv6`, `DnsTtl` |
| `Basil:Storage:DataDirectory` | `Basil:Storage:Location` |
| `Basil:Database:ConnectionString` | `ConnectionStrings:Write` (and `ConnectionStrings:Read`, which falls back to `Write`) |
| `Basil:Database:WriteConnections` | `Basil:Database:Write:PoolSize` |
| (new) | `Basil:Database:Read:PoolSize` |
| `Basil:Logging:MinimumLevel` | `Logging:LogLevel:Default` |
| `Basil:Logging:Overrides:<source>` | `Logging:LogLevel:<source>` |
| `Basil:Logging:Directory` | `Logging:File:Location` |
| `Basil:Logging:RetentionPeriod` | `Logging:File:Retention:MaxAge` (new: `MaxFiles`, `MaxSize`) |
| a 256 MB size roll | `Logging:File:Rolling:MaxSize` (default `100MB`; new: `Interval`) |
| `Basil:Logging:QueueCapacity` | `Logging:QueueCapacity` |
| `Basil:Telemetry:*` | `OpenTelemetry:*` |
| (new) | `Basil:Updates:Channel` |

Levels are now the .NET names (`Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`).

## Name resolution on a LAN

Every client must resolve the domain and each served subdomain to the server:

```text
<domain>   c.<domain>   ce.<domain>  c4.<domain>  c5.<domain>  c6.<domain>
osu.<domain>   b.<domain>   a.<domain>   api.<domain>   assets.<domain>
```

With `Basil:Server:Multicast:Enabled`, the server answers multicast DNS for exactly these names. Most operating systems only
consult multicast DNS for `.local` names; other domains need a hosts-file entry or real DNS. The `ppy.sh` hosts are
served for redirected clients but never advertised. Leave `Multicast:Enabled` at `false` to keep the advertisement off.

For the hosts file, add one line per name on each client and on the server, for example:

```text
192.168.1.10  basil.local
192.168.1.10  c.basil.local
192.168.1.10  api.basil.local
```

See [`https.md`](https.md) for the certificate those names require.

## Updates

The startup check reports whether a newer release exists. It never downloads or installs anything, gives up after 15
seconds, and does not delay the server. A feed that cannot be reached is a warning, not a failure. Basil does not
restart itself: install a new release the same way the current one was installed.

## Templates and reply text

BasilBot's wording lives in `Configurations/Templates/replies.json`, a JSON object that maps a reply key to an ICU
MessageFormat string with named placeholders such as `{room}`. The server writes the file with the default wording when
it is missing and reloads it while running. A missing key keeps its default; an unreadable file leaves the last good
wording in use; one invalid text falls back to its default. See [`basil-bot.md`](../for-client/basil-bot.md).

## Persistent data

Basil creates these beside the executable:

| Path | Contents | Safe to delete? |
|---|---|---|
| `Configurations/` | deployment configuration, certificate, templates | **No** |
| `Data/Replays/` | submitted replays | **No**, unless you mean to lose them |
| `Data/Avatars/` | user avatars | **No**, unless you mean to lose them |
| `Data/Beatmapsets/` | one folder per set: archive, difficulties, derived files | **No**, unless you mean to remove beatmaps |
| `Data/Menu/` | banners, seasonals and the main-menu icon | Optional |
| `Data/Faqs/` | text used by `!faq <entry>` | Optional |
| `Data/Screenshots/` | uploaded screenshots | Optional |
| `Data/Cache/` | image crops and other rebuildable files | **Yes**, while the server is stopped |
| `Logs/` | operational log files | Optional |

### Database

Basil stores its records in PostgreSQL, configured by `ConnectionStrings:Write` and `Read` and by `Basil:Database`. One
database belongs to one Basil process; nothing else should write to it. Migrations run when the server starts. Back up
the database with `pg_dump` and `Data/` together, because records and files refer to each other.

### Beatmapsets

Drop a `.osz`, or a folder of loose set files, into `Data/Beatmapsets/` while the server runs; the watcher imports it
within about two seconds, and a full reconciliation runs at startup. Deleting a set's `archive.osz` removes the set.
A standalone `.osu` file is not imported. Uploads go through `POST /v1/beatmapsets`. See
[`beatmap-ingestion.md`](../for-developers/beatmap-ingestion.md).

## Moving or upgrading a server

Treat the executable, `Configurations/`, `Data/` and `Logs/` as separate:

1. Stop Basil.
2. Copy the executable directory, including `Configurations/` and `Data/`.
3. Do not copy `Logs/` unless you need the history.
4. Verify the domain, certificate and ports on the target.
5. Start Basil and check the startup log.

**Never replace `Data/` with an empty directory during an upgrade.** It holds the stored files, and `Configurations/`
holds the configuration. A development database that already applied an older `InitialCreate` must be dropped: the
project edits migrations in place while it is not deployed.

## See also

* [`https.md`](https.md) — certificates and TLS names
* [`logging.md`](logging.md) — log files, levels and retention
* [`console.md`](console.md) — console commands
* [`docker.md`](docker.md) — container deployment
* [`../for-developers/architecture.md`](../for-developers/architecture.md) — where configuration is bound
