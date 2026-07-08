# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Basil is a private osu! (stable) server for running multiplayer tournaments over LAN, built from [bancho.py](https://github.com/osuAkatsuki/bancho.py) (the osu! private server backend) and deliberately narrowed mid-project to serve **multiplayer matches and tournaments only**, then later re-pivoted to run fully offline (no osu.ppy.sh/mirror dependency, no singleplayer ranking) with a redesigned schema, runtime-generated tournament match reports, and a scoped-down BanchoBot + chat/`!mp` command layer. It is not a full bancho.py port — pp calculation, clans, friends, and a general-purpose public v1/v2 API are intentionally out of scope; chat commands and the bot account were cut, then later re-added narrower than bancho.py's own set. Read [`docs/scope-decisions.md`](docs/scope-decisions.md) before assuming a bancho.py feature should exist here — a lot of it was cut on purpose, not left unfinished. [`README.md`](README.md), [`docs/architecture.md`](docs/architecture.md), [`docs/bot-commands.md`](docs/bot-commands.md) (BasilBot chat command wiki), [`docs/api-client.md`](docs/api-client.md) (bancho packets + osu-web endpoints), and [`docs/api-external.md`](docs/api-external.md) (the `api.` host exposed to external tournament tooling) are the other primary references; this file complements them rather than repeating their content.

## Rules
### 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

### 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

### 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## Commands

```bash
dotnet restore
dotnet build --configuration Release
```

### Running locally

```bash
docker compose -f docker-compose.dev.yml up -d   # MySQL 8 + Redis 7, ports published to host
dotnet run --project src/Basil.Web
```

Full stack (app + MySQL + Redis) via the production Dockerfile:

```bash
docker compose up --build
```

Migrations run automatically on startup (`SqlMigrationRunner`, DbUp) — no manual migration step.

### Tests

Six test projects, run individually:

```bash
dotnet test tests/Basil.Domain.Tests
dotnet test tests/Basil.Protocol.Tests
dotnet test tests/Basil.Application.Tests
dotnet test tests/Basil.ArchitectureTests
dotnet test tests/Basil.IntegrationTests
dotnet test tests/Basil.Infrastructure.Tests   # needs Docker; Testcontainers spins up a real MySQL
```

Run a single test class or method with a filter:

```bash
dotnet test tests/Basil.Application.Tests --filter "FullyQualifiedName~MatchSessionRaceTests"
dotnet test tests/Basil.Application.Tests --filter "FullyQualifiedName=Basil.Application.Tests.Sessions.MatchSessionRaceTests.ConcurrentJoins_UnderLock_NeverDoubleAssignsASlot"
```

**Run `Basil.Infrastructure.Tests` in the foreground, not backgrounded** — it has been observed to get killed mid-run when backgrounded, unrelated to Docker health.

### CI

`.github/workflows/ci.yml`: `dotnet restore`/`build`/`test` across the whole solution, then `docker build` of the production image. Mirror this locally before assuming a change is CI-clean.

## Architecture

Monolith Clean Architecture, five projects under `src/`, dependency direction enforced by `tests/Basil.ArchitectureTests` (NetArchTest) — a PR that violates it fails CI, not just review:

```
Basil.Domain           # no project references. Pure C#: enums, records, value calculators.
Basil.Protocol         # no project references. Bancho wire-format packet reading/writing.
Basil.Application      # → Domain, Protocol. Use cases, packet handlers, and *ports* (interfaces)
                         # describing what Infrastructure must provide.
Basil.Infrastructure    # → Application (implements its ports), Domain. MySQL/Redis/filesystem/
                         # osu!lazer-ruleset-library implementations.
Basil.Web               # → Application, Infrastructure, Protocol. ASP.NET Core host: subdomain
                         # routing, DI composition root.
```

Full walkthrough (dependency rule, login flow, multiplayer match-creation flow) is in [`docs/architecture.md`](docs/architecture.md) — read it before making cross-layer changes rather than re-deriving the flow from scratch.

Key structural facts worth knowing up front:

- **Routing is host-based, not path-based.** `Basil.Web/Routing/BanchoHostGroups.cs` mounts a different route group per subdomain (`c./ce./c4./c5./c6.` → bancho binary protocol, `osu.` → `/web/osu-*.php` HTTP endpoints, `b.` → legacy beatmap-thumbnail redirect to osu.ppy.sh's CDN, `a.` → local avatar files, `api.` → tournament match report (TRT) over GET/WebSocket, file downloads, and admin-key-gated management CRUD). See [`docs/api-client.md`](docs/api-client.md) for the `c./ce./c4./c5./c6.`/`osu.`/`b.`/`a.` packet and endpoint tables plus exactly where the hosting domain is configured, and [`docs/api-external.md`](docs/api-external.md) for the `api.` host's TRT/download/management-CRUD tables.
- **`Basil.Application` is organized by feature, not by kind**, under `PacketHandlers/{Core,Channels,Spectating,Multiplayer}/`, `Abstractions/{Beatmaps,Scores,Users,Channels,Social,Multiplayer}/` (the ports Infrastructure implements), `Sessions/{Channels,Multiplayer}/` (in-memory runtime state, including `IMatchEventBus` — the non-blocking pub/sub feeding the `api.` host's live WebSocket layer), `UseCases/{Authentication,Beatmaps,Multiplayer,Scores,Spectating,Mail,Anticheat,Bot}/`. The namespace matches the folder path — an import tells you exactly where a file lives.
- **`MatchSession.Lock` (a per-match `SemaphoreSlim(1,1)`) is the concurrency model for multiplayer state**, added because ASP.NET Core runs a real thread pool (unlike bancho.py's asyncio event loop, which the Python source implicitly relies on for atomicity between `await` points — there is no lock in the Python source to port). Any new handler or use case that reads-then-mutates a `MatchSession`'s slots must hold this lock across that whole read-mutate-broadcast sequence, matching every existing match packet handler. Don't hold it across an unrelated `await` (a DB call is fine; a long poll is not) — see the class's existing handlers for the pattern. Publishing to `IMatchEventBus` while still holding the lock is fine — its `Publish*` methods are a non-blocking `ChannelWriter.TryWrite`.
- **No pp calculation anywhere.** Star rating/difficulty (`PpyBeatmapDifficultyCalculator`, `Basil.Infrastructure/Performance/`) is computed locally by referencing ppy's own osu!lazer ruleset NuGet packages directly, for **display only** — nothing in scoring, leaderboards, or match win conditions depends on it. Don't reintroduce a pp dependency into gameplay-affecting logic. `!mp condition` has no pp option for the same reason.
- **Dapper + MySqlConnector quirks to remember when touching `Basil.Infrastructure/Persistence/Repositories/`:** connection strings need `;TreatTinyAsBoolean=false` (bancho.py's schema uses `tinyint(1)` for non-boolean columns like `clan_priv`), and `ApiKey` (schema type `char(36)`) needs an explicit `CAST(ApiKey AS CHAR(36))` in SQL or MySqlConnector infers `Guid` instead of `string`.
- **The schema is offline-pivot MySQL**, PascalCase tables/columns, ids auto-incrementing from 1 (no bancho.py-style gaps). `Matches`/`Rounds`/`Scores` replace per-score online-play bookkeeping; `UserStats` is seeded once at zero and never updated by score submission (no singleplayer ranking exists). The tournament match report (TRT) is never persisted — built at read time from those three tables (or from the live `MatchSession` for an in-progress match) by `MatchReportService`. Full detail in [`docs/architecture.md`](docs/architecture.md).
- **Chat commands, the bot account, and the scrim engine were built, then deleted wholesale**, then **BanchoBot + a narrow subset of chat/`!mp` commands were re-added** for the offline pivot — as a *fresh* dispatch layer (`ICommandDispatcher`/`CommandDispatcher`/`MpCommandService`), explicitly not a resurrection of the deleted `ICommand`/`MpCommandDispatcher`/`MatchScoringService` classes. The scrim engine specifically is still dropped — don't resurrect it without being asked. Which `!mp` subcommands exist versus are still deliberately deferred (`!mp make`, `!mp timer`/`aborttimer`, all scrim/mappool commands) is listed in [`docs/scope-decisions.md`](docs/scope-decisions.md) — read it before assuming a bancho.py chat command exists here.
