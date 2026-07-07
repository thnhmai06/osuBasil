# Getting started

## Quickest path: full stack via Docker

```bash
docker compose up --build
```

`docker-compose.yml` builds the production image (multi-stage: a Rust build stage for the native difficulty-rating crate, then `dotnet publish`, then a slim ASP.NET Core runtime) and starts it alongside MySQL 8.0 and Redis 7.4. `SqlMigrationRunner` (`src/Bancho.Infrastructure/Persistence/SqlMigrationRunner.cs`) applies the embedded `base.sql` schema against MySQL automatically on startup via [DbUp](https://dbup.readthedocs.io/) — no manual migration step.

The app listens on `http://localhost:8080`. Relevant environment variables (see `docker-compose.yml`):

| Variable | Purpose |
| --- | --- |
| `BANCHO_DOMAIN` | the domain the server identifies itself as (used in menu icon links, etc.) |
| `BANCHO_MENU_ICON_URL` / `BANCHO_MENU_ONCLICK_URL` | the in-game main menu icon and its click-through URL |

Data persists in three named volumes (`bancho-net-mysql-data`, `bancho-net-redis-data`, `bancho-net-replays`).

## Local development: `dotnet run` against dockerized MySQL/Redis

For iterating on the app itself without rebuilding a container each time:

```bash
docker compose -f docker-compose.dev.yml up -d
```

This starts only MySQL and Redis, with ports published to the host (`3306`, `6379`) — separate from the Testcontainers-managed instances the test suite spins up on its own. Then:

```bash
dotnet run --project src/Bancho.Web
```

Configure the connection strings in `src/Bancho.Web/appsettings.Development.json` to point at `localhost:3306` / `localhost:6379` with the `bancho`/`bancho` credentials from `docker-compose.dev.yml`.

## Running the tests

```bash
dotnet test tests/Bancho.Domain.Tests
dotnet test tests/Bancho.Protocol.Tests
dotnet test tests/Bancho.Application.Tests
dotnet test tests/Bancho.ArchitectureTests
dotnet test tests/Bancho.IntegrationTests
dotnet test tests/Bancho.Infrastructure.Tests
```

> [!IMPORTANT]
> `Bancho.Infrastructure.Tests` spins up a real, disposable MySQL instance via [Testcontainers](https://testcontainers.com/) and needs a running Docker daemon. Run it in the foreground — backgrounding it has been observed to get the process killed before Testcontainers finishes tearing down.

## Pointing a real osu! client at a local instance

The stable osu! client only ever connects over **HTTPS (port 443)** to `c./ce./c4./c5./c6./osu./b./api.<your-domain>` — plain HTTP alone gets silently refused before it reaches Kestrel. This needs a bit of one-time setup:

1. Start MySQL/Redis (`docker-compose.dev.yml`) as above.
2. Generate a self-signed certificate whose SAN list covers the domain **and all 8 subdomains** (`bancho.local`, `c.bancho.local`, `ce.bancho.local`, `c4.bancho.local`, `c5.bancho.local`, `c6.bancho.local`, `osu.bancho.local`, `b.bancho.local`, `api.bancho.local`), e.g. via `New-SelfSignedCertificate -DnsName @(...)` in an **elevated** PowerShell, export to `.pfx`, and install it into `Cert:\LocalMachine\Root` — the client validates the exact subdomain it connects to, so a plain `CN=localhost` dev cert will fail.
3. Point `Kestrel:Certificates:Default:Path`/`Password` in `appsettings.Development.json` at that `.pfx` using an **absolute path** — `dotnet run` does not change the working directory to the project folder, so a relative path resolves incorrectly.
4. Run `dotnet run --project src/Bancho.Web --urls "http://*:80;https://*:443"` from an **elevated** terminal (binding ports 80/443 needs Admin on Windows).
5. Add the domain and all 8 subdomains to the Windows hosts file (`C:\Windows\System32\drivers\etc\hosts`), pointing at `127.0.0.1`.
6. Launch the client with `osu!.exe --debug -devserver <domain>` — `--debug` makes it write `Logs/runtime.log`, the most useful diagnostic when something doesn't connect.

There's no in-game registration endpoint yet, so create test accounts directly in MySQL: one row in `users` (with `priv=3`, and `pw_bcrypt` set to the bcrypt hash of the **hex-encoded MD5 digest** of the password — not the raw digest, matching bancho.py's own password scheme) plus one row per game mode (`0,1,2,3,4,5,6,8` — mode `7` doesn't exist) in `stats`.

Without an nginx reverse proxy in front locally, the server synthesizes `X-Forwarded-For`/`X-Real-IP` from the raw connection's remote address when neither header is present — `Bancho.Domain.ClientIpResolver.Resolve` otherwise throws, since it assumes (like bancho.py) that a proxy always sets these in production.
