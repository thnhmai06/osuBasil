# Basil documentation review

Read-only review of branch `develop`. Marks: **verified** = checked against source; **likely** = read but not fully traced.
Paths are under `/mnt/shared/Development/osuBasil`. Known findings F-021, F-038, F-055 are only confirmed, not repeated.

## Top ten problems

1. **First-run Postgres command cannot work.** `docs/for-developers/development.md:21`:
   `docker run -d --name basil-pg -e POSTGRES_PASSWORD=basil -e POSTGRES_DB=basil -p 5432:5432 postgres:17-alpine`.
   Line 25-27 says the default connection string uses `Username=basil`, but no `POSTGRES_USER=basil` is given, so the only
   role is `postgres`. A contributor's first `dotnet run` fails to log in. (verified: `Basil/Configurations/appsettings.json` `ConnectionStrings`; `docker-compose.yml` sets `POSTGRES_USER: basil`.)
2. **Release archive format is wrong for Linux and macOS.** `docs/for-technicians/deployment.md:75`: "Extract: `Basil-{version}-{platform}.zip`". The release
   workflow (`.github/workflows/release.yml:72-80`) makes `.zip` only for `win-*`; Linux and macOS get `Basil-<v>-<rid>.tar.gz`
   (a zip would drop the executable bit). An operator on Linux finds no zip. (verified)
3. **osu! login doc says a second login replaces the first session; the server refuses it.**
   `docs/for-client/osu-stable/authentication.md:131`: "Logging in again with the same account replaces the previous session."
   `Basil.Application.Users/Registry/UserRegistry.cs:491-551`: at the per-type limit, only an idle connection is closed first; a stable
   login then fails with `TooManySessions` ("This account has too many open sessions."). Only API connections are replaced. (verified)
   Also line 133 omits that a tourney client needs the `Tournament` privilege (`UserRegistry.cs:500`).
4. **Hardware-ban check is documented as a feature; it is out of scope.** `authentication.md:119` "the account being blocked by a hardware-ban check",
   `:149` "Basil can compare the supplied hardware information with hardware associated with banned accounts", `:153` "Established verified accounts are not rejected...".
   `docs/for-developers/working-scopes.md:154` says "Multi-account detection, hardware bans, client hash checks, the `Verified` grant | Out of scope",
   and no such check exists in `Basil.Application.Users`. The page also mentions "verified accounts" while `:159` says there is no verification. (verified)
5. **IRC host name contradicts itself across pages.** `docs/for-client/irc-client.md:18` "Server | `irc.<domain>`" and `Basil/ServerOptions.cs`
   ("the IRC server is `irc.` plus this domain", and `LanAdvertiser.Labels` advertises `irc`) versus
   `docs/for-technicians/https.md:48` "It needs no `irc.<domain>` record" and `troubleshooting.md:226` "There is also no `irc.<domain>` hostname requirement".
   The LAN name list in `configuration.md:135-136`, `https.md:12-22` and `getting-started.md:77-87` omits `irc.<domain>` although the server answers mDNS for it. A user told "no record needed" and "server = irc.basil.local" cannot tell which to type. (verified)
6. **Default log level documented as `Information`; the shipped file is `Trace`.** `docs/for-technicians/logging.md:32`
   (example `"Default": "Information"`), `troubleshooting.md:381-385` ("return the setting to: Information"), but
   `Basil/Configurations/appsettings.json` sets `Default`, `Microsoft`, `Microsoft.Hosting.Lifetime` and `System` to `Trace`. An operator who deploys the
   shipped file writes Trace logs (100 MB/day rolls, up to 1 GB kept) while the docs imply a quiet default. (verified)
7. **ffmpeg failure documented as `503`; no such behaviour exists.** `deployment.md:45` "Without it, audio preview requests return `503 Service Unavailable`",
   `troubleshooting.md:60` heading "Audio preview returns `503 Service Unavailable`". The only `503` in `src/Hosts` is `shuttingDown`
   (`RestPipeline.cs:28`, `SseHub.cs:285`); `BeatmapPreviewMaker` just writes no preview. An operator diagnosing a missing preview looks for a status that never appears. (verified)
8. **Stale "console is not implemented" in the scope document.** `docs/for-developers/working-scopes.md` ("Account creation and operator console"):
   "The console is planned for the Host phase in `Basil.Cli`; it is not implemented yet ... The hosts have not been migrated to the new Application contracts".
   `Basil.Host.Cli` exists, `docs/for-technicians/console.md` specifies it, and `migration-status.md` says the host migration is done. This is the page the owner names as the scope authority. (verified)
9. **`protocol.md` contradicts `authentication.md` on session storage, and both misdescribe it.** `docs/for-client/osu-stable/protocol.md:115`
   "Sessions are stored in server memory ... active sessions do not survive the restart" vs `authentication.md:137` "Sessions are recorded in the database, not only in server memory".
   Two pages for one fact; the reader cannot tell what a restart does. (verified contradiction)
10. **Broken link and a nonexistent `/live` rule in the client envelope page.** `docs/for-client/api/response-envelope.md:109` links `../../adr/ADR-009-rest-v1.md`;
    `docs/adr/` does not exist (AGENTS.md says ADRs were removed), so the table of error codes the page promises is unreachable. Lines 9 and 217 say the envelope
    does not apply to "`/live` Server-Sent Events streams", but `sse.md` lists `GET /v1/matches/{id}`, `/v1/users/{id}/spectate` and `/v1/diagnostics`, none of which end in `/live`;
    only `/v1/rooms/{id}/live` does. Line 80 ("`200 OK` with `data: null` for operations that would return 204") is also contradicted by the 204 in `sse.md:50`. (verified)

Further notable items (not in the top ten): `docs/for-developers/testing.md:49` hard-codes a personal path
(`/home/haith/.docker/desktop/docker.sock`); `testing.md` says the composition test calls `AddBasil()` and `AddBot()` but `AddBot` exists nowhere in `src` or `tests`;
`working-scopes.md:56` has a truncated sentence ("A command of the user who typed it, and the bot only replies.") and `:59` points to `api.<domain>/docs/basil-bot/`, a route that does not exist (the guide is `/guides/bot`).

## Part A: generated references

### A1. Route coverage (`Basil.Host.Rest`)

82 `Map*` calls. 80 `WithSummary` call sites (two of the route-generating loops make several routes from one call). Routes with **no summary and no description**: the three
`/guides`, `/guides/{slug}`, `/guides/{slug}.md` routes in `Routes/GuideRoutes.cs:16-25` (documentation pages, so arguably not API; they are not tagged and not in OpenAPI).

Routes with a summary but **no description** (verified from the route files): `GET /v1/faqs/{entry}`, `DELETE /v1/faqs/{entry}`, `GET /v1/menu/seasonals`, `GET`/`DELETE /v1/menu/icon`,
`DELETE /v1/beatmapsets/{id}`, `GET .../audiopreview`, `GET .../storyboard`, `GET /v1/beatmaps/{id}/{audio|video|background}` (3), `POST /v1/auth/logout`,
`GET /v1/settings`, `GET /v1/settings/{category}`, `GET`/`DELETE /v1/screenshots...` (2), `DELETE /v1/users/{id}/avatar`, `GET /v1/users/{id}/sessions/{sid}`,
`GET /v1/users/{id}/friends`, `GET /v1/users/{id}/blocks`, `GET /v1/scores/{id}`, and **all 23 room command routes** (see A3). That is roughly 40 of ~110 effective routes with no description.
Of the 58 `WithDescription` call sites, most are one-liners; only about 15 follow the four-part order of AGENTS.md rule 5.

### A2. Fifteen routes read against rule 5

Good (contract only, errors named): `POST /v1/auth/login` (401/429 stated, token shown once), `PUT /v1/users/{id}/avatar` (422 and `tooLarge`),
`GET /v1/matches/{id}` (SSE contract, 404 for private, 204 on reconnect), `GET /v1/users/{id}/spectate`, `POST /v1/beatmapsets` (409, 422), `POST /v1/scores/performance/recalculate` (202, 409).

Violations or weaknesses:

* `POST /v1/settings/ruleset/update` (`Routes/RulesetRoutes.cs:24`): "Stages the newest stable version for the next restart, after checking it against the hash its feed publishes; nothing is kept when the check fails." The hash/feed check is a mechanism; the contract is "refused with 502 when the new version cannot be verified". Fails the Implementation Test (verified).
* `GET /health` (`RestHost.cs:89`): fine in content, but it lives outside `/v1`, has no `RequireToken`, and the generic OpenAPI transformer still adds 404 and 422 responses to it (see A3).
* `GET /v1/settings/{category}`, `GET /v1/users/{id}/friends`, `GET /v1/scores/{id}`: summary only; do not say who may call (public or token) or what 404 means. (verified)
* `DELETE /v1/faqs/{entry}`, `DELETE /v1/beatmapsets/{id}`: no mention that the record or files are removed, nor that the call is refused with 403 or 409. (verified)
* `GET /v1/users/{id}/sessions` (`UserRoutes.cs:63`): "a bitmask of the session types" never says which numbers. A client must read the source. (verified)
* `POST /v1/announce` (`ContentRoutes.cs:31`): good content but the `to` list and `404` rule are the only errors; says nothing about the privilege needed. (likely a rule-5 gap, since other routes also omit it deliberately)
* `PATCH /v1/settings/{category}`: "rate limits already counting a caller keep their old limit until they expire" describes an internal behaviour of the limiter; borderline for rule 5 (likely).
* `PATCH /v1/beatmapsets/{id}`: the status advice "use a status with a leaderboard ... so those functions keep working" is documentation of client behaviour, useful, but not in the order prescribed (special case before what it provides). Minor.
* Order rule: nearly every description starts with a special case or a parameter ("`query` uses the room search syntax...", `ListAsync`) instead of "what the endpoint provides". (verified for rooms, users, channels)

No description names PostgreSQL, Redis, DI, middleware, `Channel<T>` or file paths. Rule 5's "never implementation" is respected except the ruleset-update line.

### A3. Errors and room command bodies (F-022)

* **Responses: F-022 is partly refuted.** `Basil.Host.Rest/Http/OpenApiSetup.cs:84-134` now adds generic responses to every operation: 200 (envelope or `text/event-stream`),
  401 and 403 for token routes, 404 on every route, 409 and 422 on non-GET, 422 on GET, 429 on every route. So a status list exists. But it is **generic, not per-route**:
  e.g. `/health` and `POST /v1/auth/login` are given a 404 and 409 they never return, `GET` routes without a body or paging get a 422 "paging parameter out of range", and the error `code`
  (`errors.code`: `tooLarge`, `targetOffline`, `locked`, ...) is never listed per route. Only a handful of descriptions name a status and code in prose. Verdict: the "no responses described" gap is closed in form but the content is not trustworthy per route. (verified)
* **Room command request bodies: F-022 confirmed.** `Routes/RoomRoutes.cs:42-67` maps every command with `MapPost("/{id:int}/<name>", async (HttpContext, int, Lobby, ISender) => ...)` and reads the
  body by hand (`ReadFromJsonAsync(type, ...)`) with no `.Accepts<T>()` (compare `UserRoutes.cs:77`, which has one), no description, and only `command.Summary`
  (e.g. "Update the number of usable slots."). The OpenAPI document therefore shows no request body for the 18 commands that take one (`size`, `set`, `name`, `password`, `private`, `map`, `mods`, `move`, `team`,
  `host`, `kick`, `ban`, `unban`, `invite`, `addref`, `removeref`, `start`, `timer`), and nothing says who may call them (creator or referee), what `RoomOutcome` refusals mean (404 / 403 / 409), or the 400 "A JSON body is required" cases. This is the main remote-control surface of the product. (verified)

### A4. BasilBot and IRC catalogs

* **BasilBot** (`Basil.Application.Bot/Commands/CommandCatalog.cs`): 38 commands, each with a path, a usage string (`Usage`) and a summary reply. **No command has an example**; `BotReference.cs` writes a two-column table (`Command | What it does`) with no example column and no privilege column.
  Gaps (verified):
  * `!channel` has usage `list|create|set|delete` and summary "manage the general channels (needs privilege)": no per-subcommand arguments, no options. The only description of `name=value` options is hand-written in `docs/for-client/basil-bot.md:73-76`, contradicting "this page intentionally does not duplicate the command list" (`:109`).
  * `!mp private [0|1]`, `!mp set <teammode 0-3> [scoremode 0-3] [size 1-16]`, `!mp mods <mods>|Freemod|None`, `!mp map <beatmap id> [playmode]` give no valid values for modes, mod syntax or "playmode" (numbers? names?).
  * `!mp addref`/`removeref` say "(creator only)" while AGENTS.md says the creator and `Tournament` can manage; privilege per command is not listed.
  * `!faq <entry>|list`, `!where <username>`, `!roll [max]` have usage but no example output.
* **IRC** (`Basil.Host.Irc/Session/IrcReference.cs`): a table of 23 commands `(Command, Needs sign-in, What it does)`. **No usage line and no example for any command**; `CAP`, `AUTHENTICATE`, `PRIVMSG`, `WHO`, `WHOIS` say nothing about parameters. The page has no connection example (what a PASS/NICK/USER sequence or an SASL `AUTHENTICATE` payload looks like), no numeric list (`464`, `421`, `482` are mentioned but not listed), and no mention of `#mp_<id>` joining rules beyond a bullet. (verified)
* Both generated pages are good as a summary and weak as a how-to.

## Part B: Markdown documents

### `docs/for-client/osu-stable/getting-started.md`

* Verified correct: `-devserver <domain>`, default domain `basil.local` (`appsettings.json`), host labels (`LanAdvertiser.Labels`, `StableRoutes.cs:18`), `bancho_connect.php` stub returns an empty 200 (`OsuWebRoutes.cs:141`), in-game registration route `POST /users`.
* Problem (verified): `:120` "https://api.<domain>/docs/" reaches the Scalar page, but the generated OSU reference is at `/docs/osu-stable` and the HTTP API at `/docs/v1` (`index.md:71-72`); the plain `/docs` page defaults to the first document, fine, but the link text says "API documentation" for an admin task and gives no pointer to the create-account route.
* Problem (likely): the hosts list omits `irc.<domain>` (see top 5); `:42` "certificate must cover ... required subdomains" without listing them (the list is only in `https.md`).
* Completeness: a newcomer can finish with the page, except the Linux/macOS "launch" step: osu! stable is a Windows client, and `osu!.exe -devserver` is given with no Wine/Proton note. The `-devserver` option, certificate and hosts steps are otherwise complete.
* Duplication: the hosts-file list repeats `configuration.md#name-resolution-on-a-lan`, which repeats `https.md`, in three pages (rule 7).
* Guideline: Overview first, then steps, then troubleshooting: good.

### `docs/for-client/osu-stable/authentication.md`

* Problems (verified): sessions replaced (top 3), hardware ban (top 4), `:5` "osu! stable does not use a separate login packet" is fine, `:137-141` vs `protocol.md:115` (top 9). The third-line field list `:51-56` ("display city flag", "hardware hashes") matches `LoginRequest.Parse`, but the example `b20250101|1|1|a1b2c3...|0` is not a valid client hash form (the field is `OsuFingerprintExtensions.Parse`) and a newcomer cannot build a request from it. Hand-written protocol detail duplicates `/docs/osu-stable`.
* Completeness for an osu! player: there is no failure-message list (e.g. the `too many open sessions` and `tourney privilege` replies exist in `StableReplies.cs` and are not mentioned); a player blocked by the session limit is not told why.
* Guideline: one topic, overview first: ok. Verdict fix.

### `docs/for-client/osu-stable/protocol.md`

* Verified: packet framing `uint16 id, uint8 reserved, uint32 length` matches `Protocol.Stable`; hosts `c`, `ce`, `c4`, `c5`, `c6`.
* Problems: `:115-119` memory-only claim (top 9). Code fences carry stray generated attributes (```` ```http id="v2l7fq" ````, `:19`, `:26`, `:50`, `:59`, `:75`, `:98`, `:137`) that render as literal junk on some viewers. The packet framing and the long-polling description duplicate the generated `/docs/osu-stable` reference and `for-developers/osu-stable.md` (rule 7).
* Verdict: fix; could be shortened to a pointer.

### `docs/for-client/api/overview.md`

* Verified: `POST /v1/auth/login`, `GET /v1/auth/session`, `POST /v1/auth/logout`, Bearer scheme, 401/403 (`OpenApiSetup.cs`), console `users create --admin` (`UserCommands.cs:38`).
* Completeness (the first task, "call the API with a token"): the page tells you which routes to call but gives **no request example**. There is no `curl` that logs in, extracts `data.token` and calls an endpoint; the only working example is hidden in `for-developers/development.md:100-103` (contributor audience) and `deployment.md:316-321`. A tool author must read the OpenAPI to learn the login body (`username`, `password`). Missing step. (verified)
* Duplication: `:88-110` repeats `response-envelope.md` verbatim.
* Problem: `:42` `/docs/` rather than `/docs/v1`; `:59-62` list "osu! client protocol, Basil API, BasilBot, IRC client" as sections of a GitHub Pages site but give no sub-paths.
* Verdict: fix.

### `docs/for-client/api/response-envelope.md`

* Verified: field table, `Retrieval successful`, `Operation successful` (`Api.cs:44,50`), default limit 25 and maximum 100 (`Targets.cs:11-18`), 413 `tooLarge` and 400 `invalid` (`RestPipeline.cs:80`), `errors.code` camelCase.
* Problems (verified): broken ADR link `:109`; `/live` wording `:9`, `:217`; 204 contradiction `:80`; `:111` says a path number that does not fit an integer is 422 `invalid` and a bad query value 400 `invalid`, but `Targets.cs:17-18` returns `invalid` through `Api.Invalid` for an out-of-range `limit` (the doc agrees at `:152`), so the status split is not documented in one place. `:178` "Sets of privileges and privileges are bitmasks" is garbled (likely a lost word).
* Duplication: a second full page for the same envelope exists at `docs/for-developers/response-envelope.md`, and the index declares both authoritative (`index.md`), one per audience: acceptable by the index but the two must stay in step (not checked line by line).
* Verdict: fix.

### `docs/for-client/api/sse.md`

* Verified: `retry: 3000` and `: keep-alive` (`SseHub.cs:89,274`), `targetOffline` 409 (`SpectateRoutes.cs:42`), `204` on reconnect (`MatchRoutes.cs:28`), `RetryAfter: 5` while stopping (`RestPipeline`), the five routes exist.
* Problems: `:4` "authoritative generated reference ... at `https://api.<domain>/docs/`" (see overview). `:12` says spectate "required" token: ok. A newcomer lacks a minimal client example (`curl -N -H "Authorization: Bearer ..."` or `EventSource` snippet) and the exact shape of a `bundle` item for rooms/matches; the shapes are in prose only.
* Duplication: description of snapshot/bundle semantics is repeated in each route's `WithDescription`; acceptable, but rule 7 says to link. Verdict: ok.

### `docs/for-client/irc-client.md`

* Verified: port 6667 (`ServerOptions.Irc.Port`), `POST /v1/users/{id}/sessions/irc` with `label` (`UserRoutes.cs:77-80`), 464 on wrong key, `482` for `TOPIC`/`MODE`/`KICK`/`INVITE` (`IrcCommands.Messaging.cs`), `Configurations/Templates/replies.json` (`RepliesOptions.cs:7`).
* Completeness (join IRC from the page alone): fails at the first step. The page says "Create [an IRC key] with the HTTP API" but never says that a Bearer token (from `POST /v1/auth/login`) and the account id are needed, nor shows a request. A player who has never called the API cannot proceed. Missing: the login call, the `id` lookup (`GET /v1/auth/session`) and a `curl` for the key. (verified)
* Inconsistency: server `irc.<domain>` vs `https.md`/`troubleshooting.md` (top 5).
* Verdict: fix.

### `docs/for-client/basil-bot.md`

* Verified: `!channel` options `topic`, `read`, `write`, `autojoin`, `visible` (`ChannelCommands.cs:120-144`), replies file path and ICU behaviour.
* Problems: the page is mostly design explanation for developers, not for a chat user: `:63-65` ("It is not an actor but the voice the server speaks with ... privileges mean nothing to it"), `:81-96` (connections, the console, operator typing as BasilBot, "implemented in `Basil.Infrastructure.Bot`"). Internal project names appear in a client page (rule 6/7 implementation detail). It duplicates `identity-privileges.md`. A player who wants "how do I create a match" gets a one-line `!mp <subcommand>` pointer and must find the generated page. No example of `!mp make`, `!mp invite`, `!mp start`.
* Duplication: `:73-76` repeats command syntax that the page says it will not duplicate (`:109`).
* Verdict: rewrite (short, command-user oriented, with a worked `!mp` session).

### `docs/for-technicians/deployment.md`

* Verified: `Basil:Server:Domain`, `ConnectionStrings:Write/Read`, Kestrel and certificate keys, default 443, `--console 0`, console `users create` works offline (`UserCommands.cs` `Offline = true`; `console.md:159-163`), BasilBot id 1 (`BasilBot.cs:10`), `~/Configurations/basil.local.pfx` shipped, `Logs/` and `Data/` created.
* Problems: archive naming (top 2); ffmpeg 503 (top 7); `:7` says only PostgreSQL while `configuration.md:69` and `Infrastructure/Persistence/DependencyInjection.cs:16` offer `MySql` as a provider (the two pages disagree on which databases are supported: likely a doc gap on one side).
* Completeness: the sequence `Configure -> HTTPS -> DNS -> first account -> start -> verify` is nearly complete. Step 5 runs `./Basil users create` before the first start; this works (offline mode migrates an empty database) but the page does not say so, and says the database "must already exist" which makes the reader create the role and database but gives no `CREATE ROLE`/`CREATE DATABASE` command. Windows start-up under a service manager is only one sentence. The `curl -k https://api.<domain>/v1/users/1` check needs the domain to resolve (step 4 says so).
* Duplication: the Moving/Upgrade section (`:328-372`) repeats `configuration.md:195-207`.
* Verdict: fix.

### `docs/for-technicians/configuration.md`

* Verified: all `Basil:Server:*` keys and defaults (`ServerOptions.cs`), `Basil:Assets:CacheDirectory`/`FfmpegFolder` (`AssetOptions.cs`, bound at `Beatmaps/DependencyInjection.cs:59`), `Basil:Bot:Replies:Path` (`RepliesOptions.cs`), `Data/` subfolders (`DataPaths.cs`), `Basil:Database:*Provider` values (`PostgreSQL`, `MySql`).
* Problems (verified):
  * `:66` "answer multicast DNS for the domain ... (off unless enabled)", `:113` "`LanAdvertise` (default `true`) -> `Multicast:Enabled` (default `false`)" and `:141` "Leave `Multicast:Enabled` at `false`". The shipped `appsettings.json` sets `"Enabled": true`, and `deployment.md:194` and `getting-started.md:54` say the server advertises "by default". The code default is `false`; the shipped file is `true`. Three pages give two answers.
  * `:135-136` name list omits `irc.<domain>` which `LanAdvertiser.Labels` includes (top 5). Also says "exactly these names".
  * `:173-179` table omits `Data/Spool`, `Data/Quarantine` (`DataPaths.cs:34,37`), the folder where refused uploads are kept for seven days (AGENTS.md); an operator watching disk use will find them.
  * `:206-207` "A development database that already applied an older `InitialCreate` must be dropped: the project edits migrations in place while it is not deployed" is a developer note in the operator reference, and in the same section as upgrade advice that says migrations are safe. It contradicts `deployment.md:362` ("Basil applies migrations automatically"). Likely the sentence belongs to `development.md` only.
  * `:100-128` "Upgrading from v1.0.0-alpha.1" is a migration table for one past release; it will rot (guideline: "describe the system as it actually works").
  * `console.md` is also linked as authoritative for the console but `:94` ("Basil <command> run one console command (in the running server, or offline)") does not mention the exit codes; fine.
* Duplication: Settings and key tables are repeated in `deployment.md:98-157` and `https.md:79-113`; the Beatmapsets ingestion note duplicates `beatmap-ingestion.md`.
* Verdict: fix.

### `docs/for-technicians/https.md`

* Verified: SAN list matches the host labels; `basicConstraints=CA:TRUE` guidance; the bundled-certificate warning exists (`KestrelSetup.cs:35-37 UsesPublishedCertificate`); `Kestrel:Certificates:Default:Path` relative to the executable.
* Problems: `:48` no `irc.<domain>` record needed (top 5); `:62` "It matches `basil.local` and its subdomains only" not checked against the pfx (likely true; the pfx is binary).
* Completeness (configure TLS): complete for Windows, macOS, Linux; Windows trusting the cert uses `Import-PfxCertificate` into `LocalMachine\Root` with the private key: acceptable on a LAN. The reverse-proxy section lacks a sample proxy configuration (Caddy/nginx) and says nothing about forwarding the `Host` header, which Basil uses to pick the sub-domain host (routing is by `Host`), so a proxy that rewrites it breaks the `c.`/`osu.`/`api.` routing. (likely a missing warning; routing by host is in `StableRoutes.cs:18,50`)
* Verdict: ok with the IRC fix.

### `docs/for-technicians/docker.md`

* Verified: compose file matches (`docker-compose.yml`: service names, `docker-data/Postgres|Data|Templates|Logs`, `restart: unless-stopped`, `stop_grace_period: 90s`); the image contains `ffmpeg` and `curl` (`Dockerfile:8`); `linux-x64` self-contained publish (`Dockerfile:4`).
* Problems: `:44` "The image already holds the bundled development TLS certificate" (verified `COPY`, likely true); `:48-50` password duplication is clearly explained. `:156-159` shows `/app/Basil, Configurations, Data, Logs`; the compose file also bind-mounts `Templates` but the "directly" `docker run` example (`:143-147`) does not mount `Templates`: that works because the whole `Configurations/` directory is mounted, so ok. `docker-compose.yml` publishes `6667` but `docker run` on `:143` also publishes it: consistent.
* Completeness (deploy with Docker): complete. Missing: how the Postgres `Host=postgres` name differs for the direct-run case, which is said only as "a reachable PostgreSQL database".
* Verdict: ok.

### `docs/for-technicians/logging.md`

* Verified: file name pattern `basil-.log` rolling (`ServerLogging.cs:69`; the page says `basil-YYYYMMDD.log`, consistent with Serilog rolling), retention check every six hours (`LogRetention.cs:61`), defaults 14 days, 30 files, 1 GB, `Rolling` `Day`/`100MB` (`appsettings.json`).
* Problems: default level (top 6); `:43` "applies while the server runs" vs `troubleshooting.md:379` "Restart Basil, reproduce the problem" for the same change. `:59` "visible in the dashboard and `/v1/diagnostics`" is wrong: the live diagnostics route is `GET /v1/diagnostics` as SSE (needs a token), not a JSON endpoint to read once. (verified in `DiagnosticsRoutes.cs`)
* Verdict: fix.

### `docs/for-technicians/troubleshooting.md`

* Verified: `ConnectionStrings` keys, `Basil:Server:Irc:Port` default, console `users <id> set --password`, `GET /v1/settings/locks` category exists (`Locks.cs`).
* Problems: ffmpeg 503 (top 7); `:226` irc hostname (top 5); `:379` "Restart Basil" vs hot reload; `:290` says only `Logging` and `Updates` apply while running, while `configuration.md:80-82` says `Logging:LogLevel` and `Basil:Updates` (not all of `Logging`; `Logging:File` is read once). `:232` "Make sure you use `/join #mp_x` instead of `/chat #mp_x`" is an osu! client tip in an operator page. `:239` quotes "Cannot join channel (no privilege)" but `IrcReplies.cs:75` is `Cannot join channel` (the shown text differs; likely a copy from another client).
* The osu! client section duplicates `getting-started.md:149-169`.
* Verdict: fix.

### `docs/for-technicians/console.md`

* Verified: `Data/basil.sock` and exit codes (the doc matches `docs/for-technicians/console.md` itself; command tables match `UserCommands.cs` patterns, `Offline = true`, option names `username`, `password`, `country`, `privileges`, `admin`), exit code 75 for restart (as stated in `deployment.md` and `docker-compose.yml` comment), `--console 0|1`.
* Confirmed F-038: the `--console 0` row ("Changes the server: no", `:19-20`) conflicts with `:125-163` (a shell command runs against the running server by socket) and with `deployment.md:241/257` (a service runs with `--console 0` and is operated through `Basil <command>` on the same machine). The column wording "Changes the server" is not defined.
* Problems: `:213-216` example `--privileges 1048575`: `UserPrivileges.All` is a six-bit set (`Owner 1<<3 ... Tournament 1<<5`), so `1048575` (20 bits) is not a valid "all" mask and may be refused with `invalid`; `--admin` is the right way (likely; not run).
* Duplication: 470 lines, of which the command tables (`:309-450`) restate `help` and the HTTP routes they mirror; the page itself says routes are described by the generated reference. Rule 7 forbids this; `help [<command>]` already prints the same. Dashboard tab/keys detail (`:34-300`) is closer to a UI manual than an operator guide.
* Verdict: fix (shorten; keep modes, socket, offline, exit codes).

### `docs/for-developers/development.md`

* Verified: `data/certs/basil.local.pfx` copied via `Basil/Basil.csproj:61`; `dotnet test tests/...` paths exist; the Postman folder exists.
* Problems: Postgres container (top 1); `:158` `Server:TrustedProxies` should be `Basil:Server:TrustedProxies`; `:96-109` repeats an operator task (curl with `Host` header) and `:90-92` "the server has not been deployed yet" is a time-bound statement.
* Verdict: fix.

### `docs/for-developers/architecture.md`

* Verified: module graph (`:55-61`) matches AGENTS.md except that it lists `Authentication, Beatmaps, Content -> Shared` while AGENTS.md calls the module `Auth` (naming differs, project is `Basil.Application.Authentication`); `Bot` row present; `tests/Architecture` files exist.
* Problems: `:11-13` says the page is the "direction" and the code may differ, but only `migration-status.md` (41 lines) lists differences; its "Known gaps" are test notes, not architecture gaps. `:53-` the layout of projects omits `Basil.Application.Common`, which exists in `src/Application/Common` and `tests/Application/Common` (verified directory listing; not in the graph). likely a doc omission.
* Verdict: ok.

### `docs/for-developers/working-scopes.md`

* Verified: `Basil.Infrastructure.Bot` exists; the hardware-ban row is correct and contradicts `authentication.md` (top 4).
* Problems: stale console text (top 8), truncated sentence at `:56` and bad link `:59`, `:78` `GET /menu-content.json` (F-055 confirmed: not in any `MapGet` under `Basil.Host.Rest`), `:141` "adding a note" moderation feature not checked.
* Verdict: fix.

### `docs/for-developers/testing.md`

* Verified: the layer table matches the `tests/` tree; Postman files exist.
* Problems: personal path (see "further notable items"), `AddBot()` does not exist (verified), `:101-103` Postman "OpenAPI generated from the code is the source of truth" while F-021 shows the generated OpenAPI is stale (known).
* Verdict: fix (minor).

### `docs/index.md`

* Verified: all relative links resolve (link check over `docs/for-client`, `for-technicians`, `for-developers`, `index.md`: only the four `Basil.Protocol.Irc/*.cs` links in `for-developers/irc.md` and the ADR link broke).
* Problems: the audience table sends "Player or tool" to `osu-stable/getting-started.md` only, with no direct link to the API (`for-client/api/overview.md`), the IRC page or BasilBot page: three of the four client journeys are reachable only through "See also" lists. The references table gives `/docs/v1` etc. correctly, but every `for-client` page says `/docs/`.
* Verdict: fix (navigation).

### Broken links found by script

* `docs/for-client/api/response-envelope.md` -> `../../adr/ADR-009-rest-v1.md` (no such directory).
* `docs/for-developers/irc.md` -> `../../Basil.Protocol.Irc/IrcMessage.cs`, `IrcTag.cs`, `IrcNumericReply.cs`, `IrcUserPrefix.cs` (paths do not exist at that depth; the files are in `Basil.Protocol.Irc/...` possibly in a subfolder, so the relative depth is off).
* No broken anchors in the files checked.

## Summary table

| File | Audience | Verdict | Why |
|---|---|---|---|
| `index.md` | all | fix | `for-client` journeys other than osu! unreachable from the table; `/docs/` vs `/docs/v1` |
| `for-client/osu-stable/getting-started.md` | player | ok | works end to end; duplicates hosts list and omits `irc.` |
| `for-client/osu-stable/authentication.md` | player / tool author | fix | false session replacement and hardware-ban claims |
| `for-client/osu-stable/protocol.md` | tool author | fix | memory-session claim contradicts authentication.md; stray fence attributes; duplicates generated reference |
| `for-client/api/overview.md` | API author | fix | no login-and-call example; repeats the envelope |
| `for-client/api/response-envelope.md` | API author | fix | dead ADR link, wrong `/live` rule, garbled enum sentence |
| `for-client/api/sse.md` | API author | ok | accurate; add a client snippet |
| `for-client/irc-client.md` | IRC user | fix | key creation needs a login the page never explains; host name contradicts https.md |
| `for-client/basil-bot.md` | chat user | rewrite | developer design content; no worked `!mp` example |
| `for-technicians/deployment.md` | operator | fix | zip vs tar.gz, ffmpeg 503, database setup missing |
| `for-technicians/configuration.md` | operator | fix | multicast default contradicts shipped file; missing `irc.`, Spool and Quarantine; stale upgrade table |
| `for-technicians/https.md` | operator | ok | accurate; fix irc line; add proxy `Host` note |
| `for-technicians/docker.md` | operator | ok | matches compose and Dockerfile |
| `for-technicians/logging.md` | operator | fix | documented default level is not the shipped one |
| `for-technicians/troubleshooting.md` | operator | fix | fictional 503, irc hostname, hot-reload contradiction |
| `for-technicians/console.md` | operator | fix | F-038, invalid `--privileges` example, too long and duplicates `help` |
| `for-developers/development.md` | contributor | fix | the Postgres command cannot connect; wrong config key |
| `for-developers/architecture.md` | contributor | ok | consistent with AGENTS.md; `Common` module not described |
| `for-developers/working-scopes.md` | contributor | fix | console "not implemented", truncated sentence, F-055 route |
| `for-developers/testing.md` | contributor | fix | personal path, nonexistent `AddBot()` |

## Not checked

`docs/for-developers/{irc,osu-stable,multiplayer,sse,response-envelope,assets,database,gateway,diagnostics,chat,privileges}.md`, `docs/for-agents/`, the published GitHub Pages output and the live Scalar pages (the server was not touched), and the rest of the `console.md` command tables beyond `users`. Statements about the bundled `.pfx` contents and the published Docker image were not inspected.
