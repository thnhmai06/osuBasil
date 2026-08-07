# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Basil is a private osu! (stable) server for running multiplayer tournaments fully offline (no osu.ppy.sh/mirror dependency, no singleplayer ranking), built from [bancho.py](https://github.com/osuAkatsuki/bancho.py) (the osu! private server backend) with a redesigned schema, runtime-generated tournament match reports, and a scoped-down BanchoBot + chat/`!mp` command layer. It is not a full bancho.py port — pp calculation, clans, friends, and a general-purpose public v1/v2 API are intentionally out of scope; chat commands and the bot account are narrower than bancho.py's own set. Read [`docs/working-scopes.md`](docs/working-scopes.md) before assuming a bancho.py feature should exist here — a lot of it was cut on purpose, not left unfinished. [`README.md`](README.md) and [`docs/architecture.md`](docs/architecture.md) are the other primary references; this file complements them rather than repeating their content. The full HTTP API reference (osu! client protocol + Basil's own tournament API) and the BasilBot chat command wiki are no longer hand-written Markdown — they're generated OpenAPI documents rendered with Scalar, served at `api.<domain>/docs/` on any running instance (root `/` redirects there) and published to GitHub Pages by CI (see `src/Basil.Web/docs-site/` and the `deploy-docs` job in `.github/workflows/ci.yml`).

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

### 5. API Documentation Style

**Every `.WithSummary`/`.WithDescription` on an `api.` host route (`src/Basil.Web/Routing/Api/`) describes the API contract, never the implementation.**

The Implementation Test: if the whole implementation were swapped out tomorrow (SQLite → Postgres, filesystem → S3, events → a queue) but the endpoint still behaved the same, would this sentence need to change? If no, keep it. If yes, it's an implementation detail — cut it.

- **Summary**: one sentence, answers "what does this endpoint do?" (`Retrieve a match report.`, not `Gets data from SQLite...`). End with a period, matching `getMatchReport` in `MatchRoutes.cs` — the house style exemplar for both Summary and Description.
- **Description**: Markdown paragraphs, one idea per paragraph, in priority order: what it returns → special cases → related/live endpoint → errors that matter (not every status code). Prefer `"""` raw strings over `" + "` concatenation so paragraph breaks are visible in source.
- Never name: SQLite, Redis, middleware, DI, `Channel<T>`, background services, file paths, cache keys, or other internal service/type names. Say "the response is streamed using Server-Sent Events" (client-visible), never "uses `Channel<T>`" (internal). Say "deletion is processed asynchronously," never "deletes by renaming the folder."
- Never restate what the OpenAPI schema already shows: HTTP method+path, "returns JSON," a param already documented by its own schema entry, or the exact response shape when a `Produces<T>` already declares it.
- No history ("replaces the old endpoint", "previously...") unless the route exists purely for backward compatibility.

### 6. XML Documentation Style

**Every `///` doc comment describes responsibility and observable behavior, never implementation.** Same Implementation Test as rule 5 above (would this need to change if the whole implementation were swapped out but behavior stayed the same?), applied to every C# XML doc comment in the codebase, not just `api.` host routes.

- **Summary**: one sentence answering "what is this / what does it do?", not "how." Don't restate the member's own name (`class AvatarRoutes` needs `Registers avatar endpoints.`, not `Registers avatar routes under /avatars.`).
- **Remarks**: caller-visible details that don't fit in one sentence — still contract, never implementation ("clients receive the current state first, then incremental updates" is a remark; "backed by a `Channel<T>`" is not).
- Never name: temp files, renames, locks/mutexes, cache keys, dictionaries, switch statements, reflection, or a specific framework/library (ASP.NET Core, Dapper, Serilog, OpenAPI, Scalar, `Channel<T>`, `EventSource`, middleware, DI) unless that detail is itself the contract (`Returns a Server-Sent Events stream.` is contract; `Uses TypedResults.ServerSentEvents.` is not).
- Never explain *why* the code is written a certain way (nginx buffers, Dapper can't do X, OpenAPI requires Y) — that belongs in a regular `//` comment or ADR, not a doc comment.
- No history ("replaces", "formerly", "used to", "legacy") unless the member exists purely for backward compatibility.
- `<see cref>` only when it helps the reader (pointing at a public return/param type); not to reference an internal implementation collaborator.
- `<param>`/`<returns>` describe meaning, not how the value is produced (`The destination file.`, not `The file created after renaming the temp file.`; `Returns the resolved MIME type.`, not `Returns Provider.TryGetContentType(...).`).

### 7. Markdown Documentation Style (`docs/*.md`)

**XML docs (rule 6) answer "what does this API/class do?" `docs/*.md` answers "why is the system built this way, how does it work, and how do the pieces fit together?"** XML docs are reference material; `docs/*.md` is architecture and guides. Keep them separate — a reader who wants one shouldn't have to wade through the other.

- **Why → What → How, in that order.** Open with the problem being solved, then what Basil does about it, then the mechanics. Don't open with an implementation type (`Channel<T>`, `IAsyncEnumerable`).
- **One topic per file.** `authentication.md`, `sse.md`, `database.md`, not one `everything.md`. If a change touches more than 3 doc files, the docs are duplicating each other — merge them.
- **Overview before detail.** State the shape of the whole flow before the first packet field or column name.
- **Separate contract from current implementation.** A "Contract" section (what the client sends, what the server guarantees) should stay true even if the implementation behind it is rewritten; put class/method names and mechanics in their own "Design"/"Implementation" section instead of interleaving them.
- **Include a "Design rationale" section** ("Why SSE instead of WebSockets?", "Why SQLite?") — this is the one thing Markdown does that an XML doc comment can't.
- **Prefer a diagram to a paragraph.** ASCII arrows or a Mermaid `sequenceDiagram` for any multi-step flow; a picture is worth the 500 words it replaces.
- **State system invariants explicitly** ("A `MatchSession` always owns exactly one `SnapshotChannel`.", "Every live route ends in `/live`.") — these are exactly the facts a contributor needs and won't find by reading one file.
- **Don't paste real code.** A short illustrative snippet is fine; a 200-line dump belongs in the source file, linked instead.
- **Always include an example** — a request, a JSON body, a packet layout, a short timeline — concrete beats abstract.
- **Each section answers one question.** A reader scanning headings should be able to guess the content before opening it.
- **Link instead of repeating.** A "See also" list pointing at related docs beats re-explaining what they already cover.
- **Add a "Related code" list** (file paths) so a reader can jump straight to the source instead of guessing.
- **Don't re-document the HTTP API.** OpenAPI/Scalar already lists routes and status codes; `docs/*.md` explains why the architecture looks the way it does, not `GET`/`POST` lists.

## Commands

```bash
dotnet restore
dotnet build --configuration Release
```

### Running locally

```bash
dotnet run --project src/Basil.Web
```

No external services to stand up — the database is a single SQLite file (`Data/Basil.db`, fixed, not configurable) created next to the running process, and migrations run automatically on startup (`SqlMigrationRunner`, DbUp) — no manual migration step. Docker is optional, not required: `Dockerfile`/`docker-compose.yml` at the repo root give a self-contained `linux-x64` image with `ffmpeg` preinstalled (see [`docs/run-deployment.md`](docs/run-deployment.md#docker-alternative-to-the-manual-publish-below)); the manual `dotnet publish` path below works identically without it, just requires `ffmpeg` on `PATH` for audio previews.

Publishing a standalone executable (framework-dependent, needs the .NET 10 runtime on the target machine):

```bash
dotnet publish src/Basil.Web -c Release -r win-x64 --self-contained false -o publish/win-x64
dotnet publish src/Basil.Web -c Release -r linux-x64 --self-contained false -o publish/linux-x64
```

The published executable creates a `Data/` folder next to itself on first run — the database plus 5 fixed storage folders (`Replays/`, `Avatars/`, `Mapsets/`, `Seasonals/`, `Faqs/`) — see [`docs/run-deployment.md`](docs/run-deployment.md).

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

`.github/workflows/ci.yml`: `dotnet restore`/`build`/`test` across the whole solution (runs on every push/PR/manual dispatch), then a `deploy-docs` job rebuilds `src/Basil.Web` alone to regenerate the 5 OpenAPI documents and publishes the Scalar docs site to GitHub Pages (push/dispatch only). Mirror the build/test locally before assuming a change is CI-clean. Executable publishing is separate: `.github/workflows/release.yml` builds self-contained `win-x64`/`linux-x64` binaries and attaches them to the GitHub Release only when one is published — not run on every push.

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

- **Routing is host-based, not path-based.** `Basil.Web/Routing/Bancho/BanchoHostGroups.cs` maps every subdomain to its route group and holds two shared helpers (beatmapset archive/audio-preview building) reused by both the `b.` and `api.` hosts; the route registrations themselves live one file per host group: `BanchoProtocolRoutes.cs` (`c./ce./c4./c5./c6.` → bancho binary protocol), `OsuWebRoutes.cs` (`osu.` → `/web/osu-*.php` HTTP endpoints), `BeatmapAssetRoutes.cs` (`b.` → beatmap thumbnail/audio-preview requests, resized/trimmed on demand from local storage and cached, see the `b.` host bullet below), `AvatarRoutes.cs` (`a.` → local avatar files), and `Routing/Api/ApiHostRoutes.cs` (`api.` → tournament match report (TRT), file downloads, and admin-key-gated management CRUD). Every route carries `.WithGroupName`/`.WithSummary`/`.WithDescription`/`.WithTags` metadata feeding 5 generated OpenAPI documents (one per host group, since OpenAPI can't hold two operations at the same path+method and several groups share literal templates like `GET /`) — see the OpenAPI/Scalar docs site (`api.<domain>/docs/`, or GitHub Pages) for the full packet/endpoint reference instead of a hand-written Markdown file. Each match sub-resource (`/hosts`, `/refs`, `/ban`, `/slots`, `/timer`, `/abort`, `/close`) carries its own granular tag rather than a shared `"Match Actions"` tag, and the `basilapi` document's Scalar sidebar is grouped by resource (all `Matches`-prefixed tags adjacent, then `Users`, then `Beatmapsets`, etc.) via the `x-tagGroups` OpenAPI vendor extension (`BasilApiTagGroups` in `Program.cs`) — SSE-vs-plain-JSON is never its own tag or group. **SSE is never content-negotiated on a shared path** — every live channel is a dedicated, always-SSE `.../live` sibling of its JSON base resource (`GET /matches/{id}/live`, `GET /matches/{id}/settings/live`, etc. — see [`docs/sse.md`](docs/sse.md)), so no route ever branches on the `Accept` header. **kick moved to `DELETE /matches/{id}/slots` and invite to `POST /matches/{id}/slots`** (both tagged `Match Slots`, no longer standalone `/kick`/`/invite` actions); `/ban` is unchanged.
- **`Basil.Application` is organized by feature, not by kind**, under `Packets/{Users,Channels,Spectating,Multiplayer}/`, `Abstractions/{Beatmaps,Scores,Users,Channels,Social,Multiplayer}/` (the ports Infrastructure implements), `Sessions/{Channels,Multiplayer,Spectating}/` (in-memory runtime state, including `IMatchLiveEvents`/`IPlayerInputEvents` — the non-blocking C#-event pub/sub feeding the `api.` host's live SSE layer), `Services/{Authentication,Beatmaps,Multiplayer,Scores,Spectating,Anticheat,Bot,Chat,Irc,Content,Users}/`. The namespace matches the folder path — an import tells you exactly where a file lives.
- **`MatchSession.Lock` (a per-match `SemaphoreSlim(1,1)`) is the concurrency model for multiplayer state**, added because ASP.NET Core runs a real thread pool (unlike bancho.py's asyncio event loop, which the Python source implicitly relies on for atomicity between `await` points — there is no lock in the Python source to port). Any new handler or use case that reads-then-mutates a `MatchSession`'s slots must hold this lock across that whole read-mutate-broadcast sequence, matching every existing match packet handler. Don't hold it across an unrelated `await` (a DB call is fine; a long poll is not) — see the class's existing handlers for the pattern. Publishing to `IMatchLiveEvents`/`IPlayerInputEvents` while still holding the lock is fine — their `Publish*` methods just raise a C# event, and each SSE connection's own handler does a non-blocking `ChannelWriter.TryWrite` into its own buffer.
- **Referee, host, and creator are three independent authority concepts on a `MatchSession`.** Referee (`_referees`, a `ConcurrentDictionary<int, byte>` keyed by persistent user id, same shape as `BannedIds`) grants `!mp` command authority and never depends on a live session — disconnecting, logging out, or leaving the room's slots leaves it untouched; only `!mp removeref`/the referee-removal API revokes it. Host (`MatchSession.HostId`) is unrelated: transient in-client settings control tied to being seated, transferred by `!mp host`/leaving. Creator (`MatchSession.CreatorId`, set once at room creation by `MatchMembershipService.CreateAsync`; stays `null` for a room created via `POST /matches` with no session behind it) is a third, permanent id — `MatchSession.IsReferee` ORs in `IsCreator`, so the creator holds full `!mp` authority for the room's whole lifetime regardless of referee-list membership, can never be kicked, banned, or removed as referee (`MatchControlService.RemoveOneRefereeAsync`/`SetRefereesAsync` guard this on both the chat command and the HTTP `/matches/{id}/refs` routes), and is the only one who can run `!mp addref`/`!mp removeref` from chat — those HTTP routes stay admin-key-only with no per-caller restriction, since there is no caller identity on that surface. A referee removed while not currently seated is also parted from the match's chat channel (`MatchControlService`'s `KickFromChatIfUnseated`), since losing referee status also loses their only standing in a match-room channel (see the JOIN-gate bullet in [`docs/irc.md`](docs/irc.md)).
- **No pp calculation anywhere.** Star rating/difficulty and per-mode hit-object counts (`IOsuCalculator`/`PpyOsuCalculator`, `Basil.Infrastructure/Performance/`) are computed locally by referencing ppy's own osu!lazer ruleset NuGet packages directly, for **display only** — nothing in scoring, leaderboards, or match win conditions depends on it. Don't reintroduce a pp dependency into gameplay-affecting logic. `!mp condition` has no pp option for the same reason. `BeatmapAnalysis.TotalLength`/`MaxCombo`/`Bpm` are computed the same way, from the same `GetPlayableBeatmap()` result `PpyOsuCalculator.Analyze` already builds for star rating — **never** read `BeatmapInfo.Length`/`.MaxCombo`/`.BPM` off the raw `.osu` decode in `BeatmapIngestionService`, those fields are always zero (the legacy/lazer decoder doesn't walk hit-objects/timing-points to populate them; only the playable beatmap does).
- **The `b.` host serves resized thumbnails and audio previews directly, computed on demand and cached** — not a redirect to `api.` host originals. `IImageResizer`/`ImageSharpResizer` (SixLabors.ImageSharp) resizes mapset backgrounds to 80×60/160×120 JPEGs; `IAudioPreviewExtractor`/`FFMpegAudioPreviewExtractor` (FFMpegCore, shells out to the `ffmpeg` binary) trims a 10s 128kbps mp3 clip from a beatmap's `AudioFile` starting at its `PreviewTime`. Both write through `IResponseCache`/`FileSystemResponseCache` (`Data/Cache/{endpoint}/{relativePath}`) so a repeat request doesn't re-resize/re-transcode. Running outside Docker (see below) requires `ffmpeg` on `PATH`; ImageSharp needs nothing extra since it's pure managed code.
- **Every `api.` host JSON response is enveloped and its OpenAPI schema matches, every user/beatmap reference is embedded, hot single-row lookups are cached.** `Basil.Web/Middleware/EnvelopeMiddleware` wraps every `basilapi`-group response (file downloads and every `.../live` route excluded — an SSE route is recognized by its literal `live` path segment via `LiveSseRoutes.IsSseRoute`, no route branches on `Accept` anymore) in `{success, code, message, data, meta, errors, timestamp}`; every prior `204 No Content` route now returns `200` with a real body instead of `data: null`. `Basil.Web/OpenApi/EnvelopeSchemaTransformer` rewrites the *declared* response schema the same way (bare `T` → `Envelope<T>`), so generated clients see the real shape, not just the runtime body. Every user reference is a `UserBrief {id, name, country}` (`MatchLiveSnapshotBuilder.UserBrief`/`UserBriefResolver`, `Country` typed as the `Country` enum — see the enum-wire-convention bullet below), never a bare id; every beatmap reference embeds a `BeatmapView`-derived record (`BeatmapDetail`/`BeatmapInSet`, not the bare domain `Beatmap`), keyed by the stored `Md5` (never `Id` — an id can survive a re-ingestion that changes content, an md5 can't), `null` once that md5 no longer resolves. `Basil.Infrastructure/Caching/Caching{User,Map,Mapset}Repository` wrap the real Sqlite repositories behind an `IMemoryCache` (TTL + explicit invalidation on every write) so this embedding doesn't reintroduce an N+1 per response — see [`docs/response-envelope.md`](docs/response-envelope.md) for the full shape.
- **Enum wire convention on every `api.` host record: keep the real enum type on the C# record, never substitute `int`/`string`.** Every enum (`UserPrivileges`, `Mods`, `GameMode`, `MatchTeam`, `SlotStatus`, `Grade`, `RankedStatus`, `MatchEventType`, ...) serializes to a plain number (System.Text.Json's default — no `JsonStringEnumConverter`); `Country` is the sole exception, serialized to its 2-letter lowercase acronym via `CountryJsonConverter` (`Basil.Application/Json/`, registered in `Program.cs` and shared with the SSE pipeline's `BasilJsonOptions`). Every `TimeSpan` field (e.g. `Difficulty.TotalLength`) is likewise serialized globally via `TimeSpanSecondsJsonConverter` (same location, same registration) as a plain integer of seconds — both converters live in `Basil.Application` rather than `Basil.Domain` specifically so a Domain record can carry a `Country`/`TimeSpan` field without needing either converter type in scope (Domain has zero project references). A PATCH-only request record's nullable properties need an explicit `= null` default (not just `T?`) or ASP.NET Core's OpenAPI generator still marks them schema-required. **A session's `Country` (`UserSession.Country`, created at login/bootstrap) always comes from the stored user record — never from request headers.** There is no geolocation plumbing: `Basil.Domain/Login/Geolocation.cs` is a static helper holding only the proxy-header *IP* resolver (`Geolocation.PhraseIpAddress`, used for the `IngameLogins` IP and the `RemoteIp` log scope); country and lat/long are never derived from proxy headers anymore. To change what country a player (or BasilBot) shows in-game, update the `Users.Country` column / `Bot:Country` option and relogin.
- **`Priv`/"priv" is written out as `Privilege` everywhere** (`User.Privilege`, `PlayerSession.Privilege`/`BanchoPrivilege`, `Channel.ReadPrivilege`/`WritePrivilege`, the `Users.Privilege`/`Channels.ReadPrivilege`/`WritePrivilege` DB columns) — don't reintroduce the abbreviation in new code. `IsPrivate` is unrelated and untouched.
- **Dapper + SQLite quirks to remember when touching `Basil.Infrastructure/Persistence/Repositories/`:** the connection string always carries `Foreign Keys=True` (SQLite disables FK enforcement per-connection by default) and `Default Timeout=5` (maps to `busy_timeout` — the server is deliberately multithreaded, see `MatchSession.Lock` below, so concurrent writers across different matches are expected and need to wait rather than throw `SQLITE_BUSY` immediately). Dapper can't materialize positional `record` types straight from a SQLite reader (column values come back as `Int64`/`string`, not the narrower `int`/`DateTime` a record's positional constructor expects) — every repository maps through a private mutable DTO class first (see any `Sqlite*Repository`'s `*Row`/`*RowDto` nested classes) instead of querying a public record type directly.
- **The schema is offline-pivot SQLite** (`Persistence/Migrations/001_base.sql`), PascalCase tables/columns, ids auto-incrementing from 1 (no bancho.py-style gaps). `Matches`/`Rounds`/`Scores` replace per-score online-play bookkeeping; `UserStats` is seeded once at zero and never updated by score submission (no singleplayer ranking exists). The tournament match report (TRT) is never persisted — built at read time from those three tables (or from the live `MatchSession` for an in-progress match) by `MatchReportService`. Full detail in [`docs/database.md`](docs/database.md) and [`docs/multiplayer.md`](docs/multiplayer.md).
- **Logging is Serilog, configured entirely in code** (`Basil.Web/Program.cs`'s `ConfigureSerilog`) — no standard ASP.NET Core `appsettings.json` `Logging` section; the only configurable knob is `Basil:Logging:MinimumLevel` (default `"Information"`, affects stdout + the full file only — the errors file is always Error+). Console + two daily-rolling file sinks (`Logs/full/`, Information+; `Logs/errors/`, Error+), each with a hardlink pointer (`Logs/latest.log`/`errors_latest.log`) recreated on every roll via `HardLinkFileLifecycleHooks`/`Basil.Infrastructure/Logging/HardLink.cs`. Correlation scopes (`RequestId`, `MatchId`/`UserId`/`PacketType` from `PacketDispatcher`, IRC's own `ConnectionId`, `ScoreId`, `!mp`'s `Subcommand`) and fixed `Category` tags (`Mapsets`/`Matches`/`Scores`/`Online`/`IRC`/`Database`/`Cache`/`Host`/`Api` — the last covering `ApiRequestLoggingMiddleware`'s per-request access lines on the `api.` host, which skip its live SSE channels — via `CategoryEnricher` matching on `SourceContext`, falling back to `App` — which `ConfigureSerilog` demotes to Warning+ only, since it's unclassified framework/library noise, not a domain event) are documented in full in [`docs/logging.md`](docs/logging.md) — read it before adding a new scope, a new category rule, or wondering why a log line is missing one. Domain-event log lines that track a resource's lifecycle (mapset/beatmap ingestion, match join/leave, login/logout, round completion, ...) are prefixed `+`/`-`/`~` (created/removed/modified) by convention — match it in new log lines rather than inventing another marker. `ConfigureKestrel` wraps TLS cert loading in try/catch — a bad `CertPath`/`CertPassword` logs `Fatal` (path only, never the password) and calls `Environment.Exit(1)` instead of crashing unclearly through the generic host's own handling. Multiplayer events log once, in the shared `MatchMembershipService`/`MatchControlService` methods — never re-logged in the packet handler/`!mp` subcommand/HTTP route that calls into them.
- **Chat commands and the bot account use a *fresh* dispatch layer** (`ICommandDispatcher`/`CommandDispatcher`/`MpCommandService`), narrower than bancho.py's full set. The scrim engine (`MatchScoringService`) does not exist — don't build it without being asked. Which `!mp` subcommands exist (including `!mp make`, `!mp timer`/`aborttimer` — all implemented) versus are deliberately deferred (`!mp force`, the full mappool/scrim engine, personal commands like `!block`/`!changename`) is listed in [`docs/working-scopes.md`](docs/working-scopes.md) — read it before assuming a bancho.py chat command exists here.

## Agent skills

### Issue tracker

Issues live in GitHub Issues (thnhmai06/osuBasil), via `gh` CLI. See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: `CONTEXT.md` + `docs/adr/` at repo root (created lazily by `/domain-modeling`). See `docs/agents/domain.md`.
