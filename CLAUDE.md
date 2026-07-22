# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Basil is a private osu! (stable) server for running multiplayer tournaments fully offline (no osu.ppy.sh/mirror dependency, no singleplayer ranking), built from [bancho.py](https://github.com/osuAkatsuki/bancho.py) (the osu! private server backend) with a redesigned schema, runtime-generated tournament match reports, and a scoped-down BanchoBot + chat/`!mp` command layer. It is not a full bancho.py port — pp calculation, clans, friends, and a general-purpose public v1/v2 API are intentionally out of scope; chat commands and the bot account are narrower than bancho.py's own set. Read [`docs/scope-decisions.md`](docs/scope-decisions.md) before assuming a bancho.py feature should exist here — a lot of it was cut on purpose, not left unfinished. [`README.md`](README.md) and [`docs/architecture.md`](docs/architecture.md) are the other primary references; this file complements them rather than repeating their content. The full HTTP API reference (osu! client protocol + Basil's own tournament API) and the BasilBot chat command wiki are no longer hand-written Markdown — they're generated OpenAPI documents rendered with Scalar, served at `api.<domain>/` on any running instance and published to GitHub Pages by CI (see `src/Basil.Web/docs-site/` and the `deploy-docs` job in `.github/workflows/ci.yml`).

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
dotnet run --project src/Basil.Web
```

No external services to stand up — the database is a single SQLite file (`basil.db`, default `Database:Path`) created next to the running process, and migrations run automatically on startup (`SqlMigrationRunner`, DbUp) — no manual migration step, no Docker.

Publishing a standalone executable (framework-dependent, needs the .NET 10 runtime on the target machine):

```bash
dotnet publish src/Basil.Web -c Release -r win-x64 --self-contained false -o publish/win-x64
dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained false -o publish/linux-x64
```

The published executable creates `basil.db` and 5 fixed storage folders (`Replays/`, `Avatars/`, `Mapsets/`, `Seasonals/`, `Faqs/`) next to itself on first run — see [`docs/run-deployment.md`](docs/run-deployment.md).

### Tests

Six test projects, run individually:

```bash
dotnet test tests/Basil.Domain.Tests
dotnet test tests/Basil.Protocol.Tests
dotnet test tests/Basil.Application.Tests
dotnet test tests/Basil.ArchitectureTests
dotnet test tests/Basil.IntegrationTests
dotnet test tests/Basil.Infrastructure.Tests   # no external service — runs against a temp SQLite file
```

Run a single test class or method with a filter:

```bash
dotnet test tests/Basil.Application.Tests --filter "FullyQualifiedName~MatchSessionRaceTests"
dotnet test tests/Basil.Application.Tests --filter "FullyQualifiedName=Basil.Application.Tests.Sessions.MatchSessionRaceTests.ConcurrentJoins_UnderLock_NeverDoubleAssignsASlot"
```

**Prefer running `Basil.Infrastructure.Tests` in the foreground, not backgrounded** — this project used to spin up a real MySQL via Testcontainers/Docker and got killed mid-run when backgrounded; it's now SQLite-only with no Docker dependency, but the caution is left here since it hasn't been specifically re-verified under a backgrounded run.

### CI

`.github/workflows/ci.yml`: `dotnet restore`/`build`/`test` across the whole solution, then publishes framework-dependent `win-x64`/`linux-x64` builds and uploads them as workflow artifacts. Mirror this locally before assuming a change is CI-clean.

## Architecture

Monolith Clean Architecture, five projects under `src/`, dependency direction enforced by `tests/Basil.ArchitectureTests` (NetArchTest) — a PR that violates it fails CI, not just review:

```
Basil.Domain           # no project references. Pure C#: enums, records, value calculators.
Basil.Protocol         # no project references. Bancho wire-format packet reading/writing.
Basil.Application      # → Domain, Protocol. Use cases, packet handlers, and *ports* (interfaces)
                         # describing what Infrastructure must provide.
Basil.Infrastructure    # → Application (implements its ports), Domain. SQLite/filesystem/
                         # osu!lazer-ruleset-library implementations.
Basil.Web               # → Application, Infrastructure, Protocol. ASP.NET Core host: subdomain
                         # routing, DI composition root.
```

Full walkthrough (dependency rule, login flow, multiplayer match-creation flow) is in [`docs/architecture.md`](docs/architecture.md) — read it before making cross-layer changes rather than re-deriving the flow from scratch.

Key structural facts worth knowing up front:

- **Routing is host-based, not path-based.** `Basil.Web/Routing/BanchoHostGroups.cs` mounts a different route group per subdomain (`c./ce./c4./c5./c6.` → bancho binary protocol, `osu.` → `/web/osu-*.php` HTTP endpoints, `b.` → legacy beatmap-thumbnail redirect to osu.ppy.sh's CDN, `a.` → local avatar files, `api.` → tournament match report (TRT) over GET/SSE, file downloads, and admin-key-gated management CRUD). Every route carries `.WithGroupName`/`.WithSummary`/`.WithDescription`/`.WithTags` metadata feeding 5 generated OpenAPI documents (one per host group, since OpenAPI can't hold two operations at the same path+method and several groups share literal templates like `GET /`) — see the OpenAPI/Scalar docs site (`api.<domain>/`, or GitHub Pages) for the full packet/endpoint reference instead of a hand-written Markdown file.
- **`Basil.Application` is organized by feature, not by kind**, under `PacketHandlers/{Core,Channels,Spectating,Multiplayer}/`, `Abstractions/{Beatmaps,Scores,Users,Channels,Social,Multiplayer}/` (the ports Infrastructure implements), `Sessions/{Channels,Multiplayer,Spectating}/` (in-memory runtime state, including `IMatchLiveEvents`/`IPlayerInputEvents` — the non-blocking C#-event pub/sub feeding the `api.` host's live SSE layer), `UseCases/{Authentication,Beatmaps,Multiplayer,Scores,Spectating,Mail,Anticheat,Bot}/`. The namespace matches the folder path — an import tells you exactly where a file lives.
- **`MatchSession.Lock` (a per-match `SemaphoreSlim(1,1)`) is the concurrency model for multiplayer state**, added because ASP.NET Core runs a real thread pool (unlike bancho.py's asyncio event loop, which the Python source implicitly relies on for atomicity between `await` points — there is no lock in the Python source to port). Any new handler or use case that reads-then-mutates a `MatchSession`'s slots must hold this lock across that whole read-mutate-broadcast sequence, matching every existing match packet handler. Don't hold it across an unrelated `await` (a DB call is fine; a long poll is not) — see the class's existing handlers for the pattern. Publishing to `IMatchLiveEvents`/`IPlayerInputEvents` while still holding the lock is fine — their `Publish*` methods just raise a C# event, and each SSE connection's own handler does a non-blocking `ChannelWriter.TryWrite` into its own buffer.
- **No pp calculation anywhere.** Star rating/difficulty (`PpyBeatmapDifficultyCalculator`, `Basil.Infrastructure/Performance/`) is computed locally by referencing ppy's own osu!lazer ruleset NuGet packages directly, for **display only** — nothing in scoring, leaderboards, or match win conditions depends on it. Don't reintroduce a pp dependency into gameplay-affecting logic. `!mp condition` has no pp option for the same reason.
- **Dapper + SQLite quirks to remember when touching `Basil.Infrastructure/Persistence/Repositories/`:** the connection string always carries `Foreign Keys=True` (SQLite disables FK enforcement per-connection by default) and `Default Timeout=5` (maps to `busy_timeout` — the server is deliberately multithreaded, see `MatchSession.Lock` below, so concurrent writers across different matches are expected and need to wait rather than throw `SQLITE_BUSY` immediately). Dapper can't materialize positional `record` types straight from a SQLite reader (column values come back as `Int64`/`string`, not the narrower `int`/`DateTime` a record's positional constructor expects) — every repository maps through a private mutable DTO class first (see any `Sqlite*Repository`'s `*Row`/`*RowDto` nested classes) instead of querying a public record type directly.
- **The schema is offline-pivot SQLite** (`Persistence/Migrations/001_base.sql`), PascalCase tables/columns, ids auto-incrementing from 1 (no bancho.py-style gaps). `Matches`/`Rounds`/`Scores` replace per-score online-play bookkeeping; `UserStats` is seeded once at zero and never updated by score submission (no singleplayer ranking exists). The tournament match report (TRT) is never persisted — built at read time from those three tables (or from the live `MatchSession` for an in-progress match) by `MatchReportService`. Full detail in [`docs/architecture.md`](docs/architecture.md).
- **Chat commands and the bot account use a *fresh* dispatch layer** (`ICommandDispatcher`/`CommandDispatcher`/`MpCommandService`), narrower than bancho.py's full set. The scrim engine (`MatchScoringService`) does not exist — don't build it without being asked. Which `!mp` subcommands exist versus are deliberately deferred (`!mp make`, `!mp timer`/`aborttimer`, all scrim/mappool commands) is listed in [`docs/scope-decisions.md`](docs/scope-decisions.md) — read it before assuming a bancho.py chat command exists here.
