# Release 1.0: goals

## Why this page exists

Basil moves from `v1.0.0-alpha.1` to `v1.0.0`. This page holds the goals of that work: what the owner decided, the
release criteria, the roadmap and how far the work is. What each day of evaluation found is in a dated file next to
it (`2026-11-17.md`); how the work is done is in [`handoff.md`](handoff.md). Chat carries only the key points.

These files are working material and are not part of `docs/`.

## Decisions of the owner

These were confirmed in the interview of 2026-11-17 and bind the evaluation.

| # | Topic | Decision |
|---|---|---|
| D1 | Deployment contexts | A trusted LAN, a tournament over the Internet and an open community server are all a measure of 1.0. Security and abuse protection are judged at the level of the public Internet, and a first installation must stay quick without a real domain or certificate. |
| D2 | Frozen from 1.0 | The HTTP API `/v1` with its envelope and SSE; the BasilBot and IRC commands; the configuration keys, the console and the database schema. A breaking change to any of them needs 2.0. The default wording of replies is not frozen. |
| D3 | Alpha databases | Not supported. The migrations are squashed into one baseline per module at 1.0; the upgrade promise starts there. |
| D4 | Refusals | `Denied` keeps naming no privilege the caller lacks, but its reply says which role can do the action. Every other refusal states its reason and the next step. |
| D5 | Blockers | All four open areas block the release: admission control on every entry point, a load test, a run of the released binary with real clients, and the internal clean-up (options binding, the `Protocol.Stable` references of Infrastructure, test naming, the missing `!mp` tests). |
| D6 | Load test | It finds the ceiling, not only whether 1000 concurrent users pass, and records memory and CPU at each step. |
| D7 | Release path | Straight to `v1.0.0` from `develop`, with no intermediate tag. |
| D8 | Experience | BasilBot and IRC, the HTTP API and its generated documentation, installation and operation, and the Cli dashboard are all below the bar today. |
| D9 | Order of evaluation | From the client inwards: IRC, the HTTP API (Postman), the Cli (tmux or kwin-mcp), a real osu! client (kwin-mcp), the documentation (the generated API reference first, then `docs/`, kept simple but complete), and the codebase last. |
| D11 | osu! client behaviour | (2026-11-18) bancho.py is the reference for **every** feature the osu! (stable) client uses: its logic is proven against the real client, so Basil follows it (the packets it sends, in which order and when, and what it does with what the client sends), rewritten under Basil's own rules (AGENTS.md, architecture, outcomes). A difference from bancho.py on the osu! side is a finding unless Basil's scope deliberately leaves the feature out. |
| D12 | Text sent to clients | (2026-11-18) Every text a client receives (chat replies of BasilBot and IRC, error and status messages of the API, notifications, descriptions shown to clients such as the generated references and guides) is written or rewritten through the `humanizer` skill and the writing skills before it is accepted. This applies to the new and changed reply constants of the fixes (`MpReplies`, `IrcReplies`, API messages, `.WithSummary`/`.WithDescription`). |
| D13 | Callers are not the system | (2026-11-18) The chat system only carries messages and keeps no chat history; it is written in Application. IRC and the osu! client are two different ways into the chat system. The osu! client, the HTTP API, BasilBot and the like are only ways to call into the system; the system tells callers apart but belongs to none of them. Domain and Application know nothing of them: they are defined only in the Hosts. This overrides the parts of `AGENTS.md` that place transport-specific types in Domain and Application (plan P3). |
| D10 | Criteria | Written per client, after trying each one: the evaluation comes first and the criteria are drawn from what it shows. The interface is a category of its own. Basil also ships what a client needs to start (a usable Postman collection, a sample IRC configuration, a guide to point the osu! client at a server). |

## Evaluation days

* [`2026-11-17.md`](2026-11-17.md): IRC and BasilBot, the HTTP API, the Cli (part 1), the event system, chat latency,
  spectating, the osu! client (from the code).

## Release criteria

Drawn from the trials of 2026-11-17 and 2026-11-18 (D10). `v1.0.0` is tagged only when every line holds, checked on the
released Docker image (P2) with real clients, and every finding graded a blocker is closed. Each line is a check that
passes or fails.

**Every surface**

* Every answer a client reads passes the three questions: the server answered, the answer says what really happened,
  the user knows what to do next. Every client-facing text lives in its surface's reply class and went through the
  humanizer and writing skills (D12). A refusal names its reason and next step; `Denied` names the role that can act
  (D4). One situation has one wording on every surface.
* One number names a room on every surface, and a match has its own stable number (F-019).

**osu! (stable) client**

* Every flow behaves as bancho.py does with the same client (D11): sign-in, presence and stats, chat (public, private,
  `#multiplayer`, `#spectator`), lobby, room create/join/leave, slot moves and locks, ready, host transfer, beatmap and
  mods, start, loading, skip, live scores of every player on the scoreboard, fail, completion, results, spectating
  with live score.
* A player's own action reaches their own client in the answer to the request that carried it (F-056), and a
  message from another user arrives within one poll interval of the receiver.
* A real client plays a full multiplayer round (two clients, one round, results shown) with no client exception in
  `runtime.log`; a round whose players cannot load ends by itself; a room never stays without a host.
* An end-to-end conversation test per flow pins the packets taken from bancho.py (F-079).
* A normal client is never refused by the rate limit during a session of play (F-045).

**IRC and BasilBot**

* Irssi, WeeChat and Halloy connect, join, chat and run every BasilBot command with no error line from the server's
  answers to their own housekeeping commands (F-018).
* Every BasilBot command answers, and every privacy rule holds for a user with no role (F-006).
* A failed sign-in says why (F-007); a database outage never locks a key out (F-043).

**HTTP API and SSE**

* The OpenAPI document served by the server equals the checked-in one (checked in CI) and describes every route's
  body, answers and errors (F-021, F-022, F-067); the shipped Postman collection runs green against a fresh server.
* Every refusal follows the envelope contract; privileges and enums are named values in and out (F-025, F-082).
* The spectate stream has separate `replay` and `activity` events, allows spectating oneself and a player who is not
  playing, and stops only when the player goes offline.

**Operation: console, dashboard, Docker**

* `docker compose up` on a clean machine with only Docker gives a working server following the docs alone; `shutdown`
  stops it and `restart` restarts it (F-069).
* Every documented key, dialog and mode of the dashboard works as `console.md` says (F-049, F-050, F-071 to F-074); no
  enum number is shown or required (F-053); settings show effective values with units (F-046, F-047).
* The shipped logging writes lifecycle lines only, with a log per session (F-036, F-037).

**Performance (D5, D6)**

* The load test reports the ceiling (users, requests per second) with memory and CPU at each step, on PostgreSQL after
  P1; chat and room actions stay within the latencies above at the target load.

**Documentation**

* Every statement in `docs/` matches the code (no stale, out-of-scope or contradicting page); a newcomer of each
  audience can do the first task from the docs alone; the client starter material ships (Postman collection, IRC
  sample configuration, osu! client guide) (D10).

**Code and tests**

* Build with no warning except the documented, assessed suppressions; no known high-severity advisory unassessed
  (F-068); the whole suite green, run once at the end; the architecture tests green; no finding of F-078 to F-085
  left.

## Open questions

* `ci.yml` runs on pushes to `main` and `master` and on pull requests only; a push to `develop` runs nothing.
* `release.yml` lets the updater packaging fail (`continue-on-error`); whether the updater is part of the 1.0 promise.
* P2 and LAN play (D1 names a LAN context): the server answers multicast DNS for `basil.local` so that LAN players
  find it, but multicast does not cross Docker's bridge network; it needs `network_mode: host`, which Docker Desktop on
  Windows and macOS does not offer. Whether LAN play keeps mDNS (Linux host networking only) or tells players to use a
  hosts file or the server's IP.
* P2: whether the dashboard (`--console 1`) stays a supported way to operate a container (`attach`), or the
  container runs `--console 0` and operators use `docker compose exec basil ./Basil <command>` only.
* P2: whether contributors keep `dotnet run` on their machine (recommended) while only the release is Docker-only.
* `docs-guideline.md` prefers lists for enumerations, the owner's code principles prefer prose; which one governs the
  rebuilt `docs/`.

## Roadmap

Proposed on 2026-11-18, to be approved group by group; nothing is changed in the code before a group is approved.
The order puts the surfaces frozen at 1.0 (D2) and the base the others stand on first, and the documentation and the
final trial last. Each group ends with a Release build; the whole suite runs once at the end (standing order).

| Group | Content | Findings |
|---|---|---|
| G1 Foundations | P1 (PostgreSQL only, tuned, migrations squashed), P2 (Docker only), configuration layout under `Data/`, dependency advisories | F-052, F-068, F-069, F-070 |
| G1b The system and its callers | P3 (D13): only the User below the Hosts, Connections in the Hosts, callers only in the Hosts, BasilBot as a caller, rules and an architecture test | F-086 |
| G2 osu! client | Align every Stable flow with bancho.py (D11), the end-to-end conversation tests first as the net, then the fixes; the event queue and chat latency | F-033, F-039, F-040, F-044, F-045, F-054, F-056 to F-060, F-063, F-079 |
| G3 Texts and contracts | Every client text through reply classes and the humanizer (D12); IRC and BasilBot; HTTP API, OpenAPI, Postman collection; spectating split into `replay` and `activity` | F-006 to F-019, F-021 to F-032, F-041 to F-043, F-067, F-080, F-082 |
| G4 Operation | Console and dashboard, logging per session, settings | F-034 to F-038, F-046 to F-051, F-053, F-071 to F-077 |
| G5 Code clean-up | Handlers, swallowed failures, migration leftovers, empty test project | F-078, F-081, F-083 to F-085 |
| G6 Capacity | Admission control on every entry point, load test to the ceiling with memory and CPU (D5, D6) | — |
| G7 Documentation | Rebuild `docs/`, client starter material | F-001 to F-005, F-015, F-031, F-055, F-061 to F-067 |
| G8 Release | Whole suite once, released image with real clients (osu!, Irssi, WeeChat, Halloy, Postman), criteria above, tag `v1.0.0` | — |

Two plans
the owner added on 2026-11-18 are recorded here first; both change surfaces frozen at 1.0 (D2), so they land before
`v1.0.0`, together with the alpha database reset (D3).

### Plan P1: PostgreSQL only, through EF Core, tuned for PostgreSQL

Owner (2026-11-18): persistence becomes EF Core on PostgreSQL only; MySQL is no longer supported; the database is
optimised for PostgreSQL.

Today: an engine abstraction `IDatabaseProvider` (`Basil.Infrastructure.Shared/Persistence/DatabaseProvider.cs`) with
`PostgresDatabaseProvider` and `MySqlDatabaseProvider` (`Basil.Infrastructure.Persistence/Providers/`), chosen per
endpoint by `Basil:Database:Read:Provider` / `Write:Provider` (`"PostgreSQL"`, `"MySql"`). The migrations are kept
engine-neutral on purpose (`database.md`: a migration may not contain `Npgsql`, `MySql`, `type:` or
`.HasColumnType(`). The tests run on either engine (`BASIL_TEST_PROVIDER`, Testcontainers.MySql in
`tests/Support/Basil.Testing.Persistence`). The diagnostics report no pool numbers on MySQL.

Steps (to detail and approve):

1. Remove MySQL: the provider, its packages, the `Provider` settings (the connection strings stay), the MySQL test
   fixture and the neutral-migration rule; decide whether the `IDatabaseProvider` seam still earns its place with one
   engine (keep only what the read/write split needs) — `ponytail`: drop it if nothing else implements it.
2. Use PostgreSQL types where they fit: `timestamptz` everywhere, `citext` or a case-insensitive collation for user
   and channel names instead of lower-cased shadow columns, `jsonb` for stored settings and match events if they are
   stored as text, enums as PostgreSQL enums or small integers by decision.
3. Indexes from the real queries: list every repository query (Users, Scores, Multiplayer, Chat, Beatmaps), take
   `EXPLAIN (ANALYZE, BUFFERS)` on a seeded database, add the missing ones (partial indexes for "open match", "active
   session", covering indexes for leaderboards), remove unused ones (`pg_stat_user_indexes`).
4. Query shape: compiled queries for the hot paths (login, presence, score submission, leaderboard), `AsNoTracking` on
   reads, `ExecuteUpdate`/`ExecuteDelete` for bulk changes, no N+1 (F-039 found per-member uncached block lookups),
   bounded reads (Streaming invariant).
5. Connection settings: Npgsql pool sizes per endpoint, multiplexing decided by measurement, prepared statements,
   `NpgsqlDataSource` once per endpoint.
6. Squash into one fresh `InitialCreate` per module (D3), regenerate snapshots, update `database.md`,
   `persistence.md`, `configuration.md`, `diagnostics.md`.
7. Verify with the load test of the blockers (D5): queries per second, p95 latency and pool use before and after.

### Plan P2: Docker only, no native builds

Owner (2026-11-18): Basil runs only as a Docker container; the native builds per platform are no longer supported.

Today: `release.yml` publishes six self-contained archives (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
`osx-x64`, `osx-arm64`), packs a Velopack updater (`continue-on-error`) and pushes an image to Docker Hub. The server
has an update checker (`Basil/Configuration/UpdateChecker.cs`), a native restart that starts the same executable
again (`Basil/Lifetime/Restarter.cs`), and a dashboard that needs a terminal (`--console 1`).

Steps (to detail and approve):

1. Release: drop the archive matrix and Velopack; build and push one multi-architecture image (`linux/amd64`,
   `linux/arm64`) with version tags; the Docker image tried on 2026-11-18 is 1.07 GB (ffmpeg): slim it (a smaller
   ffmpeg build or a separate stage, no `.pdb`/`.xml`, F-070).
2. Server: remove the update checker and the native restart path; `restart` always exits 75 and the compose file uses
   a restart policy that tells 75 from 0 (`on-failure`, F-069); remove what only the native run needed.
3. The shipped `docker-compose.yml` becomes the one supported way to run: PostgreSQL with a health check, named
   volumes for `Data` (with `Configurations` under it, F-052), the certificate, logs; secrets from an `.env` file
   instead of passwords in the command line.
4. Operator access: the dashboard through `docker compose attach` (`tty: true`, `stdin_open: true`) or
   `docker compose exec basil ./Basil` for commands; decide which one `console.md` documents (open question below).
5. Docs: `deployment.md` and `docker.md` merge into one path; `configuration.md` lists environment variables first;
   `development.md` keeps `dotnet run` for contributors only.
6. Verify: a clean machine with only Docker reaches a working server (osu! client, IRC, API) by following the docs.

### Plan P3: the system knows no caller (D13)

Owner (2026-11-18): the chat system carries messages and keeps no history; IRC and the osu! client are two ways into
it; the osu! client, the API and BasilBot are ways to call the system; Domain and Application know nothing of them,
only the Hosts do. Today Application names the callers (F-086). Proposed shape, to approve:

1. **Domain and Application model only the User** (owner, 2026-11-18): "On Domain and Application, only the User is
   modelled, to act. A Connection belongs to a Host and is used to perform the User's operations. So one user has
   several ways to act, which avoids things getting out of sync." The actor of every operation is the User; a room
   seats a User, the game status and presence are the User's, privileges and silence are checked on the User. A
   Connection (osu! client, IRC, API, tourney client, console) is a Host object that translates its wire into the
   User's operations and delivers the User's events back. `StableConnection`, `IrcConnection`, `ApiConnection`,
   `TourneyConnection`, `OperatorConnection` and the `Connection` base leave Application.
   To settle while detailing: which rules today hang on the kind of connection and where they go (only someone with
   a game client can be seated and play: a Host-side check before the User's operation, or a fact the Host reports on
   the User such as "has a game client online"); the session limits per kind and the session history (Authentication
   persists sessions today); how the console acts without a User (an operator user, D4 privileges); and what an
   event addressed to a User means when the User has several connections (every connection of every Host gets it).
2. **Chat** keeps its rule: Application owns channels, membership, delivery and silence; IRC and the osu! client are
   two Hosts that translate their wire to the same chat messages.
3. **Domain keeps the game, not the client.** Game concepts (modes, mods, beatmaps, scores, slots, the spectated
   play) stay in Domain; what describes the osu! program (`OsuClient`, `OsuVersion`, `OsuFlags`, `OsuFingerprint`,
   the client hash) moves to the Stable Host and Protocol. Sessions store what the system needs (for example an
   opaque client label for display) instead of an `OsuClient`.
4. **BasilBot becomes a caller**: its commands are parsed by a Host (a chat-command host) and sent as the typist's
   ordinary messages through Mediator; what remains of it below the Hosts is only what the system itself does
   (announcements and countdowns of a room are behaviour of the room).
5. **Rules**: rewrite the parts of `AGENTS.md`, `architecture.md`, `module-layout.md`, `application-design.md` and
   `identity-privileges.md` that place callers in Domain or Application; add an architecture test that fails when
   `src/Domain`, `src/Application` or `src/Infrastructure` names a caller (`Stable`, `Irc`, `Api`, `Osu`, `Bot`).
6. **Order**: after G1 and before G2, because G2 rewrites the osu! flows on top of whatever the session model is;
   doing G2 first would be done twice.

## Progress

| Date | Step |
|---|---|
| 2026-11-17 | Reading done, interview done (D1 to D10), evaluation started with IRC. |
| 2026-11-17 | IRC and BasilBot tried with raw lines and Irssi (F-005 to F-020); the owner graded F-005 to F-016 as blockers. Release build: 0 errors, 145 warnings. The work is handed to a new main agent; see [`handoff.md`](handoff.md). Next: the HTTP API with Postman. |
| 2026-11-17 | HTTP API (F-021 to F-032, Postman run), Cli part 1 (F-034 to F-038), the owner's reports on logging, multiplayer scores, chat latency and spectating (F-033, F-037, F-039 to F-045, latency measured). Files moved from `docs/` to `plans/review/`. Next: the rest of the Cli, then a real osu! client. |
| 2026-11-18 | Real osu! client with the owner driving it (F-044 confirmed, F-054 to F-060): actions wait a poll, the room does not draw the seated player, the client throws when a round starts. The owner decided D11 (bancho.py is the reference for every osu! client feature) and closed the osu! client trial: its findings are fixed by aligning with bancho.py, not by more trials. Next: the rest of the Cli, then the documentation. |
| 2026-11-18 | D12 (every text sent to a client goes through the humanizer and writing skills). Documentation reviewed (F-061 to F-067, report `2026-11-18-docs-report.md`), Docker image and release archive tried (F-068 to F-070), Cli part 2 (F-071 to F-077, report `2026-11-18-cli-report.md`). Next: the codebase, then release criteria per client and the roadmap. |
| 2026-11-18 | Build and whole test suite once (5,770 passed, 0 failed; F-078, F-079), codebase review (F-080 to F-085, report `2026-11-18-code-report.md`). Plans P1 (PostgreSQL only, EF Core, tuned) and P2 (Docker only) added to the roadmap. The evaluation of D9 is complete except the load test (D5) and WeeChat/Halloy. Next: release criteria per client, then the roadmap for approval. |
