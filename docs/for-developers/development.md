# Development

This guide covers developing Basil from source. It is intended for contributors who need to build, run, test, or modify
the server locally.

## Prerequisites

* The [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet).
* A PostgreSQL server. Basil keeps its records in PostgreSQL, so a development instance needs one; the simplest is a
  container (see [Clone and run](#clone-and-run)). Docker is also what database-backed tests use when no `BASIL_TEST_POSTGRES` server is set (see [Run tests](#run-tests)).
* [FFmpeg](https://ffmpeg.org), on `PATH`, for beatmapset audio previews (the container image includes it).
* Docker with Compose, for PostgreSQL and for the stack with nginx.

## Clone and run

Two ways to run Basil from a checkout, each for a purpose:

* `dotnet run` runs the server alone, on plain HTTP. It is the loop for changing code and calling the API.
* The Compose stack runs the server behind nginx, as a deployment does. It is the way to try an osu! client (which needs
  HTTPS) or an IRC client (which needs TLS and a PROXY header).

### `dotnet run`

Clone the repository, start a PostgreSQL server and give Basil its connection string. The server stops at start with one
line when `ConnectionStrings__Write` is missing, because the shipped settings hold no secret and no database address:

```bash
git clone https://github.com/thnhmai06/osuBasil.git
cd osuBasil
docker run -d --name basil-pg -e POSTGRES_USER=basil -e POSTGRES_PASSWORD=basil -e POSTGRES_DB=basil \
  -p 5432:5432 postgres:17-alpine
export ConnectionStrings__Write="Host=localhost;Port=5432;Database=basil;Username=basil;Password=basil"
dotnet run --project Basil -- --console 0
```

The role in the connection string must exist; `POSTGRES_USER=basil` creates it. Database migrations run when the server
starts, and the server listens on `http://0.0.0.0:8080` (plain HTTP, the `Kestrel:Endpoints:Http:Url` setting).

On the first run Basil creates its `Data/` directory beside the build output (`Basil/bin/Debug/net10.0/Data/`), with
`Cache/`, `Logs/` and `Temp/` beside it. The server never creates an account by itself; make the first administrator with
the console, which runs the command against the storage without starting the server (the same
`ConnectionStrings__Write` must be set in that shell):

```bash
dotnet run --project Basil -- users create --username admin --password change-me --admin
```

The console commands are specified in [`console.md`](../for-technicians/console.md). Without `--console 0` the server shows
the terminal dashboard; when stdin or stdout is redirected it falls back to `--console 0` by itself.

### The Compose stack

From the repository root:

```bash
cp .env.example .env          # set POSTGRES_PASSWORD
mkdir -p docker-data/{Data,Cache,Logs}
chown -R 1654:1654 docker-data/Data docker-data/Cache docker-data/Logs
docker compose up --build -d
```

This builds the image from your checkout and starts nginx, Basil and PostgreSQL. nginx publishes `80`, `443` and `6697`;
Basil publishes no port. [`docker.md`](../for-technicians/docker.md) describes the stack and
[`deployment.md`](../for-technicians/deployment.md) the steps after it starts (the calculation stack, the first account).

## Development configuration

Basil uses the same configuration model in development and production. There is no separate development-only file.
The files live in `Data/` beside the executable (in a checkout, `Basil/Data/` is copied to `Defaults/Data/` in the
build output, and from there into `Data/` at start where a file is missing):

```text
Data/appsettings.json
Data/appsettings.{Environment}.json   optional overlay, selected by ASPNETCORE_ENVIRONMENT
```

A setting can also come from an environment variable (`Basil__Server__Domain`, a double underscore for `:`) or be
overridden for one run on the command line of the server, for example `--Kestrel:Endpoints:Http:Url=http://127.0.0.1:8081`.
A console command given on the command line does not take such options; for those, use environment variables or put the
settings in an `appsettings.{Environment}.json` overlay and set `ASPNETCORE_ENVIRONMENT` to its name. Do not commit an
overlay that holds a local connection string. Port `0` picks a free port.

The shipped domain is `basil.local`, which is what the osu! client and the sample names in these documents use. To
develop against another domain, set `Basil__Server__Domain`. See
[`configuration.md`](../for-technicians/configuration.md) for the complete reference.

### IRC

Basil's IRC listener (`Basil:Server:Irc:Port`, default `6667`) closes every connection that does not open with a PROXY
protocol header from a trusted proxy (loopback by default), so a plain IRC client cannot connect to `dotnet run` directly.
Use the Compose stack and connect to `irc.basil.local` on `6697` with TLS (see
[`irc-client.md`](../for-client/irc-client.md)), or write the header yourself from a script:

```bash
{ printf 'PROXY TCP4 127.0.0.1 127.0.0.1 50000 6667\r\n'; cat; } | nc 127.0.0.1 6667
```

Authenticate with your account password, sent as the connection password or through SASL `PLAIN`. See
[`irc.md`](irc.md).

### Resetting the development database

The server has not been released, so each module's baseline migration is edited in place instead of adding a new
migration. A development database that already applied an older baseline fails to migrate: drop it (and let the server
create it again) after pulling a change that touches a migration.

## Trying the running server

Start the server (`--console 0`), create an account as shown above, and call it with a `Host` header for the sub-domain you
want; the sub-domains are listed in [`configuration.md`](../for-technicians/configuration.md):

```bash
TOKEN=$(curl -s -H "Host: api.basil.local" -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"change-me"}' http://127.0.0.1:8080/v1/auth/login \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["data"]["token"])')
curl -s -H "Host: api.basil.local" -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8080/v1/channels
```

Run the real executable at least once before calling a change to the hosts, the shutdown or the configuration done,
because in-process hosting in tests does not exercise the `Program` entry point: log in, chat over IRC, send `SIGTERM`
and check that the exit code is `0`.

## Running an osu! client against a development server

Running the actual osu! client against a local Basil instance requires the same two things as any other Basil
deployment:

1. A trusted TLS certificate covering the required hostnames.
2. Hosts entries mapping those hostnames to your development machine.

The osu! client does not distinguish between a development and production server. From the client's perspective, both
must satisfy the same HTTPS and hostname requirements, and Basil itself serves no HTTPS: run the Compose stack, whose nginx
ends TLS on `443`.

### Local certificate

The repository includes a certificate for local development in `docker/nginx/certs/` (`basil.local.crt` and
`basil.local.key`), which the Compose stack mounts into nginx. It covers `basil.local`, `*.basil.local` and
`irc.basil.local`, so the default local configuration can be used without generating another certificate. Trust
`basil.local.crt` on the machine that runs the client; [`getting-started.md`](../for-client/osu-stable/getting-started.md)
shows how.

For a different domain, generate an appropriate certificate using the commands in
[`https.md`](../for-technicians/https.md) and mount it over the two files nginx reads.

> [!IMPORTANT]
> The bundled certificate exists only as a development convenience: its private key is in the repository. It is suitable
> for local development and private LAN testing, but must not be used for a production or publicly exposed server. See
> [`https.md`](../for-technicians/https.md).

### Hosts entries

Add the required hostnames to your system's hosts file and then launch the osu! client.

The client-side setup is documented in [`getting-started.md`](../for-client/osu-stable/getting-started.md). The same client
configuration applies whether Basil is running from a development checkout or from a deployed image.

### Client IP handling

`dotnet run` normally runs without a reverse proxy, and the client address is the address of the connection. The
`X-Real-IP` header, the country header and `X-Forwarded-Proto` are honoured only on requests from an address listed in
`Basil:Server:TrustedProxies`, which defaults to loopback; see [`configuration.md`](../for-technicians/configuration.md).

## Run tests

Basil has no test projects at present: the repository holds no test code and `Basil.slnx` lists none, so there is nothing
to run yet. The test tree is meant to mirror the project folders at the repository root: one test project per source
project, `tests/{Application,Infrastructure}/<Module>` for the modules, `tests/Domain/`, `tests/Protocol/`,
`tests/Hosts/<Host>` for the hosts, `tests/Architecture/` (the project graph, the host graph and the container check) and
`tests/Support/` for the shared fixtures and simulated clients. See [`testing.md`](testing.md) for the layers.

When test projects exist, run the project you changed first, one at a time, and run every test project listed in
`Basil.slnx` before opening a pull request: do not pass the solution to `dotnet test`, because the projects that start a
PostgreSQL container do not run reliably together.

Database-backed test projects should create a temporary PostgreSQL database for each test class and remove it
afterward. They use the server named by the `BASIL_TEST_POSTGRES` connection string when it is set, otherwise a
Testcontainers container (Docker; set `DOCKER_HOST` when the daemon is not on the default socket).

See [`testing.md`](testing.md) for test-writing conventions and [`AGENTS.md`](../../AGENTS.md) for the repository's
development workflow.

## Build the container image

Basil ships only as a container image. The `Dockerfile` publishes the server self-contained for `linux-x64` or
`linux-arm64` and adds `ffmpeg`; `docker compose up --build` uses it. To build the image alone:

```bash
docker build -t basil:dev .
```

`dotnet publish Basil -c Release -r linux-x64 --self-contained true -o publish` makes the same executable without the
image. It does not need the .NET runtime on the machine that runs it, but `ffmpeg` remains an external dependency for
beatmapset audio previews, and Basil still expects an nginx in front of it for HTTPS and IRC.

## Before changing the code

Basil is organized into several layers with deliberately defined responsibilities. Before making a non-trivial change,
read:

* [`architecture.md`](architecture.md): how the codebase is structured and how its layers interact
* [`testing.md`](testing.md): the test layers and how new tests should be written
* [`docs-guideline.md`](docs-guideline.md): conventions for documentation under `docs/`
* [`working-scopes.md`](working-scopes.md): what is intentionally in scope and what has been excluded
