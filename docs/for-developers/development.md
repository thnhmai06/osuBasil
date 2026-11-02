# Development

This guide covers developing Basil from source. It is intended for contributors who need to build, run, test, or modify
the server locally.

## Prerequisites

* The [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet).
* A PostgreSQL server. Basil keeps its records in PostgreSQL, so a development instance needs one; the simplest is a
  container (see [Clone and run](#clone-and-run)). Docker is also what the Infrastructure and host tests use.
* [FFmpeg](https://ffmpeg.org), on `PATH`, for beatmapset audio previews. See
  [`deployment.md`](../for-technicians/deployment.md).

## Clone and run

Clone the repository, start a PostgreSQL server and point Basil at it:

```bash
git clone https://github.com/thnhmai06/osuBasil.git
cd osuBasil
docker run -d --name basil-pg -e POSTGRES_PASSWORD=basil -e POSTGRES_DB=basil -p 5432:5432 postgres:17-alpine
dotnet run --project src/Hosts/Host -- --nogui
```

The default connection string in
[`appsettings.json`](../../src/Hosts/Host/Configurations/appsettings.json) is
`Host=localhost;Port=5432;Database=basil;Username=basil;Password=basil`; create that role, or override the
setting as described below. Database migrations run when the server starts.

On the first run Basil creates its `Data/` directory next to the build output
(`src/Hosts/Host/bin/Debug/net10.0/Data/`). The server never creates an account by itself; make the first
administrator with the console, which runs the command against the storage without starting the server:

```bash
dotnet run --project src/Hosts/Host -- users create --username admin --password change-me --admin
```

The console commands are specified in [`console.md`](../for-technicians/console.md). Without `--nogui` the server shows
the terminal dashboard; when stdin or stdout is redirected it falls back to `--nogui` by itself.

## Development configuration

Basil uses the same configuration model in development and production. There is no separate development-only file.
The files live in `Configurations/` beside the executable (in a checkout, `src/Hosts/Host/Configurations/` is copied to
the build output):

```text
Configurations/appsettings.json
Configurations/appsettings.{Environment}.json   optional overlay, selected by ASPNETCORE_ENVIRONMENT
```

A setting can also be overridden for one run on the command line of the server, for example
`--Basil:Server:Ports:Https=8443`. A console command given on the command line does not take such options; for those,
put the settings in an `appsettings.{Environment}.json` overlay and set `ASPNETCORE_ENVIRONMENT` to its name. Do not
commit an overlay that holds a local connection string.

The defaults bind port `443` for HTTPS and `6667` for IRC. On a development machine without the privilege to bind `443`,
pick another port with `--Basil:Server:Ports:Https=8443`; `0` picks a free port. An empty `Server:Certificate:Path`
serves plain HTTP, which is enough for the API and the image hosts when you address them with a `Host` header.

For local osu! client testing, set the domain:

```json
{
  "Basil": {
    "Server": {
      "Domain": "basil.local"
    }
  }
}
```

Replace `basil.local` with the local domain you intend to use. See
[`configuration.md`](../for-technicians/configuration.md) for the complete reference.

### IRC

The IRC gateway listens on port `6667` by default (`Basil:Server:Ports:Irc`). For example, using an IRC client:

```text
/server basil.local 6667
```

Authenticate with an IRC key: create one for your account with `POST /v1/users/{id}/sessions/irc` and send it as the
connection password or through SASL `PLAIN`. See [`irc.md`](irc.md).

### Resetting the development database

The server has not been deployed yet, so each module's `InitialCreate` migration is edited in place instead of adding
a new migration. A development database that already applied an older `InitialCreate` fails to migrate: drop it
(and let the server create it again) after pulling a change that touches a migration.

## Trying the running server

Start the server (`--nogui`), create an account as shown above, and call it with a `Host` header for the sub-domain you
want; the sub-domains are listed in [`configuration.md`](../for-technicians/configuration.md):

```bash
TOKEN=$(curl -s -H "Host: api.basil.local" -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"change-me"}' http://127.0.0.1:8443/v1/auth/login \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["data"]["token"])')
curl -s -H "Host: api.basil.local" -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8443/v1/channels
```

(This assumes `--Basil:Server:Ports:Https=8443` and an empty certificate path.) The tests start the server inside the
test process and never run the `Program` entry point, so run the real executable at least once before calling a change
to the hosts, the shutdown or the configuration done: log in, chat over IRC, send `SIGTERM` and check that the exit code
is `0`.

## Running an osu! client against a development server

Running the actual osu! client against a local Basil instance requires the same two things as any other Basil
deployment:

1. A trusted TLS certificate covering the required hostnames.
2. Hosts entries mapping those hostnames to your development machine.

The osu! client does not distinguish between a development and production server. From the client's perspective, both
must satisfy the same HTTPS and hostname requirements.

### Local certificate

The repository includes a development certificate (`data/certs/basil.local.pfx`, copied to
`Configurations/basil.local.pfx` in the build output) with the password:

```text
basil
```

The certificate covers the required `basil.local` subdomains, so the default local configuration can be used without
generating another certificate.

For a different domain, generate an appropriate certificate using the commands in
[`https.md`](../for-technicians/https.md), then configure:

```text
Basil:Server:Certificate:Path
Basil:Server:Certificate:Password
```

> [!IMPORTANT]
> The bundled certificate and password exist only as development conveniences. They are suitable for local development
and private LAN testing, but must not be used for a production or publicly exposed server. See
[`https.md`](../for-technicians/https.md).

### Hosts entries

Add the required hostnames to your system's hosts file and then launch the osu! client.

The client-side setup is documented in [`getting-started.md`](../for-client/bancho/getting-started.md). The same client
configuration applies whether Basil is running from a development checkout or from a deployed release.

### Client IP handling

A local development server normally runs without a reverse proxy, and the client address is the address of the
connection. Forwarding headers (`X-Forwarded-For`, `X-Real-IP`) are honoured only on requests from an address listed in
`Server:TrustedProxies`, which defaults to loopback; see [`configuration.md`](../for-technicians/configuration.md).

## Run tests

The test tree mirrors `src/`: one test project per source project, `tests/{Application,Infrastructure}/<Module>` for
the modules, `tests/Domain/`, `tests/Protocol/`, `tests/Bot/`, `tests/Hosts/<Host>` for the hosts, `tests/Architecture/`
(the project graph, the host graph and the container check) and `tests/Support/` for the shared fixtures and simulated
clients. Run the project you changed first:

```bash
dotnet test tests/Domain
dotnet test tests/Application/Users
dotnet test tests/Infrastructure/Users
dotnet test tests/Hosts/Rest
dotnet test tests/Architecture
```

Run every project listed in `Basil.slnx` before opening a pull request, one at a time: do not pass the solution to
`dotnet test`, because the projects that start a PostgreSQL container do not run reliably together.

The Infrastructure and host test projects create a temporary PostgreSQL database for each test class and remove it
afterward. They use the server named by the `BASIL_TEST_POSTGRES` connection string when it is set, otherwise a
Testcontainers container (Docker; set `DOCKER_HOST` when the daemon is not on the default socket).

See [`testing.md`](testing.md) for test-writing conventions and [`AGENTS.md`](../../AGENTS.md) for the repository's
development workflow.

## Build a standalone executable

You can produce the same kind of self-contained output used by the release pipeline:

```bash
dotnet publish src/Hosts/Host \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -o publish/win-x64
```

Change `-r` to the target runtime identifier, for example:

```text
win-x64
linux-x64
osx-arm64
```

and any other RID supported by the release matrix.

The resulting executable (`Basil.Host`) is self-contained and does not require the .NET runtime to be installed on the
target machine.

`ffmpeg` remains an external runtime dependency for beatmapset audio previews. See
[`deployment.md`](../for-technicians/deployment.md).

## Before changing the code

Basil is organized into several layers with deliberately defined responsibilities. Before making a non-trivial change,
read:

* [`architecture.md`](architecture.md): how the codebase is structured and how its layers interact
* [`testing.md`](testing.md): what the test suite guarantees and how new tests should be written
* [`docs-guideline.md`](docs-guideline.md): conventions for documentation under `docs/`
* [`working-scopes.md`](working-scopes.md): what is intentionally in scope and what has been excluded
