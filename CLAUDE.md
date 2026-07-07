# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

bancho-net is a C#/.NET port of [bancho.py](https://github.com/osuAkatsuki/bancho.py) (the osu! private server backend), deliberately narrowed mid-project to serve **multiplayer matches and tournaments only**. It is not a full bancho.py port — chat commands, the bot account, pp calculation, clans, friends, and the public developer API are intentionally out of scope. Read [`docs/scope-decisions.md`](docs/scope-decisions.md) before assuming a bancho.py feature should exist here — a lot of it was cut on purpose, not left unfinished. [`README.md`](README.md), [`docs/architecture.md`](docs/architecture.md), and [`docs/api-reference.md`](docs/api-reference.md) are the other primary references; this file complements them rather than repeating their content.

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

The native difficulty-rating crate builds separately (P/Invoke, not a project reference):

```bash
cd native/bancho-pp-ffi && cargo build --release
```

### Running locally

```bash
docker compose -f docker-compose.dev.yml up -d   # MySQL 8 + Redis 7, ports published to host
dotnet run --project src/Bancho.Web
```

Full stack (app + MySQL + Redis) via the production Dockerfile:

```bash
docker compose up --build
```

Migrations run automatically on startup (`SqlMigrationRunner`, DbUp) — no manual migration step.

### Tests

Six test projects, run individually:

```bash
dotnet test tests/Bancho.Domain.Tests
dotnet test tests/Bancho.Protocol.Tests
dotnet test tests/Bancho.Application.Tests
dotnet test tests/Bancho.ArchitectureTests
dotnet test tests/Bancho.IntegrationTests
dotnet test tests/Bancho.Infrastructure.Tests   # needs Docker; Testcontainers spins up a real MySQL
```

Run a single test class or method with a filter:

```bash
dotnet test tests/Bancho.Application.Tests --filter "FullyQualifiedName~MatchSessionRaceTests"
dotnet test tests/Bancho.Application.Tests --filter "FullyQualifiedName=Bancho.Application.Tests.Sessions.MatchSessionRaceTests.ConcurrentJoins_UnderLock_NeverDoubleAssignsASlot"
```

**Run `Bancho.Infrastructure.Tests` in the foreground, not backgrounded** — it has been observed to get killed mid-run when backgrounded, unrelated to Docker health.

### CI

`.github/workflows/ci.yml`: builds the native crate (cached by `Cargo.lock` hash), `dotnet restore`/`build`/`test` across the whole solution, then `docker build` of the production image. Mirror this locally before assuming a change is CI-clean.

## Architecture

Monolith Clean Architecture, five projects under `src/`, dependency direction enforced by `tests/Bancho.ArchitectureTests` (NetArchTest) — a PR that violates it fails CI, not just review:

```
Bancho.Domain           # no project references. Pure C#: enums, records, value calculators.
Bancho.Protocol         # no project references. Bancho wire-format packet reading/writing.
Bancho.Application      # → Domain, Protocol. Use cases, packet handlers, and *ports* (interfaces)
                         # describing what Infrastructure must provide.
Bancho.Infrastructure    # → Application (implements its ports), Domain. MySQL/Redis/filesystem/
                         # Rust-FFI implementations.
Bancho.Web               # → Application, Infrastructure, Protocol. ASP.NET Core host: subdomain
                         # routing, DI composition root.
```

Full walkthrough (dependency rule, login flow, multiplayer match-creation flow) is in [`docs/architecture.md`](docs/architecture.md) — read it before making cross-layer changes rather than re-deriving the flow from scratch.

Key structural facts worth knowing up front:

- **Routing is host-based, not path-based.** `Bancho.Web/Routing/BanchoHostGroups.cs` mounts a different route group per subdomain (`c./ce./c4./c5./c6.` → bancho binary protocol, `osu.` → `/web/osu-*.php` HTTP endpoints, `b.` → beatmap asset redirects, `api.` → placeholder only, real API deferred). See [`docs/api-reference.md`](docs/api-reference.md) for the full endpoint/packet tables and exactly where the hosting domain is configured.
- **`Bancho.Application` is organized by feature, not by kind**, under `PacketHandlers/{Core,Channels,Spectating,Multiplayer}/`, `Abstractions/{Beatmaps,Scores,Users,Channels,Social}/` (the ports Infrastructure implements), `Sessions/{Channels,Multiplayer}/` (in-memory runtime state), `UseCases/{Authentication,Beatmaps,Multiplayer,Scores,Spectating,Mail,Anticheat}/`. The namespace matches the folder path — an import tells you exactly where a file lives.
- **`MatchSession.Lock` (a per-match `SemaphoreSlim(1,1)`) is the concurrency model for multiplayer state**, added because ASP.NET Core runs a real thread pool (unlike bancho.py's asyncio event loop, which the Python source implicitly relies on for atomicity between `await` points — there is no lock in the Python source to port). Any new handler or use case that reads-then-mutates a `MatchSession`'s slots must hold this lock across that whole read-mutate-broadcast sequence, matching every existing match packet handler. Don't hold it across an unrelated `await` (a DB call is fine; a long poll is not) — see the class's existing handlers for the pattern.
- **No pp calculation anywhere.** Star rating/difficulty is computed locally via a vendored Rust crate (`native/bancho-pp-ffi`, forked from `akatsuki-pp-rs`) loaded through P/Invoke, for **display only** — nothing in scoring, leaderboards, or match win conditions depends on it. Don't reintroduce a pp dependency into gameplay-affecting logic.
- **Dapper + MySqlConnector quirks to remember when touching `Bancho.Infrastructure/Persistence/Repositories/`:** connection strings need `;TreatTinyAsBoolean=false` (bancho.py's schema uses `tinyint(1)` for non-boolean columns like `clan_priv`), and `api_key` (schema type `char(36)`) needs an explicit `CAST(api_key AS CHAR(36))` in SQL or MySqlConnector infers `Guid` instead of `string`.
- **Chat commands, the bot account, and the scrim engine were built, then deleted wholesale** by explicit user decision (not abandoned mid-build) — don't resurrect any of it (`ICommand`, `MpCommandDispatcher`, `MatchScoringService`, `BanchoBot` session bootstrap, etc.) without being asked; the packet-level multiplayer protocol underneath it all is intact and unaffected. Full rationale in [`docs/scope-decisions.md`](docs/scope-decisions.md).
