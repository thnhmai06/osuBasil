# Deployment

*For containerized deployments, see [`docker.md`](docker.md).*

## Overview

Basil runs as a single process. There is no separate database or cache service to install or maintain.

Persistent server data is stored under `Data/` next to the executable. Logs are stored under `Logs/`. Both directories
are created automatically when Basil starts for the first time.

This guide covers a manual deployment where clients connect to Basil over a LAN or the public internet.

---

## Prerequisites

Before deploying Basil, make sure you have:

* A supported operating system and architecture.
* `ffmpeg` installed. *(optional)*
* A hostname or domain that clients can resolve to the server.
* A TLS certificate covering the Basil hostname and its required subdomains.
* Permission to bind to the configured HTTPS port.
* Firewall access for the configured Basil port.

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
├── Basil.Web
├── Data/
│   ├── appsettings.json
│   └── Localization/
│       ├── BasilBot.json
│       ├── Irc.json
│       └── Server.json
└── Logs/
```

`Data/`, `Logs/` may be created automatically on first startup, depending on the release.

If you are building Basil yourself instead of using a release, see [`development.md`](../for-developers/development.md).

---

### 2. Configure Basil

Edit [`Data/appsettings.json`](../../src/Basil.Web/Data/appsettings.json) under the Basil executable's `Data/` directory.

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

#### Admin key

A new database has no admin key and therefore starts in **bypass mode**.

Before allowing clients to connect, set an admin key using:

```text
PUT /adminkey
```

**Do not expose a server to untrusted users while it is in bypass mode.**

See [`configuration.md`](configuration.md) for the admin-key procedure.

#### Optional settings

The following are normally optional:

```text
Basil:Bot:Name
Basil:Bot:CommandPrefix
Basil:Irc:Name
Basil:Irc:Port
```

---

### 3. Configure HTTPS

Basil requires HTTPS for osu! stable clients.

Configure a TLS certificate covering the Basil domain and all required service subdomains.

Configure:

```text
Basil:Server:CertPath
Basil:Server:CertPassword
```

when Basil is responsible for TLS termination.

**By default, Basil ships with a TLS certificate for local and LAN development on `Data/`.** No certificate
configuration is required for a basic deployment.

For a public deployment, replace the bundled certificate with a certificate issued for your server's domain and all
required subdomains. See [`https.md`](https.md) for certificate requirements and configuration.

If TLS is terminated by a reverse proxy, configure the proxy instead and leave Basil's certificate settings unset as
appropriate for that deployment.

---

### 4. Configure DNS or hosts files

Every machine that connects to Basil must be able to resolve all required service subdomains to the Basil server.

- For a public deployment, create the appropriate DNS records.

- For a LAN deployment, add the required entries to the hosts file on each client and on the server itself.

See [`getting-started.md`](../for-client/bancho/getting-started.md) for the exact hostnames and client-side configuration.

---

### 5. Start Basil

The HTTPS port is controlled by:

```text
Basil:Server:Port
```

The default is `443`. **This is the port that osu! client will connect to, so don't change it unless you are sure you
know what you are doing.**

On Linux and macOS, binding to a port below `1024` normally requires elevated privileges or an appropriate capability
configuration.

Start Basil from its executable directory:

```bash
./Basil.Web
```

On Windows, run the published executable normally or through the service mechanism used by your deployment environment.

If a firewall is enabled, allow inbound traffic to the configured Basil port.

---

### 6. Verify the server

From the server or another machine that can reach it, run:

```bash
curl -k https://osu.<domain>/web/bancho_connect.php
```

A healthy server should return HTTP:

```text
200 OK
```

If this fails, check:

1. Basil is running.
2. The configured port is reachable.
3. The firewall allows the port.
4. DNS or hosts entries resolve correctly.
5. The TLS certificate is valid for the requested hostname.
6. Basil's startup log for configuration or binding errors.

---

## Create the first account

There are two ways to create the first account.

### Option A: In-game registration

1. Configure the osu! client to connect to the Basil server.
2. Open the login screen.
3. Select **Register**.
4. Enter the Basil admin key in the **Email** field.
5. Choose the username and password.
6. Complete registration.

New accounts are created with these privileges:

```text
Unrestricted | Verified | Supporter
```

See [`getting-started.md`](../for-client/bancho/getting-started.md).

### Option B: Admin API

Create an account through the `api.<domain>` host:

```bash
curl -X POST https://api.<domain>/users \
  -H "Authorization: Bearer <your admin key>" \
  -H "Content-Type: application/json" \
  -d '{"name":"Player1","password":"hunter2","country":"vn","privilege":19}'
```

The request requires:

* `name`
* `password`
* `country`: two-letter lowercase country code
* `privilege`: numeric privilege bitfield

`19` corresponds to:

```text
Unrestricted | Verified | Supporter
```

See [`privileges.md`](../for-developers/privileges.md) for the complete privilege reference.

Users are automatically given the `Verified` flag when they complete their first login. Additional privileges can be
managed through:

```text
PATCH /users/{userId}
```

---

## Moving Basil to another machine

Basil's deployment consists of three important categories:

| Item             | Purpose                                     | Copy during migration?                          |
|------------------|----------------------------------------------|--------------------------------------------------|
| Executable files | Basil application                           | If the target machine doesn't have               |
| `Data/`          | Persistent server state, including `appsettings.json` | If the source state should be migrated |
| `Logs/`          | Operational history                         | Normally no                                       |

### Full migration

To move an existing Basil server to another machine:

1. Stop Basil on the source machine.
2. Copy the complete executable directory to the target.
3. Make sure `Data/` is included. `Logs/` does not need to be copied.
4. Verify the configuration on the target, especially:
	* `Basil:Server:Domain`
	* HTTPS certificate settings
	* `Basil:Server:Port`
5. Start Basil on the target.
6. Verify the server using the connectivity check above.

The `Data/` directory contains the configuration, database, and other persistent state. **Do not omit it if the
intention is to preserve the existing server.**

### Upgrade an existing installation

If the target already contains a Basil installation:

1. Stop Basil.
2. Replace the application files with the new release.
3. Keep the existing `Data/` directory (this carries `appsettings.json` too, unless configuration changes are required).
4. Start Basil.
5. Check the startup log for errors.

Do **not** replace `Data/` with the empty `Data/` directory from a release archive.

Basil performs database migrations automatically when it starts.

---

## See also

* [`configuration.md`](configuration.md): configuration, admin key, and persistent data
* [`https.md`](https.md): TLS certificates and HTTPS
* [`docker.md`](docker.md): Docker deployment
* [`getting-started.md`](../for-client/bancho/getting-started.md): configuring an osu! client
* [`troubleshooting.md`](troubleshooting.md): common deployment problems and fixes
