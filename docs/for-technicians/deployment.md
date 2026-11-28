# Deployment

## Overview

Basil is deployed as a Docker Compose stack of three services: `nginx`, `basil` and `postgres`. nginx is the only
service that publishes ports. It ends TLS for HTTP (ports `80` and `443`) and for IRC (port `6697`), holds the
per-address protection and serves the stored files. Basil speaks plain HTTP and plain IRC to nginx inside the Compose
network and keeps its records in PostgreSQL.

This is the only supported way to run a server. The reasons are in [`docker.md`](docker.md): one image for every
platform, no certificate or per-address rule inside Basil, and an update that is one `docker compose pull`.
[`docker.md`](docker.md) explains how the stack is built and how to update it; this page is the order of steps from an
empty machine to a server that clients can sign in to. Running from source is for contributors; see
[`development.md`](../for-developers/development.md).

The settings and the stored files live in `Data/`, which is the one folder a backup needs besides the database. See
[`configuration.md`](configuration.md).

---

## Prerequisites

* A Linux, Windows or macOS machine with Docker Engine and Docker Compose v2. The image is built for `amd64` and
  `arm64`. You do not install .NET, PostgreSQL, nginx or `libsndfile1` yourself: they are in the stack.
* A domain name that clients can resolve to the machine, or a LAN where you can edit each client's hosts file.
* A TLS certificate covering the domain and the subdomains listed in [`https.md`](https.md). The stack ships a
  self-signed certificate for `basil.local` that is enough for a LAN and must not be used on the Internet.
* Inbound access to ports `80`, `443` and `6697` (the last only if you use IRC).

---

## Deployment

### 1. Get the stack

The Compose file and the nginx configuration are in the repository. Check out the tag of the release you want:

```bash
git clone --branch <tag> https://github.com/thnhmai06/osuBasil.git
cd osuBasil
```

The image of each release is published to `ghcr.io/thnhmai06/osubasil` under its version tag and `latest`. The Compose
file builds the image from the checkout; [`docker.md`](docker.md#using-the-published-image) shows how to use the
published image instead.

### 2. Configure Basil

Copy [`.env.example`](../../.env.example) to `.env` and set `POSTGRES_PASSWORD`. Every secret and every value that
depends on the deployment comes from the environment, never from `appsettings.json`; see
[`configuration.md`](configuration.md).

Give the `basil` container's user the folders it writes, then start the stack once so Basil writes its settings:

```bash
mkdir -p docker-data/{Data,Cache,Logs}
chown -R 1654:1654 docker-data/Data docker-data/Cache docker-data/Logs
docker compose up --build -d
```

Set `BASIL_DOMAIN` in `.env` before the first start: the hostname clients use, from which Basil derives its
subdomains. nginx's `server_name` and the certificate must match it. For a LAN the default `basil.local` works as it
is.

### 3. Configure HTTPS

osu! stable requires HTTPS, and IRC is TLS only. nginx holds the certificate, never Basil. For a LAN or a trial the
default certificate for `basil.local` needs no change. For a server on the Internet, mount a certificate that covers
the domain and every subdomain over the two files nginx reads. [`https.md`](https.md) lists the names and shows how,
including the setup behind Cloudflare.

### 4. Configure DNS or hosts files

Every machine that connects must resolve the domain and each subdomain to the server:

```text
<domain>   c.<domain>   ce.<domain>   c4.<domain>   c5.<domain>   c6.<domain>
osu.<domain>   b.<domain>   a.<domain>   api.<domain>   assets.<domain>   irc.<domain>
```

* For a public deployment, create the DNS records (a wildcard record covers them all).
* For a LAN, Basil does not advertise its names. Add the names to the hosts file of each client and of the server, or to
  your own DNS. See [Name resolution on a LAN](configuration.md#name-resolution-on-a-lan) for the list and an example.

The `irc.` name is needed only by IRC clients, and an IRC client connects to nginx directly, so behind Cloudflare that
record must stay DNS only. See [`https.md`](https.md#irc).

### 5. Install the calculation stack

The image carries none of the osu! libraries Basil uses to calculate star rating and pp. The server runs without them,
but ratings and pp stay empty and beatmapset imports are refused. Install them once:

```bash
docker compose exec basil ./Basil ruleset update
```

See [`docker.md`](docker.md#3-install-the-calculation-stack) for what the command downloads and how to install it with no
network.

### 6. Create the first account

The server never creates an account by itself. Make the first administrator with the console command inside the running
container:

```bash
docker compose exec basil ./Basil users create --username admin --password change-me --admin
```

`--admin` gives the account every privilege. The console commands are specified in [`console.md`](console.md).

### 7. Verify the server

From a machine that resolves the names, run:

```bash
curl -k https://api.<domain>/v1/users/1
```

A healthy server answers HTTP `200 OK` with the account of BasilBot. If this fails, check in order:

1. `docker compose ps` shows the three services running, and `basil` healthy.
2. Ports `80`, `443` and `6697` are open in the firewall.
3. DNS or hosts entries resolve every name to the server.
4. The certificate covers the requested hostname (`docker compose logs nginx` names a file nginx cannot read).
5. The system log in `docker-data/Logs/` for configuration, database or binding errors.

---

## Create more accounts

### In-game registration

1. Configure the osu! client to connect to the Basil server.
2. Open the login screen.
3. Select **Register**.
4. Choose the username and password. The email field is not used.
5. Complete registration.

New accounts receive the default privilege set. Registration is limited for the whole server by the registration rate
limit (nginx limits it per address), and while the **accounts** lock is on only account managers can create accounts; see
[`configuration.md`](configuration.md). See [`getting-started.md`](../for-client/osu-stable/getting-started.md).

### Console or API

An administrator can create an account with the console:

```bash
docker compose exec basil ./Basil users create --username Player1 --password hunter2 --country vn
```

or through the `api.<domain>` host, with the Bearer token of an account that may manage users (sign in with
`POST /v1/auth/login`):

```bash
curl -X POST https://api.<domain>/v1/users \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"username":"Player1","password":"hunter2","country":"vn"}'
```

`username` and `password` are required; `country` is a two-letter code and `privileges` is an optional list of privilege
names (default: the default privileges). See [`privileges.md`](../for-developers/privileges.md) for the privilege reference.

---

## Moving Basil to another machine

A deployment consists of these categories:

| Item | Purpose | Copy during migration? |
|---|---|---|
| The checkout | `docker-compose.yml`, `.env`, `docker/nginx/` (and your own certificate files) | Yes |
| `docker-data/Data/` | settings, reply wording, stored files: replays, avatars, beatmapsets | Yes |
| PostgreSQL | all records, in the `postgres` volume | Yes (`pg_dump` and restore) |
| `docker-data/Cache/` | rebuilt on demand | No |
| `docker-data/Logs/` | operational history | Normally no |

To move an existing server:

1. Stop the stack on the source machine with `docker compose down`.
2. Dump the database with `pg_dump` (start only the `postgres` service for this) and restore it on the target. Copy the
   checkout and `docker-data/Data/`. Records and files refer to each other, so take them together.
3. Copy `.env`, and check `BASIL_DOMAIN`, the certificate files and the DNS records of the new machine.
4. Start the stack on the target and verify the server as in step 7.

**Do not omit the database or `Data/` if the intention is to preserve the existing server.**

Updating a running server is in [`docker.md`](docker.md#updating-a-docker-deployment).

---

## See also

* [`docker.md`](docker.md): the stack, updates and persistent data
* [`configuration.md`](configuration.md): configuration and persistent data
* [`console.md`](console.md): the console commands, exit codes and shutdown
* [`https.md`](https.md): TLS certificates and HTTPS
* [`getting-started.md`](../for-client/osu-stable/getting-started.md): configuring an osu! client
* [`troubleshooting.md`](troubleshooting.md): common deployment problems and fixes
