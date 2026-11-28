# W10: HTTP API and SSE, Gateway, clean-up (roadmap E5, E6, C5.6, C6)

Source of truth: `plans/review/release-1.0.md`, roadmap groups **E5**, **E6**, item C5.6 and group **C6**, with their
rows: F-026 (edge half), F-023 (D22), F-024, F-025, F-082, F-028, F-042, F-022, F-067, F-029 (D18), F-089, F-088, F-090,
F-084 (edge half), F-092 (D33), D21 (last-seen flush), F-068, F-083, F-085, F-106. Read every row and decision first,
and `AGENTS.md` rules 5 and 6 (API and XML documentation), "API responses", "User-visible enums", "SSE", and
`docs/for-developers/gateway.md`. Waves W1 to W9b are committed (last `6007317ea`).

## E5: HTTP API and SSE

1. F-026 (edge half), F-023 (D22): authorize before reading the body; a missing or mistyped field answers 422 naming
   the field; room-command bodies take ids only.
2. F-024: every refusal has a sentence and names the role that can act (as BasilBot's `Denied` texts now do).
3. F-025, F-082: privileges and enums are named values in and out (JSON string enums through the existing serializer
   settings, flag sets as arrays of names), the Cli's `DashboardModel` too. Keep API record properties typed as their
   enums (AGENTS "User-visible enums").
4. F-028: a 404 says which thing is missing.
5. F-042: the spectate stream sends `replay` and `activity` events; SSE heartbeats below the idle timeouts (nginx
   `proxy_read_timeout` on SSE routes is 1 h; IRC idle 300 s; pick a heartbeat of 30 s unless the code already has one).
6. F-022, F-067: every route's body, answers and errors in the OpenAPI document; descriptions follow rule 5 order.
7. F-029 (D18): `AGENTS.md` ("SSE" invariant), `docs/for-developers/sse.md`, the client SSE page say stream routes need
   not end in `/live`.
8. W6 leftover: `POST /v1/announce` authorizes before it looks up the `to` users.

## E6: Gateway (D31, D33)

9. F-089: `RateLimiter` replaced by `System.Threading.RateLimiting` (partitioned limiters, constant memory per key,
   no purge on the request path).
10. F-088: `RuntimeSampler` reports the load signal to the Gateway; shedding works for every work class.
11. F-090: an allowance per work class; uploads: one at a time per user, a server-wide ceiling, a byte allowance per user
    and hour (plan P4 step 4).
12. F-084 (edge half): the inline rate rules (`chat:`, `rest-mutation:`, `register:server`, `diagnostics:gc-collect`)
    move into the Gateway; one `RateLimiter` registration. The per-address rules left from W1 go: `login-fail:{ip}`
    (keep the per-account login rule; `LoginAttemptGuard` keyed by account only), `rateLimits.connections.perIp`,
    `register-check:{ip}`; nginx holds per-address limits (D30).
13. F-092 (D33): `Elevated` tier for accounts with `Tournament`; `PollRequests` read from the settings.

## Core leftovers

14. C5.6 (D21): last-seen flushed every 30 s instead of 100 s (`SessionSweeper` or its successor after W8).
15. C6: F-068 (assess the advisories left in `Directory.Build.props` and the downloaded stack, AutoMapper
    GHSA-6vhm-j9h7-f9gh and GHSA-gpwp-5797-mjff included; justify each suppression in one comment line, or remove it),
    F-083 (no failure swallowed without a log or a counter, core sites; include the W4 gap: image refusals in
    Application are logged once), F-085 (unused members and packages, `TODO`s, two meters sharing a name,
    `ScoreListResult` without `Denied = 0`), F-106 (no fire-and-forget task drops its failure: the dashboard's
    `Task.Run` calls and W8's `Outbox` close send observe and log their failure).

## Rules for you

Split the wave yourself in the order above and build after each group. Rider MCP (`search_symbol`, `find_usages`,
`rename_refactoring`, `safe_delete`, `get_file_problems`, `build_solution_*`), OpenCode Go at `http://localhost:4096`
with cheap flash models for mechanical sub-tasks (OpenAPI `.Produces` lines, constant moves). Build only
(`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no commit, no `git add`, do
not touch `Basil/Data/appsettings.json` (report any setting key change; new keys keep the existing naming and become part
of the frozen surface, D38). New client-visible texts: plain first drafts in the reply class, listed in the report. An
item needing an unwritten decision: skip it, finish the rest, report.

## Report

Per item: what changed and the files; every HTTP status or body shape that changed (route, before, after); every
setting key added or removed; skipped items and why; decisions you took; build result.
