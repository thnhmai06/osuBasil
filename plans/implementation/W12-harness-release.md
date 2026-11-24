# W12: load harness, release process, P1 clean-up (roadmap E9.0, C5.5, E2.13)

Source of truth: `plans/review/release-1.0.md`, rows **F-105**, **F-120**, plan **P1** and roadmap items E9.0, C5.5,
E2.13. Read them first, plus `docs/for-developers/database.md`, `docs/for-developers/osu-stable.md`,
`docs/for-technicians/docker.md`, `.github/workflows/{ci,release}.yml`. W1 to W11 are committed (last `c53f8bf24`).
Tests stay deferred (owner 2026-11-24): do not touch `tests/`.

## Part A: load harness (F-105, E9.0)

1. A console project `tools/LoadHarness` (not in `Basil.slnx` runtime graph; add it under a `/Tools/` solution folder
   like `tools/RulesetManifest`). It references only `Basil.Protocol.Game.Stable` and `Basil.Protocol.Irc` for wire
   encoding, plus the BCL; no Application or Infrastructure reference.
2. It drives a running server through nginx (base URL and IRC host/port as options; accept the self-signed
   `basil.local` certificate only with an explicit `--insecure` switch):
   - osu! clients: log in (the login body as `osu-stable.md` describes), then poll `c.` at the client's interval with
     an occasional chat message and a status change;
   - IRC clients: PASS/NICK/USER over TLS, join `#osu`, send a message every few seconds, answer PING;
   - API callers: sign in once, then a mix of reads (`/v1/users/...`, rooms, scores lists).
   The mix and counts are options (`--osu N --irc N --api N --duration 60s --ramp 10s`); accounts come from a CSV of
   `name,password` (the operator creates them with `./Basil users create`; document how in `PERF.md`).
3. It reports per kind: requests, failures by status/reason, latency p50/p95/p99, throughput; and samples the server
   container's CPU and memory when `--container <name>` is given (`docker stats --no-stream` is acceptable). Output: a
   plain table on stdout and a JSON file for the ledger.
4. `PERF.md` at the repository root: how to run the harness against the compose stack, the fixed scenario used for
   every comparison (write one: e.g. 200 osu!, 50 IRC, 20 API for 120 s after a 20 s ramp), and the ledger table
   (idea, baseline → result, verdict, why) per the performance-optimization skill. Leave the baseline row empty: the
   coordinator runs it.

## Part B: P1 clean-up that needs no measurement (C5.5)

5. Remove the `IDatabaseProvider` seam (`Basil.Infrastructure.Shared/Persistence/DatabaseProvider.cs` and every use);
   the Npgsql setup is called directly where the provider was. Behaviour unchanged; migrations unchanged.
6. Pool settings: document in `docs/for-technicians/configuration.md` that pool size and timeouts go in the connection
   string from `.env` (`Maximum Pool Size`, `Timeout`, `Command Timeout`); no new setting key.
7. Do **not** add indexes or compiled queries in this wave; list in your report the queries you would index (table,
   columns, the code that issues it) so they can be measured against the baseline.

## Part C: release process (F-120, E2.13)

8. `ci.yml` also runs on pushes to `develop`; add an `nginx -t` step for both configurations in `docker/nginx/` (run
   the official `nginx` image with the configs mounted; the Cloudflare variant too).
9. `CHANGELOG.md` at the root (Keep a Changelog format), with an `Unreleased` section summarising W1 to W11 from
   `git log --oneline` since `0cd481918` in user-facing terms (operators and client authors), grouped Added / Changed /
   Removed / Security. Plain sentences, no internal type names.
10. `release.yml`: before building the image, check that the tag's section exists in `CHANGELOG.md` and use it as the
    GitHub release body; keep the manifest check and the multi-arch image as they are.

## Rules for you

Rider MCP (`search_symbol`, `find_usages`, `safe_delete`, `get_file_problems`, `build_solution_*`); beware the Rider
post-edit hook that may reformat whole files: if a file shows hundreds of unrelated changed lines, restore it from HEAD
and reapply only your change. OpenCode Go at `http://localhost:4096` with cheap flash models for mechanical sub-tasks.
Build only (`dotnet build Basil.slnx -c Release --nologo -v q`, and the harness project), no tests, no commit, no
`git add`, do not touch `Basil/Data/appsettings.json`. Do not start or stop Docker containers. An item needing an
unwritten decision: skip it, finish the rest, report.

## Report

Per item: what changed and the files; candidate indexes (item 7); skipped items and why; decisions you took; build
result.
