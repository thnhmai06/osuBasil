# Deployment

*For containerized deployments, see [`docker.md`](docker.md).*

## Overview

Basil runs as a single process and keeps its records in a PostgreSQL database that you provide. There is no cache
service to install.

Settings and the certificate live in `Configurations/` next to the executable. Stored files (replays, avatars,
beatmapsets, screenshots) are under `Data/`. Logs are under `Logs/`. These directories are created automatically when
Basil starts for the first time; Basil creates its schema and applies migrations itself, but the database must already
exist.

This guide covers a manual deployment where clients connect to Basil over a LAN or the public internet.

---

## Prerequisites

Before deploying Basil, make sure you have:

* A supported operating system and architecture.
* A PostgreSQL server and an empty database with a role that may create tables in it (the test suite runs against
  PostgreSQL 16 and 17). One database belongs to one Basil process.
* `ffmpeg` installed. *(optional)*
* A hostname or domain that clients can resolve to the server.
* A TLS certificate covering the Basil hostname and its required subdomains.
* Privilege to bind to the configured HTTPS port.
* Firewall access for the configured Basil ports (HTTPS, and IRC if you use it).

Basil releases are self-contained and include the required .NET runtime. **You do not need to install .NET separately.**

Supported release platforms:

* `win-x64`
* `win-arm64`
* `linux-x64`
* `linux-arm64`
* `osx-x64`
* `osx-arm64`

### Install ffmpeg

`ffmpeg` is required for beatmap audio previews. Without it, audio preview requests return `503 Service Unavailable`;
Basil itself continues running.

Install it using the appropriate package manager:

```bash
# Debian / Ubuntu
sudo apt install ffmpeg

# Windows
winget install ffmpeg

# macOS
brew install ffmpeg
```

See [`beatmap-ingestion.md`](../for-developers/beatmap-ingestion.md) for details.

---

## Deployment

### 1. Download Basil

Download the latest release for the target platform from
the [latest releases](https://github.com/thnhmai06/osuBasil/releases/latest).

Extract:

```text
Basil-{version}-{platform}.zip
```

to the directory where Basil will run.

For example:

```text
/opt/basil/
├── Basil
└── Configurations/
    ├── appsettings.json
    ├── appsettings.schema.json
    ├── basil.local.pfx
    └── Templates/
```

`Data/` and `Logs/` are created on first startup.

If you are building Basil yourself instead of using a release, see [`development.md`](../for-developers/development.md).

---

### 2. Configure Basil

Edit [`Configurations/appsettings.json`](../../src/Basil/Configurations/appsettings.json) beside the executable.
Settings can also be given on the command line (`--Basil:Server:Domain=tourney.example`), which wins over the file.

See [`configuration.md`](configuration.md) for the complete configuration reference.

Most deployments need to configure at least:

#### Server domain

Set:

```text
Basil:Server:Domain
```

to the hostname clients will use to connect to Basil.

Examples:

```text
tourney.example
```

or, for a LAN deployment:

```text
basil.local
```

Basil derives its required service subdomains from this value.

#### Database

Set:

```text
ConnectionStrings:Write
ConnectionStrings:Read
```

to the Npgsql connection strings of the PostgreSQL database that belongs to this Basil process: `Write` stores changes and
migrates, `Read` serves reads (it may point at a replica; leave it empty to read from `Write`). Basil applies its
migrations when it starts.

#### Optional settings

The following are normally optional:

```text
Kestrel:Endpoints:Https:Url
Basil:Server:Irc:Port
Basil:Server:TrustedProxies
Logging:LogLevel:Default
```

Policy that users feel while the server runs (locks, rate limits, upload limits, session lifetimes, the bot prefix) is
not in this file: it is stored in the database and changed through the API or the console without a restart. See
[`configuration.md`](configuration.md).

---

### 3. Configure HTTPS

Basil requires HTTPS for osu! stable clients.

Configure a TLS certificate covering the Basil domain and all required service subdomains.

Configure:

```text
Kestrel:Certificates:Default:Path
Kestrel:Certificates:Default:Password
```

when Basil is responsible for TLS termination.

**By default, Basil ships with a TLS certificate for local and LAN development in `Configurations/basil.local.pfx`.**
No certificate configuration is required for a basic LAN deployment.

For a public deployment, replace the bundled certificate with a certificate issued for your server's domain and all
required subdomains. See [`https.md`](https.md) for certificate requirements and configuration.

If TLS is terminated by a reverse proxy, give the endpoint an `http://` URL and remove the certificate so that Basil serves plain HTTP behind the
proxy, and list the proxy in `Basil:Server:TrustedProxies` so that the client address is taken from its forwarding
headers.

---

### 4. Configure DNS or hosts files

Every machine that connects to Basil must be able to resolve all required service subdomains to the Basil server.

- For a public deployment, create the appropriate DNS records.

- For a LAN deployment, the server advertises its own domain over multicast DNS by default, so
  clients on the same network resolve it with no configuration. This only applies to domains ending
  in `.local`, which includes the default `basil.local`.

- Otherwise — a domain outside `.local`, a network that blocks multicast, or a client that ignores
  it — add the required entries to the hosts file on each client and on the server itself.

See [Name resolution on a LAN](configuration.md#name-resolution-on-a-lan) for both methods and the
exact list of names, and [`getting-started.md`](../for-client/osu-stable/getting-started.md) for the
client-side configuration.

---

### 5. Create the first account

The server never creates an account by itself, and the first administrator is made with the console, which works on
the storage while the server is stopped:

```bash
./Basil users create --username admin --password change-me --admin
```

`--admin` gives the account every privilege. The console commands are specified in [`console.md`](console.md).

---

### 6. Start Basil

The HTTPS port is controlled by the URL of:

```text
Kestrel:Endpoints:Https:Url
```

The default is `443`. **This is the port that osu! client will connect to, so don't change it unless you are sure you
know what you are doing.**

On Linux and macOS, binding to a port below `1024` normally requires elevated privileges or an appropriate capability
configuration.

Start Basil from its executable directory:

```bash
./Basil
```

The server shows a terminal dashboard. Under a service manager (systemd, Docker) stdin and stdout are redirected and it
falls back to plain log lines by itself; use `--nogui` to ask for that explicitly. `SIGTERM` starts a graceful shutdown.
Under systemd or a container runtime, the exit code `75` asks the manager to start the server again (the `restart`
command), while `0` is a normal stop; see [`console.md`](console.md).

A minimal systemd unit for a server installed under `/opt/basil`:

```ini
[Unit]
Description=Basil
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=basil
WorkingDirectory=/opt/basil
ExecStart=/opt/basil/Basil --nogui
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

On Windows, run the published executable normally or through the service mechanism used by your deployment environment.

If a firewall is enabled, allow inbound traffic to the configured Basil ports.

---

### 7. Verify the server

From the server or another machine that can reach it, run:

```bash
curl -k https://api.<domain>/v1/users/1
```

A healthy server should answer HTTP `200 OK` with the account of BasilBot.

If this fails, check:

1. Basil is running.
2. The configured port is reachable.
3. The firewall allows the port.
4. DNS or hosts entries resolve correctly.
5. The TLS certificate is valid for the requested hostname.
6. Basil's startup log for configuration, database or binding errors.

---

## Create more accounts

### In-game registration

1. Configure the osu! client to connect to the Basil server.
2. Open the login screen.
3. Select **Register**.
4. Choose the username and password. The email field is not used.
5. Complete registration.

New accounts receive the default privilege set. Registration is limited per address and per server by the registration
rate limit, and while the **accounts** lock is on only account managers can create accounts; see
[`configuration.md`](configuration.md). See [`getting-started.md`](../for-client/osu-stable/getting-started.md).

### Console or API

An administrator can create an account with the console:

```bash
./Basil users create --username Player1 --password hunter2 --country vn
```

or through the `api.<domain>` host, with the Bearer token of an account that may manage users (sign in with
`POST /v1/auth/login`):

```bash
curl -X POST https://api.<domain>/v1/users \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"username":"Player1","password":"hunter2","country":"vn"}'
```

`username` and `password` are required; `country` is a two-letter code and `privileges` is an optional bitmask
(default: the default privileges). See [`privileges.md`](../for-developers/privileges.md) for the privilege reference.

---

## Moving Basil to another machine

Basil's deployment consists of these categories:

| Item             | Purpose                                              | Copy during migration?                          |
|------------------|------------------------------------------------------|-------------------------------------------------|
| Executable files | Basil application                                    | If the target machine doesn't have them         |
| `Configurations/`| deployment settings, certificate, templates          | Yes                                             |
| `Data/`          | stored files: replays, avatars, beatmapsets          | If the source state should be migrated          |
| PostgreSQL       | all records                                          | Yes (`pg_dump` and restore)                     |
| `Logs/`          | Operational history                                  | Normally no                                     |

### Full migration

To move an existing Basil server to another machine:

1. Stop Basil on the source machine.
2. Dump the database with `pg_dump` and restore it on the target; copy `Configurations/` and `Data/` with the
   executable. Records and files refer to each other, so take them together. `Logs/` does not need to be copied.
3. Verify the configuration on the target, especially:
	* `Basil:Server:Domain`
	* HTTPS certificate settings
	* `Kestrel:Endpoints:Https:Url`
	* `ConnectionStrings:Write`
4. Start Basil on the target.
5. Verify the server using the connectivity check above.

**Do not omit the database or `Data/` if the intention is to preserve the existing server.**

### Upgrade an existing installation

If the target already contains a Basil installation:

1. Stop Basil.
2. Back up the database with `pg_dump`. Basil applies migrations automatically when the new version starts, and
   migrations cannot be rolled back.
3. Replace the application files with the new release.
4. Keep the existing `Configurations/` and `Data/` directories (unless configuration changes are required).
5. Start Basil.
6. Check the startup log for errors.

Do **not** replace `Configurations/` or `Data/` with the ones from a release archive.

Basil performs database migrations automatically when it starts. Keep the backup until the new version has started
cleanly: the previous schema cannot be restored without it.

---

## See also

* [`configuration.md`](configuration.md): configuration and persistent data
* [`console.md`](console.md): the console commands, exit codes and shutdown
* [`https.md`](https.md): TLS certificates and HTTPS
* [`docker.md`](docker.md): Docker deployment
* [`getting-started.md`](../for-client/osu-stable/getting-started.md): configuring an osu! client
* [`troubleshooting.md`](troubleshooting.md): common deployment problems and fixes
