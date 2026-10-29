# Configuration

This page is the authoritative reference for configuring and maintaining a Basil server. It covers the two
configuration planes, the files and directories beside the executable, and how to move an installation.

If this document conflicts with another document, **this document takes precedence**.

## Two configuration planes

Basil separates settings by who owns them and when they may change:

| Plane | Lives in | Changed by | Applies |
|---|---|---|---|
| Deployment | `Configurations/appsettings*.json` | the operator, on the machine | at start, or at once for `Logging` and `Updates` |
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
`ASPNETCORE_ENVIRONMENT`), command-line arguments. Environment variables do not override an individual setting.

### Available settings

All settings are under `Basil`.

| Setting | Meaning |
|---|---|
| `Server:Domain` | the public domain; every subdomain is derived from it |
| `Server:Bind` | address Kestrel binds, default `0.0.0.0` |
| `Server:Ports:Https` | HTTPS port for osu! and the HTTP API, default `443`; `0` picks a free port |
| `Server:Ports:Irc` | plain TCP port of IRC, default `6667`; `0` picks a free port |
| `Server:Certificate:Path` | PFX path relative to the executable; empty serves plain HTTP behind a proxy |
| `Server:Certificate:Password` | PFX password |
| `Server:LanAdvertise` | answer multicast DNS for the domain and its subdomains, default `true` |
| `Server:TrustedProxies` | IPs and CIDR ranges whose forwarding headers are trusted; defaults to loopback |
| `Storage:DataDirectory` | directory that holds stored files, default `Data` |
| `Database:ConnectionString` | Npgsql connection string |
| `Database:WriteConnections` | simultaneous writing connections, default `8` |
| `Logging:MinimumLevel` | lowest level written; default `Information` |
| `Logging:Overrides` | per-source minimum levels, matched by prefix |
| `Logging:RetentionPeriod` | how long log files are kept, default 14 days |
| `Logging:Directory` | log folder, default `Logs` |
| `Logging:QueueCapacity` | events waiting for the writer before the newest are dropped, default 10000 |
| `Telemetry:OtlpEndpoint`, `Metrics`, `Traces`, `Logs` | OpenTelemetry export; off by default |
| `Updates:CheckOnStartup`, `Source` | startup update check and its feed |
| `Bot:Replies:Path` | path of the replies file, default `Configurations/Templates/replies.json` |

### Applying changes

The server watches the configuration files.

* `Logging` and `Updates` apply while the server runs.
* `Server`, `Database`, `Storage` and `Telemetry` are read once at start. A change is logged as
  "applied after the server restarts" and takes effect at the next start.

No rebuild is required.

### Command line

```text
Basil.Host                 start the server
Basil.Host --noconsole     dashboard without the command box
Basil.Host --nogui         plain log lines, no dashboard
Basil.Host <command>       run one console command (in the running server, or offline)
```

When stdin or stdout is redirected, the server falls back to `--nogui`. A setting can be overridden per run with
`--Basil:Server:Ports:Https=8443`.

## Name resolution on a LAN

Every client must resolve the domain and each served subdomain to the server:

```text
<domain>   c.<domain>   ce.<domain>  c4.<domain>  c5.<domain>  c6.<domain>
osu.<domain>   b.<domain>   a.<domain>   api.<domain>   assets.<domain>
```

With `Server:LanAdvertise`, the server answers multicast DNS for exactly these names. Most operating systems only
consult multicast DNS for `.local` names; other domains need a hosts-file entry or real DNS. The `ppy.sh` hosts are
served for redirected clients but never advertised. Set `LanAdvertise` to `false` to turn the advertisement off.

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

Basil stores its records in PostgreSQL, configured by `Database:ConnectionString` and `Database:WriteConnections`. One
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
