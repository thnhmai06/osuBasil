# bancho-net

A C# / .NET port of [bancho.py](https://github.com/osuAkatsuki/bancho.py), the osu! private server implementation, scoped down to run **multiplayer matches and tournaments**.

> [!NOTE]
> This is not a drop-in replacement for bancho.py. Chat commands, the bot account, pp calculation, clans, friends, and the public developer API are intentionally out of scope right now — see [`docs/scope-decisions.md`](docs/scope-decisions.md) for what's kept, what's cut, and why.

## What this is

- A real osu! bancho server: login, channels, multiplayer rooms (create/join/ready/start/mods/teams/host transfer/tourney referee client support), spectating, score submission, and beatmap/leaderboard lookups over the same wire protocol the osu! stable client speaks.
- Rebuilt from bancho.py's Python source following Monolith Clean Architecture, with the whole thing covered by tests (unit, architecture-boundary, and Testcontainers-backed integration tests).
- Fully offline-capable: no dependency on osu!api, a mirror service, or pp calculation. Star rating is computed locally via a small Rust FFI crate ([`native/bancho-pp-ffi`](native/bancho-pp-ffi)) for display only.

## What this isn't (yet)

- No chat command system (`!help`, `!mp`, etc.) and no bot account — deferred, not deleted from history.
- No public JSON API (`/api/v1`, `/api/v2`) — that's for external tooling (a website, a bracket tracker), not the game client, and is deferred indefinitely.
- No friends, favourites, ratings, comments, screenshots, or in-game account registration — these routes exist and respond, but do nothing (see the endpoint table in [`docs/api-reference.md`](docs/api-reference.md)).

## Tech stack

| Layer | Choice |
| --- | --- |
| Runtime | .NET 10 |
| Database | MySQL 8, accessed via [Dapper](https://github.com/DapperLib/Dapper), schema managed by [DbUp](https://dbup.readthedocs.io/) |
| Cache / leaderboards | Redis 7, via [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) |
| Difficulty/star rating | A vendored Rust crate ([`akatsuki-pp-rs`](https://github.com/osuAkatsuki/akatsuki-pp-rs)) exposed as a C ABI and loaded via P/Invoke — display only, never used for scoring |
| Tests | xUnit, [NetArchTest](https://github.com/BenMorris/NetArchTest) for layer-boundary enforcement, [Testcontainers](https://testcontainers.com/) for real-MySQL integration tests |

## Quick start

```bash
docker compose up --build
```

This builds the app image (including the native crate) and starts the app alongside MySQL and Redis. Migrations run automatically on startup. See [`docs/getting-started.md`](docs/getting-started.md) for configuration, running tests, and pointing a real osu! client at a local instance.

## Project structure

Five projects under `src/`, following Monolith Clean Architecture — dependency direction is enforced by an automated test suite, not just convention:

```
Bancho.Domain          # framework-free types: enums, value objects, pure calculators
Bancho.Protocol        # bancho wire-format packet reading/writing
Bancho.Application     # use cases, packet handlers, ports (interfaces) to Infrastructure
Bancho.Infrastructure   # MySQL/Redis/filesystem implementations of Application's ports
Bancho.Web              # ASP.NET Core host: routing by subdomain, composition root
```

See [`docs/architecture.md`](docs/architecture.md) for the dependency rule, how a login request and a score submission actually flow through these layers, and where to look when adding something new.

## Running tests

```bash
dotnet test tests/Bancho.Domain.Tests
dotnet test tests/Bancho.Protocol.Tests
dotnet test tests/Bancho.Application.Tests
dotnet test tests/Bancho.ArchitectureTests
dotnet test tests/Bancho.IntegrationTests
dotnet test tests/Bancho.Infrastructure.Tests   # needs Docker; spins up a real MySQL via Testcontainers
```

## Further reading

- [`docs/architecture.md`](docs/architecture.md) — project layout, dependency rule, request flow walkthroughs
- [`docs/getting-started.md`](docs/getting-started.md) — local setup, docker-compose, running against a real osu! client
- [`docs/scope-decisions.md`](docs/scope-decisions.md) — what was cut from bancho.py's feature set, and why
- [`docs/api-reference.md`](docs/api-reference.md) — supported bancho packets and HTTP endpoints
