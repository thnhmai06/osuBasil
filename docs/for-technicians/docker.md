# Docker

Basil publishes its container image to Docker Hub: [thanhmai06/osubasil](https://hub.docker.com/r/thanhmai06/osubasil)

Each release is published with a version tag.

The image contains the Basil application, .NET runtime, and `ffmpeg`. A Docker deployment therefore does not require a
separate .NET installation, a source checkout, or a manual `ffmpeg` installation on the host.

Docker Compose configuration is provided in the repository's root [`docker-compose.yml`](../../docker-compose.yml).

## Docker Compose

Use Docker Compose when the Basil repository is available on the deployment machine.

### 1. Configure Basil

Before the first run, edit:

```text
src/Basil.Web/Data/appsettings.json
```

Configure the settings required by the deployment, especially:

* `Basil:Server:Domain`
* `Basil:Server:CertPath`
* `Basil:Server:CertPassword`

See [`configuration.md`](configuration.md) for the complete configuration reference and [`https.md`](https.md) for TLS
requirements.

The Compose setup bind-mounts this file into the container as read-only:

```text
src/Basil.Web/Data/appsettings.json
        │
        └──► /app/Data/appsettings.json
```

`src/Basil.Application/Data/Localization/` (reply text, see [`configuration.md`](configuration.md)) is
bind-mounted the same way, so it can be edited on the host and survives container recreation just like
`appsettings.json` — without this mount, the broader `docker-data/Data` volume below would shadow the
localization files built into the image, and Basil would fail to start. The bundled development TLS
certificate (`data/certs/basil.local.pfx`) is bind-mounted individually for the same reason.

### 2. Start Basil

From the repository root:

```bash
docker compose up --build -d
```

This builds the image and starts Basil in the background.

> Verified against a real container build and a genuinely fresh (empty) `docker-data/` directory:
> `docker compose up --build -d` produces a running server that answers `/health`, and
> `/settings/motd` / `/settings/mirror` / `/settings/adminkey` round-trip correctly through it.

Persistent data is stored on the host under:

```text
docker-data/
├── Data/
└── Logs/
```

These directories are bind-mounted into the container. They survive container recreation and should be included in
backups as appropriate.

### 3. Set the admin key

A new database has no admin key and therefore starts in **bypass mode**.

After Basil is running, set the admin key through:

```text
PUT /settings/adminkey
```

on the `api.<domain>` host.

The key is stored in the database and does not require a container restart after being changed.

**Do not expose a new Basil deployment to untrusted users while it is in bypass mode.**

See [`configuration.md`](configuration.md).

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

---

## Using the Docker image directly

A machine that only runs Basil does not need a source checkout. Pull the release image directly from Docker Hub.

Pull a specific version:

```bash
docker pull thanhmai06/osubasil:<version>
```

Prepare the configuration file and persistent directories on the host. `appsettings.json` is written from scratch
using [`configuration.md`](configuration.md)'s setting reference; the `Localization/` files are not — extract the
image's copies once instead of writing 150+ reply strings by hand:

The bundled development TLS certificate (`basil.local.pfx`) needs the same individual-file treatment
— extract it alongside `Localization/`:

```bash
container=$(docker create thanhmai06/osubasil:<version>)
docker cp "$container:/app/Data/Localization" ./Localization
docker cp "$container:/app/Data/basil.local.pfx" ./basil.local.pfx
docker rm "$container"
```

```text
./
├── appsettings.json
├── basil.local.pfx
├── Localization/
│   ├── BasilBot.json
│   └── Irc.json
└── docker-data/
    ├── Data/
    └── Logs/
```

Run the container:

```bash
docker run -d --name basil -p 443:443 \
  -v "$PWD/appsettings.json:/app/Data/appsettings.json:ro" \
  -v "$PWD/Localization:/app/Data/Localization:ro" \
  -v "$PWD/basil.local.pfx:/app/Data/basil.local.pfx:ro" \
  -v "$PWD/docker-data/Data:/app/Data" \
  -v "$PWD/docker-data/Logs:/app/Logs" \
  thanhmai06/osubasil:<version>
```

The published image is a `linux-x64` build.

The container expects the application files and persistent directories in the same layout as the normal executable
deployment:

```text
/app/
├── Data/
│   ├── appsettings.json
│   ├── basil.local.pfx
│   └── Localization/
│       ├── BasilBot.json
│       └── Irc.json
└── Logs/
```

Mount `appsettings.json`, `basil.local.pfx`, and `Localization/` (into their paths above), plus `Data/` from the
host, so configuration, the TLS certificate, reply text, and server state (including the MOTD, now a
database-backed setting) survive container replacement. All three must be mounted individually, not just `Data/`:
the broader `Data/` mount would otherwise shadow the image's own copies of them, and a missing `Localization/`
file specifically prevents Basil from starting at all (see [`configuration.md`](configuration.md)).

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

docker run -d --name basil -p 443:443 \
  -v "$PWD/appsettings.json:/app/Data/appsettings.json:ro" \
  -v "$PWD/Localization:/app/Data/Localization:ro" \
  -v "$PWD/basil.local.pfx:/app/Data/basil.local.pfx:ro" \
  -v "$PWD/docker-data/Data:/app/Data" \
  -v "$PWD/docker-data/Logs:/app/Logs" \
  thanhmai06/osubasil:<new-version>
```

The existing `appsettings.json`, `basil.local.pfx`, `Localization/`, and `docker-data/` are reused.

Basil applies database migrations automatically when the new version starts.

**Never replace `docker-data/Data/` with an empty directory during an upgrade.** It contains the existing Basil database
and other persistent server state.

---

## Persistent data

The container filesystem should be treated as disposable.

At minimum, persist:

```text
Data/
```

This contains the Basil database and server state, including:

* users and server configuration stored in the database;
* beatmaps;
* avatars;
* replays;
* other persistent application data.

`Logs/` contains operational history. Persist it if logs need to survive container recreation; it is not required to
preserve Basil's application state.

The cache under `Data/Cache/` can be deleted when Basil is stopped. It is regenerated when required.

See [`configuration.md`](configuration.md) for the complete `Data/` directory reference.

---

## TLS certificates

Docker does not change Basil's TLS requirements.

If Basil terminates HTTPS itself, the container must have access to the configured certificate file specified by:

```text
Basil:Server:CertPath
```

and its corresponding:

```text
Basil:Server:CertPassword
```

The certificate file must therefore be accessible from inside the container, normally through a bind mount.

For example:

```bash
-v "$PWD/certs:/certs:ro"
```

with:

```text
Basil:Server:CertPath=/certs/basil.pfx
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
* admin-key setup
* account creation
* osu! client configuration
* persistent server data

For the complete deployment procedure, see [`deployment.md`](deployment.md).

## See also

* [`deployment.md`](deployment.md): manual executable deployment
* [`configuration.md`](configuration.md): configuration, admin key, and persistent data
* [`https.md`](https.md): TLS certificates and HTTPS
* [`troubleshooting.md`](troubleshooting.md): Docker-specific troubleshooting
