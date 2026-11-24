# Docker

Basil publishes its container image to Docker Hub: [thanhmai06/osubasil](https://hub.docker.com/r/thanhmai06/osubasil)

Each release is published with a version tag.

The image contains the Basil application, .NET runtime, and `ffmpeg`. It does not contain the database (Basil keeps its
records in PostgreSQL) or the web front: Basil only speaks plain HTTP and plain IRC, on ports that only the Compose
network reaches. The Compose file runs three services: `nginx`, which ends TLS and is the only one that publishes ports,
`basil`, and `postgres`.

```text
client ──TLS──► nginx (80, 443, 6697) ──HTTP :8080 / IRC :6667 + PROXY header──► basil ──► postgres
                  │                                                              │
                  └── reads Data/ and Cache/ (read-only) and serves stored files ◄─── names the file (X-Accel-Redirect)
```

nginx holds the per-address protection (request and connection rates, body sizes, timeouts), the certificate and the
delivery of stored files; Basil trusts the client address only from the nginx container. The nginx configuration is in
[`docker/nginx/`](../../docker/nginx/).

Docker Compose configuration is provided in the repository's root [`docker-compose.yml`](../../docker-compose.yml).

## Docker Compose

Use Docker Compose when the Basil repository is available on the deployment machine.

### 1. Configure Basil

Before the first run:

1. Copy [`.env.example`](../../.env.example) to `.env` and set `POSTGRES_PASSWORD`. Compose builds both database
   connection strings from it (`ConnectionStrings__Write` and `ConnectionStrings__Read`); no password is kept in
   `appsettings.json`, and `.env` is never committed.
2. Start once so Basil writes `docker-data/Data/appsettings.json`, then edit that file for the deployment, above all
   `Basil:Server:Domain`. The default certificate of nginx covers `basil.local` and its subdomains; for another domain, mount your own certificate (see
   [`https.md`](https.md)).
3. Give the `basil` container's user (`app`, id `1654`) the data directories:
   `mkdir -p docker-data/{Data,Cache,Logs} && chown -R 1654:1654 docker-data/Data docker-data/Cache docker-data/Logs`.
   Basil creates its files readable by others, which nginx needs for `Data` and `Cache`.

See [`configuration.md`](configuration.md) for the complete configuration reference and [`https.md`](https.md) for TLS.

The image carries the shipped files in `/app/Defaults/Data`. At every start Basil copies each one that is missing from
the `Data` volume and never overwrites a file, so the settings in the volume are the operator's own.

The `basil` container has `tty` and `stdin_open` on, so `docker attach` shows the console dashboard; detach with
`Ctrl+P` then `Ctrl+Q`.

### 2. Start Basil

From the repository root:

```bash
docker compose up --build -d
```

This builds the image and starts PostgreSQL and then Basil in the background. Basil creates its tables when it starts.

Persistent data is stored on the host under:

```text
docker-data/
├── Postgres/
├── Data/
├── Cache/
└── Logs/
```

These directories are bind-mounted into the containers. They survive container recreation and should be included in
backups as appropriate.

### 3. Install the calculation stack

The image carries no osu! library. Without them the server runs, but star ratings and pp stay empty and beatmapset
imports are refused. Install them once; they are kept in `Data/Rulesets/`:

```bash
docker compose exec basil ./Basil ruleset update
```

The command needs the network (it downloads about 100 MB from nuget.org). On a machine with no network, copy the
`Data/Rulesets/` folder of another install of the same Basil version into the volume and restart. Each file is checked
against the build at every start, so a wrong or damaged copy is refused, not loaded.

### 4. Create the first account

The server never creates an account by itself. Create the first administrator with the console command inside the
running container:

```bash
docker compose exec basil ./Basil users create --username admin --password change-me --admin
```

The command is forwarded to the running server. See [`console.md`](console.md) for the commands. The same command also
works against a stopped server, with `docker compose run --rm basil ./Basil users create ...`.

### 5. Check the logs

Follow the container logs:

```bash
docker compose logs -f
```

Stop the deployment:

```bash
docker compose down
```

`docker compose down` does not remove the `docker-data/` directory, so persistent data remains on the host.

The container receives `SIGTERM` when it stops, which starts the graceful shutdown. Basil has no restart command: the
Compose file's `restart: on-failure` restarts a container that crashed, and an operator restarts a deployment with
`docker compose restart basil`. A setting marked "applies after the server restarts" needs that restart.

---

## Running the image without Compose

The image alone is not a complete deployment: it needs a PostgreSQL database, an nginx in front of it (HTTP on
`basil:8080`, IRC on `basil:6667` with the PROXY protocol, as in [`docker/nginx/nginx.conf`](../../docker/nginx/nginx.conf))
and the environment variables `ConnectionStrings__Write`, `ConnectionStrings__Read`, `Basil__Server__TrustedProxies__0`
(the address or range nginx connects from) and, for nginx to serve the stored files, `Basil__Server__Files__AccelPrefix`.
Follow the Compose file as the reference for all of these.

---

## Updating a Docker deployment

### Docker Compose

To update a Compose deployment:

```bash
docker compose down
docker compose up --build -d
docker compose logs -f
```

Keep the existing `docker-data/` directory. Back up first (see below). The server only reports in its logs and dashboard when a newer release
exists; it never updates itself.

Basil applies database migrations and the `Data/` layout steps automatically when the new version starts. Before each
`docker compose pull`, take a `pg_dump` backup and copy `docker-data/Data`: migrations cannot be rolled back, so the
backup is the only way back. An older Basil refuses to start on the data of a newer one.

**Never replace `docker-data/Data/` or the database volume with an empty one during an upgrade.** They hold the stored
files and every record of the server.

---

## Persistent data

The container filesystem should be treated as disposable.

At minimum, persist:

```text
Data/
the PostgreSQL data directory
```

`Data/` holds the configuration files, the reply wording, and the stored files:

* beatmaps;
* avatars;
* replays;
* screenshots and other persistent application data.

Users, rooms, scores and the server policy settings are records in the PostgreSQL database. Back up the database with
`pg_dump` and `Data/` together, because records and files refer to each other.

`Logs/` contains operational history, including one file per connection session in `Logs/Sessions/`. Persist it if logs
need to survive container recreation; it is not required to preserve Basil's application state. The system log also goes
to standard output, so `docker compose logs` shows it.

`Cache/` (covers, difficulty attributes, audio previews) can be deleted when Basil is stopped. It is regenerated when
required. It is mounted so that nginx can serve covers from it.

See [`configuration.md`](configuration.md) for the complete `Data/` directory reference.

---

## TLS certificates

Basil holds no certificate. nginx ends TLS for HTTPS and for IRC (port `6697`) with the files under
`docker/nginx/certs/`; see [`https.md`](https.md) for how to mount your own.

---

## Docker-specific considerations

Docker changes how Basil is packaged and run. It does not change how clients connect to Basil.

The following remain the same as a normal executable deployment:

* Basil domain and subdomains
* DNS or hosts-file configuration
* TLS requirements
* the HTTPS port (nginx publishes it)
* firewall configuration
* account creation
* osu! client configuration
* persistent server data

For the complete deployment procedure, see [`deployment.md`](deployment.md).

## See also

* [`deployment.md`](deployment.md): manual executable deployment
* [`configuration.md`](configuration.md): configuration and persistent data
* [`console.md`](console.md): the console commands, exit codes and shutdown
* [`https.md`](https.md): TLS certificates and HTTPS
* [`troubleshooting.md`](troubleshooting.md): Docker-specific troubleshooting
