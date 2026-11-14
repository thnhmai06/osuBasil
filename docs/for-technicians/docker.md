# Docker

Basil publishes its container image to Docker Hub: [thanhmai06/osubasil](https://hub.docker.com/r/thanhmai06/osubasil)

Each release is published with a version tag.

The image contains the Basil application, .NET runtime, and `ffmpeg`. A Docker deployment therefore does not require a
separate .NET installation, a source checkout, or a manual `ffmpeg` installation on the host. It does not contain the
database: Basil keeps its records in PostgreSQL, which the Compose file runs as a second container and which you
provide yourself when you run the image directly.

Docker Compose configuration is provided in the repository's root [`docker-compose.yml`](../../docker-compose.yml).

## Docker Compose

Use Docker Compose when the Basil repository is available on the deployment machine.

### 1. Configure Basil

Before the first run, edit:

```text
src/Basil/Configurations/appsettings.json
```

Configure the settings required by the deployment, especially:

* `Basil:Server:Domain`
* `Kestrel:Certificates:Default:Path`
* `Kestrel:Certificates:Default:Password`
* `ConnectionStrings:Write` (and `Read`)

See [`configuration.md`](configuration.md) for the complete configuration reference and [`https.md`](https.md) for TLS
requirements.

The Compose setup bind-mounts this file into the container as read-only:

```text
src/Basil/Configurations/appsettings.json
        │
        └──► /app/Configurations/appsettings.json
```

The image already holds the bundled development TLS certificate (`/app/Configurations/basil.local.pfx`), so the default
configuration works as it is for a LAN. For a real certificate, mount it as a file and point
`Kestrel:Certificates:Default:Path` at it.

The database password appears twice: `POSTGRES_PASSWORD` of the `postgres` service and the connection strings in the
`command` of the `basil` service (a command-line setting wins over `appsettings.json`, and so does an environment
variable such as `ConnectionStrings__Write`). Change both before the first run.

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
├── Templates/
└── Logs/
```

These directories are bind-mounted into the containers. They survive container recreation and should be included in
backups as appropriate.

### 3. Create the first account

The server never creates an account by itself. Create the first administrator with the console command inside the
running container:

```bash
docker compose exec basil ./Basil users create --username admin --password change-me --admin
```

The command is forwarded to the running server. See [`console.md`](console.md) for the commands. The same command also
works against a stopped server, with `docker compose run --rm basil ./Basil users create ...`.

### 4. Check the logs

Follow the container logs:

```bash
docker compose logs -f
```

Stop the deployment:

```bash
docker compose down
```

`docker compose down` does not remove the `docker-data/` directory, so persistent data remains on the host.

The container receives `SIGTERM` when it stops, which starts the graceful shutdown. The exit code `75` that the
`restart` command uses asks the container runtime to start the server again, and the Compose file's
`restart: unless-stopped` does so; see [`console.md`](console.md).

---

## Using the Docker image directly

A machine that only runs Basil does not need a source checkout. Pull the release image directly from Docker Hub.

Pull a specific version:

```bash
docker pull thanhmai06/osubasil:<version>
```

You need a reachable PostgreSQL database and a `Configurations/` directory on the host. Extract the image's copy of
`Configurations/` once, then edit its `appsettings.json` (domain, certificate, and the database connection strings):

```bash
container=$(docker create thanhmai06/osubasil:<version>)
docker cp "$container:/app/Configurations" ./Configurations
docker rm "$container"
```

```text
./
├── Configurations/
│   ├── appsettings.json
│   ├── appsettings.schema.json
│   ├── basil.local.pfx
│   └── Templates/
└── docker-data/
    ├── Data/
    └── Logs/
```

Run the container:

```bash
docker run -d --name basil -p 443:443 -p 6667:6667 \
  -v "$PWD/Configurations:/app/Configurations" \
  -v "$PWD/docker-data/Data:/app/Data" \
  -v "$PWD/docker-data/Logs:/app/Logs" \
  thanhmai06/osubasil:<version>
```

The published image is a `linux-x64` build. The container expects the application files and persistent directories in
the same layout as the normal executable deployment:

```text
/app/
├── Basil
├── Configurations/
├── Data/
└── Logs/
```

Mount `Configurations/` and `Data/` from the host so that the configuration, the TLS certificate, BasilBot's reply
template and the stored files survive container replacement. Mount the whole `Configurations/` directory only after
extracting the image's copy as shown above: an empty mount would hide the settings and the certificate built into the
image.

---

## Updating a Docker deployment

### Docker Compose

To update a Compose deployment:

1. Stop Basil.
2. Update the Basil image or repository checkout.
3. Start the new version.
4. Check the startup logs.
5. Verify the server is responding.

For a repository-based deployment:

```bash
docker compose down
docker compose up --build -d
docker compose logs -f
```

Keep the existing `docker-data/` directory.

### Direct Docker image

To update a manually managed container:

```bash
docker stop basil
docker rm basil

docker pull thanhmai06/osubasil:<new-version>

docker run -d --name basil -p 443:443 -p 6667:6667 \
  -v "$PWD/Configurations:/app/Configurations" \
  -v "$PWD/docker-data/Data:/app/Data" \
  -v "$PWD/docker-data/Logs:/app/Logs" \
  thanhmai06/osubasil:<new-version>
```

The existing `Configurations/` and `docker-data/` are reused. Compare the new image's `Configurations/appsettings.json`
(`docker cp` as above) with yours when a release adds settings.

Basil applies database migrations automatically when the new version starts. Take a `pg_dump` backup before the
upgrade: migrations cannot be rolled back, so the backup is the only way back to the previous schema.

**Never replace `docker-data/Data/` or the database volume with an empty one during an upgrade.** They hold the stored
files and every record of the server.

---

## Persistent data

The container filesystem should be treated as disposable.

At minimum, persist:

```text
Configurations/
Data/
the PostgreSQL data directory
```

`Data/` holds the stored files:

* beatmaps;
* avatars;
* replays;
* screenshots and other persistent application data.

Users, rooms, scores and the server policy settings are records in the PostgreSQL database. Back up the database with
`pg_dump` and `Data/` together, because records and files refer to each other.

`Logs/` contains operational history. Persist it if logs need to survive container recreation; it is not required to
preserve Basil's application state.

The cache under `Data/Cache/` can be deleted when Basil is stopped. It is regenerated when required.

See [`configuration.md`](configuration.md) for the complete `Data/` directory reference.

---

## TLS certificates

Docker does not change Basil's TLS requirements.

If Basil terminates HTTPS itself, the container must have access to the configured certificate file specified by:

```text
Kestrel:Certificates:Default:Path
```

and its corresponding:

```text
Kestrel:Certificates:Default:Password
```

The certificate file must therefore be accessible from inside the container, normally through a bind mount.

For example:

```bash
-v "$PWD/certs:/certs:ro"
```

with:

```text
Kestrel:Certificates:Default:Path=/certs/basil.pfx
```

See [`https.md`](https.md) for certificate requirements and the bundled development certificate.

---

## Docker-specific considerations

Docker changes how Basil is packaged and run. It does not change how clients connect to Basil.

The following remain the same as a normal executable deployment:

* Basil domain and subdomains
* DNS or hosts-file configuration
* TLS requirements
* HTTPS port
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
