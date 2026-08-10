# Development

This guide covers developing Basil from source. It is intended for contributors who need to build, run, test, or modify
the server locally.

## Prerequisites

Basil development requires the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet).

The SDK provides everything needed to build and run the ASP.NET Core web host. No separate .NET runtime or external
service is required for a normal development checkout.

Some functionality has an additional dependency:

* [FFmpeg](https://ffmpeg.org) is required for beatmapset audio previews. It must be available on `PATH`. See [
  `deployment.md`](../for-technicians/deployment.md).

## Clone and run

Clone the repository and start the web host:

```bash
git clone https://github.com/thnhmai06/osuBasil.git
cd osuBasil
dotnet run --project src/Basil.Web
```

On the first run, Basil creates its local `Data/` directory next to the build output:

```text
src/Basil.Web/bin/Debug/net10.0/Data/
```

Database migrations run automatically. A normal development instance therefore requires no Docker daemon, database
server, or other external service.

## Development configuration

Basil uses the same [`appsettings.json`](../../src/Basil.Web/appsettings.json) configuration model in development and production. There is no separate
development-only configuration file.

The main configuration file is:

```text
src/Basil.Web/appsettings.json
```

For local osu! client testing, set:

```json
{
  "Basil": {
    "Server": {
      "Domain": "basil.local"
    }
  }
}
```

Replace `basil.local` with the local domain you intend to use.

See [`configuration.md`](../for-technicians/configuration.md) for the complete configuration reference and the bundled
development certificate settings.

### IRC

The IRC gateway listens on port `6667` by default. The port can be changed with:

```text
Basil:Irc:Port
```

For example, using an IRC client:

```text
/server basil.local 6667
```

Authenticate using your Basil account password.

## Running an osu! client against a development server

Running the actual osu! client against a local Basil instance requires the same two things as any other Basil
deployment:

1. A trusted TLS certificate covering the required hostnames.
2. Hosts entries mapping those hostnames to your development machine.

The osu! client does not distinguish between a development and production server. From the client's perspective, both
must satisfy the same HTTPS and hostname requirements.

### Local certificate

The repository includes a development certificate:

```text
Data/basil.local.pfx
```

with the password:

```text
your-password
```

The certificate covers the required `basil.local` subdomains, so the default local configuration can be used without
generating another certificate.

For a different domain, generate an appropriate certificate using the commands in [
`https.md`](../for-technicians/https.md), then configure:

```text
Basil:Server:CertPath
Basil:Server:CertPassword
```

> [!IMPORTANT]
> The bundled certificate and password exist only as development conveniences. They are suitable for local development
and private LAN testing, but must not be used for a production or publicly exposed server. See [
`https.md`](../for-technicians/https.md).

### Hosts entries

Add the required hostnames to your system's hosts file and then launch the osu! client.

The client-side setup is documented in [`getting-started.md`](../for-client/bancho/getting-started.md). The same client
configuration applies whether Basil is running from a development checkout or from a deployed release.

### Client IP handling

A local development server normally runs without a reverse proxy.

When `X-Forwarded-For` and `X-Real-IP` are absent, Basil synthesizes them from the connection's remote address. This is
required because [`Basil.Domain.Login.Geolocation.PhraseIpAddress`](../../src/Basil.Domain/Login/Geolocation.cs) assumes these headers exist, matching the proxy-based
setup used by bancho.py in production.

You generally do not need to configure anything manually for local development.

## Run tests

Basil has six test projects. Run them individually:

```bash
dotnet test tests/Basil.Domain.Tests
dotnet test tests/Basil.Protocol.Tests
dotnet test tests/Basil.Application.Tests
dotnet test tests/Basil.ArchitectureTests
dotnet test tests/Basil.IntegrationTests
dotnet test tests/Basil.Infrastructure.Tests
```

`Basil.Infrastructure.Tests` creates a temporary SQLite database for each test class and removes it afterward. It does
not require Docker or an externally running database.

See [`testing.md`](testing.md) for test-writing conventions.

For repository-specific development workflow recommendations, including how to run `Basil.Infrastructure.Tests`, see [
`CLAUDE.md`](../../CLAUDE.md).

## Build a standalone executable

You can produce the same kind of self-contained output used by the release pipeline:

```bash
dotnet publish src/Basil.Web \
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

The resulting executable is self-contained and does not require the .NET runtime to be installed on the target machine.

`ffmpeg` remains an external runtime dependency for beatmapset audio previews. See [
`deployment.md`](../for-technicians/deployment.md).

## Before changing the code

Basil is organized into several layers with deliberately defined responsibilities. Before making a non-trivial change,
read:

* [`architecture.md`](architecture.md): how the codebase is structured and how its layers interact
* [`testing.md`](testing.md): what the test suite guarantees and how new tests should be written
* [`docs-guideline.md`](docs-guideline.md): conventions for documentation under `docs/`
* [`working-scopes.md`](working-scopes.md): what is intentionally in scope and what has been excluded
