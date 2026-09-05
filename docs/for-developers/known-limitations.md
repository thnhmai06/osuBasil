# Known limitations

## Overview

This page lists what the perf-investigation effort (tracked outside the repo; see the PR history on
`chore/perf-investigation`) has **not** proven, alongside a full inventory of Basil's production dependencies
and why each exists.

A root cause here is either:

* **SUPPORTED** — the mechanism is traced through the code but has not been reproduced under real conditions, or
* **HYPOTHESIS** — consistent with the evidence gathered so far, but not yet demonstrated, or
* **NEEDS EXPERIMENT** — the only way to close it is a benchmark or load test that has not been run.

Do not treat an entry here as a confirmed bug to fix on sight. Each one already has a concrete next step; follow
that step (usually "reproduce it" or "measure it") before writing a fix.

## Open items

### Capacity above 100 concurrent users is unmeasured (NEEDS EXPERIMENT)

`Profiles/full.json` (stress ramping 100 → 5000 concurrent users, then a 12-hour soak at 750) has never run to
completion. Every load test run so far has either stopped short deliberately (to validate a fix) or crashed
before reaching this range (see the next item). The server's actual capacity envelope, and where its scaling
curve knees, are both unknown above the 100-user mark that has been exercised.

**Blocker:** running it requires direct supervision (see the next item) for however many hours the run takes.

### RC11 — the ADR-001 fix has never been re-verified under a full, unsupervised, sustained combined-load run

RC11 (the server dying under combined multiplayer + API load, root-caused to RC1's SQLite write-path saturation)
was fixed by ADR-001 (busy-timeout + `synchronous=NORMAL` + collapsing round-trip writes) and verified against
the specific load profile that originally triggered it (0 failures where there were previously 12 `SQLITE_BUSY`
failures at concurrency 100). That verification run was itself supervised and comparatively short. The full
`login → idle → chat → multiplayer → api → soak` sequence that first exposed RC11 has not been rerun end to end
since the fix, because every attempt to run it unsupervised risks the same crash-and-silently-waste-hours outcome
the original investigation hit twice. Until it is rerun under supervision, "RC11 is fixed" rests on a narrower
repro than the one that found it.

**Blocker:** needs a human present at the machine for the run's full duration (see "Handoff: what a load test
run needs" below).

### RC8 — protocol allocation and login fan-out (HYPOTHESIS)

`BinaryWriter`-per-primitive packet allocation, `GameSession`'s outbound queue's double-copy `Dequeue()`, and
login's channel-info broadcast fan-out are all confirmed present in the code. Whether any of them is the *next*
bottleneck after RC1 (SQLite writes) and RC5 (SSE) were addressed is not established — that requires re-profiling
under load, which needs the same supervised run as the two items above. Do not optimize these speculatively; the
plan's own rule for this phase is "profile first."

### Tourney client tracking's concurrency fix has unconfirmed real-world impact (HYPOTHESIS)

`_tourneyClients` was changed from a plain `HashSet<int>` to `ConcurrentDictionary<int, byte>` (matching the
pattern already used by `_referees`/`_bannedIds`/`_invitedIds` in the same file) because concurrent, unsynchronized
mutation of a `HashSet<int>` is a real category of bug. Whether it was ever actually hit — i.e., whether real
tourney client traffic produces the concurrent access this fix protects against — has never been observed, since
no load test scenario exercises tourney-client connections. The fix is safe and low-cost either way; only its
necessity is unconfirmed.

### Phase 7 — narrowing `BeatmapWatcherService` to non-recursive, `.osz`-only watching (blocked by code, not schedule)

The `.osz` direct-storage rewrite (ADR-006, now implemented and removed as a standalone doc — see
[`beatmap-ingestion.md`](beatmap-ingestion.md) for the current storage model) left this one follow-up
deliberately undone: `BeatmapsetRoutes.HandleReplace` and `HandleDelete` still each branch on
`FindBeatmapsetFolder(...)` returning non-null, i.e. they still actively handle a beatmapset stored in the
legacy extracted-folder layout. Narrowing the watcher now would silently stop live-reconciling any beatmapset
that has not yet migrated to the canonical `.osz` layout. This is not a "wait some amount of time" blocker; it is
"wait until those two `if (targetFolder is not null)` branches have no remaining folders to reach," which is a
statement about deployment data, not a date. Re-check by searching for `targetFolder is not null` /
`folder is not null` in those two handlers before starting this.

### Match Hosts/Referees API — cannot be manually exercised with real osu! clients in this environment

Issue #4 marked this "Unable to test" because reproducing it needs two osu! client connections at once, and the
environment that reported it only runs one client at a time (confirmed: this is an environment constraint, not
evidence of a defect). Basil's own test suite already exercises multi-session host/referee transitions in-process
(constructing `GameSession` objects directly, which does not require a second real client): see
`MatchTransferHostHandlerTests` (host transfer, including the TOCTOU re-check under lock), the referee-focused
cases in `MpCommandServiceTests` (`HandleAsync_AddRef_*`, `HandleAsync_RemoveRef_*`, `RemoveRef_LastReferee_*`),
and the API-level cases in `MatchSubResourceEndpointTests` (`Refs_*`, `Hosts_*`, `Ban_*`). If a specific
host/referee transition is ever suspected of a real bug, write an in-process regression test for that exact
transition rather than trying to reproduce it with two real clients.

## Recently closed (investigated this round, not left open)

* **`EnvelopeMiddleware`'s two internal throw sites** (`JsonNode.ParseAsync`, `GetValue<int>()`) — traced and
  confirmed unreachable by any current `basilapi` route (every enveloped body is framework-serialized from a
  typed object, never a re-parse of client input), and covered by `ExceptionLoggingMiddleware` even if they were
  ever hit. See [`response-envelope.md`](response-envelope.md)'s "Internal invariant" section.
* **`MatchMembershipService.CloseAsync`'s slot/registry-miss edge case** — traced and confirmed unreachable:
  `PlayerLogoutService.LogoutAsync` is the only code path that removes a `GameSession` from the registry, and it
  always clears the match slot under the same match lock first. See [`multiplayer.md`](multiplayer.md)'s
  invariants list.

## Handoff: what a load test run needs

The two NEEDS EXPERIMENT items above both need the same thing: a supervised run of the full load-test sequence
with someone able to notice a crash and stop the run, rather than burning hours against a dead server. When
ready to schedule this:

```bash
dotnet run --project tests/Basil.LoadTests -- --profile Profiles/full.json
```

(exact invocation may need adjusting to the harness's current CLI — check `tests/Basil.LoadTests/README.md` or
the harness's `--help` output first). Expect several hours for the full stress-to-soak sequence. Watch for the
server process exiting or stopping responding to `/health`; if it does, capture the last ~2 minutes of
`Logs/latest.log`/`Logs/errors_latest.log` and the process's final `ThreadPoolQueueLength`/`ThreadCount` from
`resources.csv` before stopping the run.

## Final dependency inventory

Every third-party package referenced by a production (`src/`) project, and why it exists. Test-only tooling
(xUnit, NSubstitute, BenchmarkDotNet, coverlet) is omitted — its purpose is standard and not a project-specific
decision.

| Package                                                                                      | Why it exists                                                                                                                             |
|-----------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `Microsoft.Data.Sqlite`                                                                       | The database engine. Chosen for a single-process, self-contained tournament server with no separate DB service to install (see [`database.md`](database.md)). |
| `Dapper`                                                                                       | Thin SQL mapper over `Microsoft.Data.Sqlite`. Chosen deliberately over a full ORM so repositories keep explicit control of their SQL.       |
| `dbup-sqlite`                                                                                  | Runs the numbered schema migrations (`001_base.sql` onward) at startup, on both a fresh database and an upgrading one.                     |
| `Microsoft.Extensions.Caching.Memory`                                                          | Backs the read-through caching decorators (beatmap/beatmapset/user/settings repositories) and `BeatmapsetAssetCache`.                       |
| `BCrypt.Net-Next`                                                                              | Hashes the admin key and user passwords.                                                                                                    |
| `BouncyCastle.Cryptography`                                                                    | Rijndael-256 decryption of osu! stable's legacy score-submission encoding; no modern alternative implements this legacy scheme.             |
| `FFMpegCore`                                                                                   | Extracts and trims the 10-second audio preview clip (with fade-out) from a beatmapset's audio file.                                         |
| `SixLabors.ImageSharp` / `SixLabors.ImageSharp.Web`                                            | Beatmap background/thumbnail/menu-icon image processing, and the on-the-fly resize provider middleware ahead of routing.                    |
| `ppy.osu.Game.Rulesets.{Osu,Taiko,Catch,Mania}`                                                | Official osu! difficulty/star-rating calculators. Display-only (no pp-based gameplay); kept per an explicit user decision earlier in this effort rather than reimplemented. |
| `Microsoft.AspNetCore.OpenApi` / `Microsoft.OpenApi` / `Microsoft.Extensions.ApiDescription.Server` | Generates the OpenAPI schema for every host (`bancho`, `osuweb`, `beatmapassets`, `avatar`, `assets`, `basilapi`) at build time.        |
| `Scalar.AspNetCore`                                                                            | Serves the interactive API reference UI at `/docs/basil-api/`.                                                                             |
| `Serilog.AspNetCore` / `Serilog.Sinks.Console` / `Serilog.Sinks.File`                          | Structured logging, including the size-capped `latest.log`/`errors_latest.log` file sinks (see [`logging.md`](logging.md)).                |
| `Microsoft.Extensions.Http`                                                                    | `HttpClientFactory`, used by `HttpMirrorSearchClient` (the osu!direct search mirror client).                                               |
| `Microsoft.Extensions.Options` / `.ConfigurationExtensions` / `Configuration.Binder`           | The `IOptions<T>` configuration-binding pattern used throughout (`StorageOptions`, `MirrorOptions`, etc.).                                  |
| `Microsoft.Extensions.Hosting.Abstractions`                                                    | `BackgroundService` base class for `GhostDisconnectService`, `BeatmapWatcherService`, `BeatmapsetMigrationService`, `MatchRoundEndOutbox`.  |
| `Microsoft.Extensions.DependencyInjection.Abstractions` / `Logging.Abstractions`               | DI container and logger abstractions referenced from the Domain/Application layers, which cannot depend on the concrete Infrastructure/Web implementations. |

## See also

* [`database.md`](database.md): SQLite write model and configuration
* [`multiplayer.md`](multiplayer.md): match lifecycle and its invariants
* [`response-envelope.md`](response-envelope.md): the envelope middleware's internal invariant
* [`beatmap-ingestion.md`](beatmap-ingestion.md): the canonical `.osz` storage model
