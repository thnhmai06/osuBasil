# Design: Vertical Slice migration and consolidated refactor

Status: approved design, pending implementation plan
Date: 2026-09-07
Branch base: `chore/perf-investigation` (PR #7)

This document is the validated design for a single end-to-end migration of Basil from Clean
Architecture to Vertical Slices inside one monolith module, together with the refactor items the
2026 performance investigation surfaced. It exists so that the implementation plan that follows has
one settled target to work towards.

Spec location note: the brainstorming workflow's default is `docs/superpowers/specs/`. This
repository already keeps working documents in `plans/` (`perf-remaining-items-plan-20260906.md` and
peers), and `docs/` is reserved for authoritative topic documentation owned by `docs/index.md`. The
repository convention wins.

---

## 1. Scope

In scope:

* project restructure to three source projects with slices as the primary organizing unit
* separation of business logic from SSE broadcast
* match state-version ownership
* `MatchSession` and DTO encapsulation
* localization architecture, including command-owned help
* logging taxonomy and density
* the User slice: `SafeName`, `SilenceEnd`, search filters, mute API, username validation
* configuration source-of-truth policy
* test strategy, including the xunit v3 migration
* a Diagnostic API and live observability endpoints
* load-test streaming persistence and diagnostic correlation
* documentation updates, performed inside each phase rather than after them

Out of scope:

* rerunning the 12-24h soak. RC5 stays SUPPORTED. No item in this design is blocked on it.
* pp, friends, clans, a general-purpose public API, or any other item excluded by
  `docs/for-developers/working-scopes.md`
* changing bancho wire-format bytes. Every packet layout is a fixed contract here.

### 1.1 Scope honesty

The full scope is larger than 1-3 days at the fidelity this design implies: the migration alone
touches ~37.7k LOC of source and ~35.6k LOC of tests, the Diagnostic API adds eight categories with
snapshot, live, and action surfaces, and the load harness needs its persistence model rewritten.
The user has decided not to cut scope and to optimize for speed instead. The phase graph in
section 12 is therefore built to maximize parallelism and to keep the additive work (Diagnostics,
load harness) at the end of the critical path, where it can absorb overrun without leaving the
codebase in a half-migrated state.

---

## 2. Findings that changed the brief

These were verified against the code and, where a database claim was involved, against a live
SQLite instance. They are recorded because four of them contradict the task brief, and the
implementation must follow the evidence, not the brief.

### 2.1 `SilenceEnd` is load-bearing; keep it

The brief proposed removing it. It is used at:

* `src/Basil.Application/Services/Authentication/LoginService.cs:268` - writes the
  `ServerPacketWriter.SilenceEnd` packet on the wire at login
* `src/Basil.Application/Sessions/UserSession.cs:113` - `RemainingSilence`
* `SendPrivateMessageHandler` / `SendPublicMessageHandler` - the chat gate
* `src/Basil.Infrastructure/Persistence/Migrations/001_base.sql:17` - column
* `tests/Basil.Protocol.Tests/ServerPacketWriterTests.cs:475` - wire contract test

The brief's own condition ("remove if it no longer carries semantic value") is not met. It stays,
and instead gains a nullable representation and a management API (section 7).

### 2.2 Enum underlying types are already nearly done

Most enums already declare `: byte`, `: sbyte`, or `: uint`
(`GameMode`, `Country`, `SlotStatus`, `MatchTeamType`, `MatchEventType`, and every service result
enum). Remaining work: `ComparisonOperator` in
`src/Basil.Application/Abstractions/Beatmaps/BeatmapsetSearchFilters.cs:6`, and a verification pass
over `UserPrivileges`. This is a mechanical sweep folded into the slice that owns each enum, not a
phase.

### 2.3 The `LastChanged` trigger already exists, and is duplicated at runtime

`src/Basil.Infrastructure/Persistence/Migrations/001_base.sql:321` defines
`Settings_AdminKeyHash_AfterUpdate`, which stamps `AdminKey:LastChanged` whenever
`AdminKey:Hash` changes. `AdminKeyService.StampLastChangedAsync` writes the same value from
application code on both set and clear. `SqliteSettingsRepository.SetAsync` is a plain `UPDATE`, so
the trigger fires on both paths.

The database already owns the invariant. The work is to delete the redundant runtime stamp and the
two tests that assert it
(`AdminKeyServiceTests.SetKeyAsync_AlsoStampsLastChanged`,
`AdminKeyServiceTests.ClearAsync_AlsoStampsLastChanged`, both asserting
`Received(1).SetAsync("AdminKey:LastChanged", ...)`). Those two tests are the canonical example of
this repository's implementation-locking test problem, and the design uses them as such.

No other table carries a mutation-stamped timestamp. `Beatmapsets.LastUpdate` is beatmap metadata
from the `.osu` file, not a row-mutation stamp; `CreatedAt`, `LoggedInAt`, and `SubmittedAt` are
insert-only, as `001_base.sql:145` already documents.

### 2.4 SSE does not block the way the brief assumes

Subscriber handlers `TryWrite` into a bounded channel
(`src/Basil.Application/Services/BoundedSseChannel.cs`), which never blocks. The real cost on the
business path is at
`src/Basil.Application/Services/Multiplayer/MatchMembershipService.cs:672-689`:

```csharp
var mainSnapshot = await MatchLiveSnapshotBuilder.BuildMain(
    match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
if (match.MainSnapshot.Publish(mainSnapshot, version) is { } mainDelta)
    eventBus.PublishMain(match.DbId, mainDelta);

var settings = await MatchLiveSnapshotBuilder.BuildSettings(
    match, gameRegistry, ircRegistry, userRepo, beatmapRepo, cancellationToken);
// ... six more channels
```

Every match mutation performs two awaited snapshot builds - each hitting `userRepo` and
`beatmapRepo` - then serializes, diffs, and publishes across eight channels, unconditionally.
`IMatchLiveEvents` exposes `HasPlayerScoreSubscribers` and nothing equivalent for the other nine
streams, so there is no way to skip the work when nobody is listening.

This reframes the refactor: the fix is a subscriber-aware hub that does not build what nobody
reads, not a per-client queue system. Per-client queues already exist.

### 2.5 Environment variables can silently override application settings

`Program.Main:142` calls `WebApplication.CreateBuilder(args)`, which registers the environment
variable provider by default. `ConfigureConfiguration` (`Program.cs:359`) then layers JSON and
command line *on top of* that default set, but never removes it, so `Basil__Server__Port` overrides
`Data/appsettings.json` with nothing in the codebase referencing an environment variable.

### 2.6 Localization does not cover a large share of user-facing text

`Data/Localization/BasilBot.json` holds 122 entries across 15 categories. Outside it:

* 47 string literals in `src/Basil.Application/Services/Multiplayer/*.cs`, including
  `MatchControlService.cs:841` (`"Good luck, have fun!"`), `MatchControlService.cs:948`
  (`"Match aborted."`), and `MatchMembershipService.cs:758`
* `CommandDispatcher.ChatCommands` (`CommandDispatcher.cs:39`) - the entire `!help` output
* `MpCommandService.HelpText` - the entire `!mp help` output
* the five `ValidateUsername` messages in `src/Basil.Domain/Users/User.cs:64-69`
* API error text

Answering the brief's question ("can the whole system be switched to another locale?") with "yes"
requires all of these inside the localization boundary, and requires a test that can prove
coverage.

---

## 3. Target architecture

### 3.1 Projects

```
src/Basil.Domain/      unchanged - pure domain types and calculations
src/Basil.Protocol/    unchanged - bancho wire encoding and decoding
src/Basil.Server/      Microsoft.NET.Sdk.Web - host, features, shared infrastructure
```

`Basil.Application`, `Basil.Infrastructure`, and `Basil.Web` merge into `Basil.Server`.

Rationale: a vertical slice owns its endpoint, its handler, and its persistence. The current split
forces `IMatchRepository` into `Application/Abstractions` and `SqliteMatchRepository` into
`Infrastructure`, with exactly one implementation of each, purely because of the layer boundary.
Those are the abstractions the brief describes as existing only to serve the old architecture.

`Basil.Domain` and `Basil.Protocol` stay separate because they are genuine shared kernels with zero
project references, and keeping them gives the architecture tests something real to enforce.

### 3.2 Internal layout

```
src/Basil.Server/
  Host/
    Bootstrap.cs            entry point and pipeline order
    SerilogSetup.cs
    KestrelSetup.cs
    OpenApiSetup.cs
    CorsSetup.cs
    ImageSharpSetup.cs
    StartupData.cs
    StartupBanner.cs
  Features/
    Multiplayer/
    Chat/
    Bot/
    Irc/
    Users/
    Auth/
    Beatmaps/
    Scores/
    Spectating/
    Content/
    Diagnostics/
  Shared/
    Eventing/               LiveEventHub, SnapshotChannel, BoundedSseChannel, SequenceGate
    Sessions/               GameSession, IrcSession, registries, channel registry
    Persistence/            SqliteConnectionFactory, migrations, instrumentation
    Localization/           locale loader, key resolution, fragment merging
    Logging/                category enricher, log-level policy helpers
    Http/                   envelope, middleware, OpenApi transformers, pagination,
                            route constraints, content types
    Configuration/          options types and the configuration source chain
    Media/                  asset providers, ImageSharp resolvers, audio extraction
    Storage/                replay storage, response cache, hard links
```

Each `Features/<Slice>/` folder contains that slice's endpoints, handlers, packet handlers,
repositories and their SQL, background services, localization fragment, help text, and reply
constants.

`Program.cs` is currently 816 lines and is the clearest instance of the god-file problem the brief
names. It splits by responsibility as above. Where a file needs internal sectioning, `#region` is
used rather than long comment dividers.

### 3.3 Architecture rules

The existing `tests/Basil.ArchitectureTests` enforces the old direction
(`Application` must not depend on `Infrastructure`, and so on). Those assertions become false the
moment the projects merge, so they are replaced in Phase 0 rather than deleted or fought.

New rules, enforced on namespaces with NetArchTest:

1. `Basil.Domain` depends on no Basil project. (kept)
2. `Basil.Protocol` depends on no Basil project. (kept)
3. `Basil.Domain` and `Basil.Protocol` depend on no web, ORM, or SQLite assembly. (kept)
4. `Basil.Server.Shared.*` must not depend on `Basil.Server.Features.*`.
5. `Basil.Server.Features.X` must not depend on `Basil.Server.Features.Y` unless the edge `X -> Y`
   is declared in the adjacency allowlist.
6. `Basil.Server.Host` may depend on anything. It is the composition root.

Rule 5 is the important one, and it is deliberately not "slices may never reference each other".
That rule would fail immediately and honestly: Multiplayer resolves usernames through
`IUserRepository` and `UserBriefResolver`; Scores resolves beatmaps by md5; Chat needs session
registries. Banning the edges is unenforceable, and allowing all of them makes the rule
meaningless. The allowlist makes every cross-slice edge a declared, reviewable decision, and makes
an undeclared new edge a build failure.

The initial allowlist is derived from the real dependencies found during migration, and is
minimized during Phase 1-4 by moving genuinely shared reads into `Shared` where that is the honest
answer.

---

## 4. Cross-cutting designs

### 4.1 LiveEventHub

Problem: section 2.4.

Design:

```
mutation completes
      │
      ▼
hub.Publish(key, stream, version, buildFn)
      │
      ├── no subscriber for (key, stream)  ──► buildFn is never invoked
      │                                        (no snapshot build, no repository call,
      │                                         no serialization, no diff)
      │
      └── subscribers present
              │
              ▼
          buildFn() once ──► immutable event
              │
              ▼
          broadcast: TryWrite into each subscriber's bounded channel
```

Key points:

* the hub is keyed by `(key, stream)`, where `key` is the match database id for match streams and a
  category name for diagnostic streams. `Mediator`-style type-only routing cannot express this, and
  the per-key dimension is precisely what makes the zero-subscriber short-circuit possible.
* `buildFn` is a delegate, not a prebuilt value. This is what removes the repository calls from the
  business path when nobody is listening.
* the event handed to broadcast is immutable and built exactly once, regardless of subscriber
  count.
* the existing per-subscriber bounded channel with its gap marker
  (`BoundedSseChannel.WriteWithGapMarker`) is kept as-is. It already implements the per-client
  buffering the brief asks about, and a slow client already cannot stall the producer.
* `SnapshotChannel<T>` keeps its sequence gate and merge-patch diffing; it moves under
  `Shared/Eventing` and is invoked from inside `buildFn`.
* `IMatchLiveEvents`'s twenty `Subscribe*`/`Publish*` methods collapse into the hub's typed stream
  keys.

The same hub backs the Diagnostic SSE endpoints (section 10), so diagnostic collection is decoupled
from client writes by construction rather than by a second mechanism.

### 4.2 Match mutation scope and version ownership

Today, every caller must remember to allocate `match.NextStateVersion()` inside the lock and then
thread that value through every publish call. `MatchReadyHandler` is representative:

```csharp
await match.Lock.WaitAsync(cancellationToken);
long version;
try
{
    var slot = match.GetSlot(gameSession.Id);
    if (slot is null) return;
    slot.Status = SlotStatus.Ready;
    version = match.NextStateVersion();
}
finally { match.Lock.Release(); }

await matchMembership.EnqueueStateAsync(match, version, false, cancellationToken);
```

Replaced by a scope that owns the whole sequence:

```csharp
await using var mutation = await match.BeginMutationAsync(cancellationToken);
var slot = mutation.Session.GetSlot(gameSession.Id);
if (slot is null) return;
slot.Status = SlotStatus.Ready;
mutation.PublishState(lobby: false);
```

Disposal releases the lock, allocates the version, and runs the queued publishes unlocked - which
preserves the ADR-004 4b property that building and broadcasting happen outside the lock, gated by
version. The caller cannot forget to increment, cannot pass a stale version, and cannot hold the
lock across the broadcast.

The invariants that ADR-004 documents are inlined as comments at the scope type itself, so the code
explains itself without depending on the ADR file still existing at that path. ADR-004 remains as
the rationale document.

### 4.3 Localization

Key namespace mirrors command syntax:

```
General.*                    not command-related
Users.Validation.*           username and user-input validation messages
Commands.Help.*              !help
Commands.Mp.*                !mp itself
Commands.Mp.In.*             !mp in
Commands.Mp.Start.*          !mp start
Multiplayer.Announce.*       room announcements not tied to a command
Irc.*                        IRC numerics and replies
```

Rules:

* each slice ships its own locale fragment file; the loader merges fragments into one resolved
  locale at startup. A slice owning its text is what keeps localization cohesive with the slice.
* help text is a localized resource owned by the command it documents. `!help` composes its output
  from the commands that register themselves, and `!mp help` from the `!mp` subcommands. Neither
  reads a central hardcoded array.
* a missing key is a startup failure, matching the current `ReplyLocale` behavior, so an incomplete
  translation cannot be discovered mid-request.

Coverage gate: a test enumerates every reply constant and every resource key referenced in code,
and asserts the active locale covers all of them with no orphan keys in either direction. This is
what turns "the whole system can be switched to a new locale" from an aspiration into a CI-enforced
property.

Migration targets are listed in section 2.6.

### 4.4 Logging

Current density: 216 call sites, of which 101 are `LogDebug` (46 Application, 43 Infrastructure, 12
Web). `ApiRequestLoggingMiddleware` logs every request.

Taxonomy:

| Level | Meaning |
| --- | --- |
| Trace | not used |
| Debug | developer diagnostics; not present in the production default level |
| Information | a state change an operator would want during a postmortem: login, match created or closed, migration applied, admin key rotated, server lifecycle |
| Warning | degraded but handled: a mirror timed out, a beatmap failed to resolve, a diagnostic collector could not attach |
| Error | an operation or request failed |
| Critical | the process cannot continue |

Rules:

* per-request and per-packet volume goes to metrics, not to logs. `BasilMetrics` already carries
  request duration; the middleware's per-request Information log is the single largest source of
  log volume and is dropped in favor of the metric plus Warning-and-above on failures.
* logging happens at the slice boundary - the handler - not inside helpers called from it. A helper
  that logs makes its callers' log output unpredictable.
* the log level answers "who needs to see this, and when", not "how interesting is this".
* per the existing test policy, log messages are not assertion targets. Section 9 removes the tests
  that currently assert them.

The existing curated-category filter (`CategoryEnricher` plus the `Filter.ByExcluding` on the
fallback category in `Program.ConfigureSerilog`) is kept; it is a working mechanism, and the
categories map cleanly onto slices.

### 4.5 Configuration policy

Target:

```
Application settings ──► Data/appsettings.json ──► Application configuration
```

Change: after `WebApplication.CreateBuilder(args)` returns, clear the inherited provider set and
re-add only the intended sources.

```csharp
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile(Path.Combine("Data", "appsettings.json"), optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine("Data", $"appsettings.{builder.Environment.EnvironmentName}.json"),
        optional: true, reloadOnChange: true)
    .AddCommandLine(args);
```

Ordering constraint, which is the discriminating detail for this item: `builder.Environment`
resolves `EnvironmentName` from `ASPNETCORE_ENVIRONMENT` while the builder is being constructed,
before this code runs. Clearing the source list afterwards therefore does not disturb it. That is
host configuration, which the brief explicitly permits, and `docker-compose.yml:14` depends on it.

Verification: a test that sets `Basil__Server__Port` in the process environment, builds the
configuration the same way the host does, and asserts the bound `ServerOptions.Port` reflects
`appsettings.json` rather than the variable. Documentation in
`docs/for-technicians/configuration.md` is updated in the same phase.

---

## 5. `MatchSession` and model encapsulation

`MatchSession` is 505 lines with a 14-parameter primary constructor and around 30 mutable
properties. The redesign groups data by the concept it actually represents, rather than by
mechanically extracting classes.

### 5.1 Selected beatmap

Today: `MapId` (`int?`), `MapMd5` (`string`), `MapName` (`string`), plus `PrevMapId` and
`UnresolvedMapMd5`. "No beatmap selected" is currently expressed as a combination across three
fields.

```csharp
public sealed record SelectedBeatmap(int Id, string Md5, string Name);

// on the session
public SelectedBeatmap? Map { get; set; }        // null means no beatmap is selected
public SelectedBeatmap? PreviousMap { get; set; }
public string? UnresolvedMd5 { get; set; }       // stays separate: it is a warning-dedup concern,
                                                 // not part of the selection itself
```

`UnresolvedMapMd5` deliberately stays a separate field. Its documented purpose is deduplicating the
"beatmap not found locally" warning across the client's repeated settings snapshots; folding it
into the selection would conflate "what is selected" with "what we last failed to resolve".

The wire mapping in `MatchPacketDataMapper` translates `null` to the same byte layout the current
three-field default produces. Packet bytes do not change.

### 5.2 Countdown timer

Today: `PendingTimer` (`CancellationTokenSource?`), `PendingTimerIsAutoStart` (`bool`),
`TimerStartedAt` (`DateTimeOffset?`), `TimerTotalSeconds` (`int?`), all set and cleared together,
by convention, at several call sites.

```csharp
public sealed class MatchCountdown(
    CancellationTokenSource cancellation,
    DateTimeOffset startedAt,
    int totalSeconds,
    bool startsMatch);

public MatchCountdown? Countdown { get; set; }   // null means no countdown is running
```

Four fields that must be mutated as a unit become one nullable reference that cannot be half-set.
This directly addresses the timer-related state the soak investigation had to reason about.

### 5.3 Empty-room close state

`EmptyRoomTimer` and `EmptyRoomWarningSent` follow the same pattern and become one nullable value.
The 15-minute empty-room timer gains jitter here, which is the one concrete change this design
takes from the unconfirmed soak hypothesis - it is cheap, it is correct regardless of whether the
hypothesis holds, and it removes a mechanism that can synchronize cleanup bursts across rooms
created at the same time.

### 5.4 Authority

`Referees`, `BannedIds`, `InvitedIds`, `TourneyClients`, `CreatorId`, `HostId`, plus
`IsReferee`/`IsCreator`/`HasGameplayHost`. These are one concept - who may do what in this room -
and move into a dedicated type owned by the session. The distinction between referee, host, and
creator that `docs/for-developers/multiplayer.md` documents is preserved exactly; the type makes it
harder to conflate them, which is the invariant the brief calls out.

### 5.5 `ScoreDetailView` and other DTOs

`ScoreDetailView` groups that represent one concept each: hit counts and accuracy, combo, grade and
mods. `HitCounts` already exists in `Basil.Domain/Scores/HitCounts.cs` and is the natural home for
the count group.

The constraint on all of this: a group is extracted only when it is read or written as a unit and
the resulting type is easier to use than the flattened form. Serialization shape changes to API
responses are contract changes and are documented as such in the same phase, with the OpenAPI
schema regenerated and `docs/for-client/response-envelope.md` checked for impact. Where a nested
shape would break a client for no gain, the DTO keeps its flat serialization via explicit mapping
and only the internal model is grouped.

---

## 6. God-file decomposition

| File | Lines | Members | Split |
| --- | --- | --- | --- |
| `Program.cs` | 816 | - | `Host/` by responsibility (section 3.2) |
| `MpCommandService.cs` | 1424 | 44 | one handler per `!mp` subcommand, each owning its help text and locale keys |
| `MatchSubResourceRoutes.cs` | 1423 | 33 | one endpoint file per sub-resource: chat, hosts, refs, bans, slots, timer, abort, close |
| `MatchControlService.cs` | 1303 | 43 | one handler per operation, grouped by concern: settings, slots, authority, countdown, lifecycle |
| `MatchMembershipService.cs` | 903 | 27 | membership (join, leave, occupy), lifecycle (create, close, teardown), broadcast (the `Enqueue*`/`Publish*` family moves onto the hub) |
| `MatchLiveSnapshotBuilder.cs` | 630 | - | stays cohesive; becomes the `buildFn` supplier for the hub |

The rule that prevents new dumping grounds: a slice's shared helpers live in that slice, and
anything promoted to `Shared` must be used by at least two slices and must pass the architecture
rule in section 3.3.

---

## 7. Users slice

This slice's four items touch the same files - `User`, `IUserRepository`, `SqliteUserRepository`,
`UserRoutes`, and the user test suite - so they land in one commit. Splitting them would rewrite
`SqliteUserRepository` and the user tests four times.

### 7.1 `SafeName` as a stored generated column

Current rule (`src/Basil.Domain/Users/User.cs:46`):

```csharp
public static string MakeSafeName(string name) => name.ToLowerInvariant().Replace(' ', '_');
```

SQLite equivalent: `replace(lower(Name), ' ', '_')`.

Verified against Microsoft.Data.Sqlite 10.0.11, which bundles **SQLite 3.53.3**:

| Check | Result |
| --- | --- |
| `STORED` generated column with an inline `UNIQUE` constraint | works |
| `INSERT` that lists `SafeName` | fails: `cannot INSERT into generated column "SafeName"` |
| `INSERT` that omits it | works, value generated |
| `UPDATE Name` | regenerates: `'Renamed Guy'` becomes `'renamed_guy'` |
| inserting `'a b'` when `'A B'` exists | fails: `UNIQUE constraint failed: Users.SafeName` |
| query plan for `SafeName = ?` | `SEARCH Users USING INDEX sqlite_autoindex_Users_2 (SafeName=?)` |
| query plan for `SafeName LIKE ?` | `SCAN Users`, identical to today - a leading wildcard was never indexable |

Character-level equivalence over the real charset:

| Input | `MakeSafeName` | SQLite |
| --- | --- | --- |
| `Peppy` | `peppy` | `peppy` |
| `pe ppy` | `pe_ppy` | `pe_ppy` |
| `PE_PPY` | `pe_ppy` | `pe_ppy` |
| `AB-CD` | `ab-cd` | `ab-cd` |
| `[Box] x` | `[box]_x` | `[box]_x` |
| `Z9 _-[]` | `z9__-[]` | `z9__-[]` |

The single divergence is non-ASCII: SQLite's `lower()` is ASCII-only, while `ToLowerInvariant()` is
not (`lower('ÄÖÜ İ')` returns the input unchanged in SQLite, and `'äöü İ'` in .NET). That input is
unreachable because `ValidateUsername` restricts usernames to ASCII (section 7.4). The design
therefore converts, and makes the ASCII restriction an explicit, separately-tested invariant rather
than an implicit one.

`User.MakeSafeName` stays in `Basil.Domain`. It is still required for in-memory indices:
`GameSessionRegistry._bySafeName`, `IrcSessionRegistry._bySafeName`, and
`CachingUserRepository`'s name cache key. Removing manual synchronization applies to persistence,
not to the rule itself.

Guard for the rule now living in two places: a contract test that runs both implementations over
the full allowed character set plus boundary cases and asserts equality, plus the validation test
in section 7.4.

Migration `006`, a table rebuild - which also flips `SilenceEnd` to nullable, so the table is
rebuilt once rather than twice:

```sql
create table Users_new
(
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Name       varchar(32) not null,
    SafeName   varchar(32) generated always as (replace(lower(Name), ' ', '_')) stored,
    Privilege  int      default 1    not null,
    PwBcrypt   char(60)              not null,
    Country    char(2)  default 'xx' not null,
    SilenceEnd datetime null,
    DeletedAt  datetime null,
    constraint Users_Name_uindex unique (Name),
    constraint Users_SafeName_uindex unique (SafeName)
);

insert into Users_new (Id, Name, Privilege, PwBcrypt, Country, SilenceEnd, DeletedAt)
select Id, Name, Privilege, PwBcrypt, Country,
       case when SilenceEnd <= '1970-01-01 00:00:00' then null else SilenceEnd end,
       DeletedAt
from Users;

drop table Users;
alter table Users_new rename to Users;
create index Users_Privilege_index on Users (Privilege);
```

Notes on the rebuild:

* SQLite 3.53.3 does accept `ALTER TABLE ... ADD COLUMN ... STORED`, contrary to older
  documentation, but it does not help here: the existing `SafeName` is a real column carrying a
  `UNIQUE` index, and `DROP COLUMN` refuses an indexed column. A rebuild is required either way.
* only one foreign key references `Users` (`UserStats_Users_Id_fk`, `001_base.sql:38`), and
  `PRAGMA foreign_keys` is never enabled - `SqliteConnectionFactory` sets only `busy_timeout` and
  `synchronous`. The script still brackets the rebuild with the standard `foreign_keys` guard.

Call sites that change:

* `IUserRepository.UpdateNameAsync(id, name, safeName)` drops its `safeName` parameter
* `SqliteUserRepository.cs:85` (`UPDATE`) and `:109` (`INSERT`) drop the `SafeName` column
* `001_base.sql:333`, the BasilBot seed insert, drops `SafeName`
* `BotBootstrapService.cs:55`, `UserRoutes.cs:159`, `UserRoutes.cs:197` stop computing it
* `SqliteScoreRepositoryTests.cs:25` and `:170` fixtures stop inserting it
* `UserRow.SafeName` is unaffected; `SELECT *` still reads the generated value

### 7.2 Nullable `SilenceEnd`

| State | Representation |
| --- | --- |
| never silenced | `NULL` |
| currently silenced | `SilenceEnd > now` |
| silence expired | `SilenceEnd <= now` |

* `User.SilenceEnd` becomes `DateTimeOffset?`
* `UserSession.SilenceEnd` becomes nullable; `Silenced` is `SilenceEnd > UtcNow`;
  `RemainingSilence` returns `TimeSpan.Zero` when null
* the wire is unchanged: `ServerPacketWriter.SilenceEnd(int delta)` still receives
  `(int)RemainingSilence.TotalSeconds`, which is `0` for an unsilenced user exactly as the epoch
  default produced. `ServerPacketWriterTests.cs:475` is untouched.
* `UserView.SilenceEnd` becomes nullable. This is an API contract change, made deliberately, with
  the OpenAPI schema and documentation updated in the same phase.

### 7.3 User search

```csharp
public sealed record UserSearchFilters(
    string? Keywords = null,
    IReadOnlyList<Country>? Countries = null,
    UserPrivileges? Privilege = null,   // "has this privilege"
    bool? Silenced = null,              // sensitive; null means no filter
    bool IncludeDeleted = false);       // sensitive
```

| Filter | SQL |
| --- | --- |
| `Privilege` | `(Privilege & @Privilege) = @Privilege` |
| `Silenced = true` | `SilenceEnd > datetime('now')` |
| `Silenced = false` | `SilenceEnd IS NULL OR SilenceEnd <= datetime('now')` |
| `Silenced = null` | no condition |
| `IncludeDeleted = false` | `DeletedAt IS NULL` |
| `IncludeDeleted = true` | no condition |

The privilege SQL is already correct at `SqliteUserRepository.cs:196-199`; only the parameter's
type changes from `ushort?` to the enum, and the parser accepts flag names as well as a number.
`DeletedAt IS NULL` is currently hardcoded into the where-clause builder and becomes conditional.

Authorization: `GET /users/search` is registered on the public group
(`UserRoutes.cs:67`), unlike the admin-only listing at `:47`. `Silenced` and `IncludeDeleted`
require a valid admin key.

* unauthenticated caller supplying either filter: `401`
* authenticated non-admin caller supplying either filter: `403`
* both reuse the response shape that `RequireAuthorization(AdminKeyDefaults.Policy)` already
  produces, so the contract stays uniform across the API
* the filter is never silently dropped, and results are never silently unfiltered

Parser change: `UserSearchQueryParser` currently returns any unrecognized or unparseable token to
the free-text portion rather than erroring. That is acceptable for `country` and `privilege`, where
the fallback is a harmless keyword search. It is not acceptable for the sensitive filters: a
mistyped `silenced=yse` would silently apply no filter. Sensitive keys therefore parse strictly,
and a bad value returns `400`.

### 7.4 Username validation

Two changes to `User.ValidateUsername`:

1. a distinct non-ASCII error, placed before the character-set check so the reported cause is
   precise. This is also the formal guard for section 7.1's SQLite equivalence.
2. the regex is dropped. The rule is a fixed character set, which `SearchValues<char>` expresses
   directly, faster, with no source generator - and it lets the `User` record stop being `partial`.

```csharp
private static readonly SearchValues<char> AllowedUsernameCharacters = SearchValues.Create(
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-[] ");

// ...
else if (!Ascii.IsValid(name))
    error = /* Users.Validation.NonAscii */;
else if (name.AsSpan().ContainsAnyExcept(AllowedUsernameCharacters))
    error = /* Users.Validation.Charset */;
```

All five validation messages move into the localization system under `Users.Validation.*`.

### 7.5 Mute API

```
GET /users/{userId}/mute
PUT /users/{userId}/mute
```

Both on the admin group, `RequireAuthorization(AdminKeyDefaults.Policy)`, matching the existing
admin user routes.

Request body for `PUT`: `{ "until": <ISO 8601 timestamp | null> }`.

Only an absolute timestamp is accepted. A parallel `durationSeconds` form is deliberately not
offered: two ways to express the same thing is an ambiguity source, and only the absolute form
makes `PUT` genuinely idempotent - a retried request cannot push the expiry further out. `null`
lifts the mute. A timestamp in the past returns `400` rather than being silently treated as an
unmute.

Response, shared by both verbs:

```csharp
public sealed record MuteView(bool IsMuted, DateTimeOffset? MutedUntil);
```

`IsMuted` is not derivable from `MutedUntil` alone without the caller also knowing the server's
clock, which is why it is present. A remaining-seconds field is not: it is derivable from
`MutedUntil`, and it is stale the moment the response leaves the server.

The handler is not a thin database write. `SilenceEnd` is copied onto the session at login
(`LoginService.cs:204`, `IrcAuthenticationService.cs:80`), so a database-only write would not take
effect until the user reconnects. The handler must:

1. persist through `IUserRepository.UpdateSilenceEndAsync(id, DateTimeOffset?)`
2. update the user's live `GameSession` and `IrcSession` through the session registries - after
   which the chat gate in `SendPublicMessageHandler` and `SendPrivateMessageHandler` is correct
   automatically, because it reads `UserSession.Silenced`
3. send `ServerPacketWriter.SilenceEnd(remainingSeconds)` to the online game session, so the client
   reflects the change immediately - the same packet login already sends
4. invalidate `CachingUserRepository`'s entries for that user, under both the id key and the name
   key

---

## 8. Documentation

Documentation updates happen inside the phase that changes the behavior, never in a trailing phase.
A trailing documentation phase would re-open every area the migration just closed, which is exactly
the double-touch the brief asks to avoid.

Affected authoritative documents: `architecture.md`, `multiplayer.md`, `sse.md`, `testing.md`,
`logging.md`, `database.md`, `privileges.md`, `configuration.md`, `load-testing.md`,
`known-limitations.md`, and `docs/index.md`.

Code comment policy:

* a comment that a reader needs in order to understand the code states the behavior, invariant,
  reason, or constraint at that location. `// per ADR-004` on its own is replaced by the invariant
  itself, with the ADR kept as the rationale document and referenced only as further reading.
* ADR-003, ADR-004, ADR-005, and ADR-007 stay. ADR-004 gains an addendum describing the hub.
* XML documentation uses the tag that matches the kind of information: `summary` for what the
  member is and does, `param` and `returns` for the meaning of values, `exception` for what callers
  must handle, `remarks` for caller-visible behavior that does not fit the summary. Several current
  `<summary>` blocks carry three kinds of information at once and are split accordingly.
* API route documentation continues to follow the Implementation Test in `CLAUDE.md` rule 5.

---

## 9. Testing

Current state: 1422 `[Fact]`/`[Theory]` attributes across six projects, 61 files using NSubstitute,
48 `.Received()` assertions.

### 9.1 xunit v3

`xunit 2.9.3` migrates to `xunit.v3` as part of the test restructure, using the
`migrate-xunit-to-xunit-v3` skill. Because every test project is being restructured anyway, doing
the version migration in the same pass avoids touching each test file twice.

### 9.2 Project layout

```
tests/Basil.Domain.Tests/        kept as-is in scope
tests/Basil.Protocol.Tests/      kept as-is in scope - wire contract tests, highest value per line
tests/Basil.Server.Tests/        Features/<Slice>/ + Shared/<Concern>/
tests/Basil.IntegrationTests/    kept - end-to-end HTTP, envelope, and auth contracts
tests/Basil.ArchitectureTests/   rewritten in Phase 0 (section 3.3)
tests/Basil.LoadTests/           reworked in the load-harness phase
```

`Basil.Application.Tests` (676) and `Basil.Infrastructure.Tests` (245) merge into
`Basil.Server.Tests`, mirroring the slice layout.

### 9.3 Triage

Each slice's tests are triaged in the same commit that refactors the slice - keep, merge, rewrite
against the contract, or delete. The classification criterion is the Implementation Test already in
`CLAUDE.md` rule 8: if the implementation changed but observable behavior did not, would this test
still pass?

Contracts worth pinning: packet bytes, IRC wire text and numerics, HTTP status codes, envelope and
JSON shapes, API error codes, user-visible reply constants, multiplayer state transitions,
localization coverage, and the architecture rules.

Not pinned: internal call counts and ordering, mock interaction verification where the observable
outcome is already asserted, log message text, and internal diagnostic strings.

Named starting targets:

* the 48 `.Received()` assertions, each evaluated against the criterion above
* `AdminKeyServiceTests.SetKeyAsync_AlsoStampsLastChanged` and `ClearAsync_AlsoStampsLastChanged` -
  deleted, since the runtime stamp they assert is being removed as redundant with the database
  trigger. Replaced by one test asserting the observable contract: rotating the key changes the
  value returned by `GET /settings/admin-key`'s `LastChanged`.

The slice boundary is what makes this reduction safe: a slice's public surface (its endpoints, its
packets, its commands) is a real seam to test through, so tests no longer need to reach into
internals to get coverage.

The target is not a specific test count. The target is that a refactor which preserves behavior
does not break tests, and that a change which breaks behavior does.

---

## 10. Diagnostic API

Protected by the existing admin key policy. No new authentication mechanism.

The API reads current state and invokes supported runtime operations. It does not store diagnostic
state, keep history, maintain ring buffers, produce reports, generate dumps, or manage artifact
lifecycles.

### 10.1 Categories

Derived from what the investigation actually needed - the 7 `BasilMetrics` instruments and the 15
fields of `ResourceSample` - rather than from an invented list.

| Category | Content |
| --- | --- |
| `process` | process id, uptime, working set, private memory, virtual memory, peak working set, CPU usage, thread count, handle count |
| `memory` | managed memory, GC heap size, fragmented bytes, memory load, available memory, high-memory-load threshold, working set, private memory |
| `gc` | gen0/1/2 collection counts, generation sizes, LOH, POH, fragmentation, GC mode, latency mode, time in GC |
| `threadpool` | thread count, worker and completion-port availability and limits, queue length, completed work items, contention count |
| `runtime` | runtime version, server GC, concurrent GC, processor count, architecture, operating system - static, snapshot only |
| `exceptions` | total thrown count, unhandled count, rate |
| `http` | active requests, request rate, failed request rate, request duration, active connections, connection rate |
| `application` | active users, active sessions, active matches, active SSE subscribers by stream, active timers, eventing statistics |

`application` carries only Basil-specific semantics; no runtime metric is filed there.

Memory naming is explicit throughout - no field named `MemoryUsage`. Process memory, managed
memory, GC heap, and the native remainder are separately named so the relationship the soak
incident needed can actually be read off the data.

### 10.2 Surfaces

```
GET  /diagnostic/{category}              snapshot
GET  /diagnostic/{category}/live         SSE, one full snapshot of that category per second
GET  /diagnostic/live                    curated overview, SSE
POST /diagnostic/{category}/{action}     direct runtime operation
```

* every SSE endpoint uses the `/live` suffix, matching the existing convention that
  `LiveSseRoutes.IsLiveRoute` already encodes.
* live payloads carry the full current snapshot of that category, not a delta, so a client holds no
  reconstruction state. This is not the whole server's state: `runtime` is static and is excluded
  from every live payload.
* `/diagnostic/live` is a curated selection - process CPU, working set, handle count; managed
  memory, heap size, fragmentation; collection counts; threadpool thread count and queue depth;
  active sessions, matches, and SSE connections - not a merge of all categories.
* actions invoke a supported operation and return its result. No background jobs, no stored
  results, no generated artifacts, no dumps, no profiler sessions. Candidate actions are limited to
  what the runtime genuinely exposes as an operation, such as a collection or an LOH compaction
  followed by a collection; configuration knobs are not actions.

### 10.3 Cost

The diagnostic subsystem must not become a load source on the system it observes. A one-second live
interval does not mean recomputing every metric with an expensive call each second: cheap counters
are read on demand, expensive ones are sampled at a rate matched to their cost and their rate of
change, and collection is shared across subscribers through the hub in section 4.1 so N subscribers
cost one collection.

Collection is decoupled from client writes by the same hub, so a slow diagnostic consumer cannot
slow collection or the business path.

---

## 11. Load harness

Problem: `tests/Basil.LoadTests/Program.cs:250-252` writes `run.json`, `resources.csv`, and
`summary.md` only after the run completes. `ResourceTimeline` accumulates samples in a `List` in
memory. A crash at 13h42m of a 24h run loses everything.

Design:

* each sample is appended to disk the moment it is taken, in an append-friendly, crash-tolerant
  line format (one JSON object per line), flushed per record
* separate logical streams so the data kinds stay distinguishable and correlatable by timestamp:
  load metrics timeline, resource timeline, server diagnostic timeline, significant events
* the harness polls the Diagnostic API on its own cadence and appends the results into the server
  diagnostic stream, producing one unified timeline across load, runtime, and application state
* the final summary becomes an aggregation over what was already persisted, not the only moment
  anything is written

Result: a crash at any point leaves every sample taken up to that point on disk, and correlating a
memory spike against match cleanup, GC activity, and health-check latency no longer requires
manually stitching `Get-Process` output, server logs, health checks, and NBomber reports after the
fact.

---

## 12. Phases and dependencies

```
Phase 0  Foundation                              (sequential, blocks everything)
   │
   ├───────────────────────────────┐
   ▼                               ▼
Phase 1  Multiplayer            Phase 5  Diagnostics API
   │     (largest; settles              (purely additive; needs only the hub)
   │      hub/localization/                     │
   │      logging patterns)                     ▼
   │                              Phase 6  Load harness
   ├──────────┬──────────┐
   ▼          ▼          ▼
Phase 2    Phase 3    Phase 4
Chat/Bot/  Users      Beatmaps/
Irc                   Scores/Content
   └──────────┴──────────┴──────────────────────┘
                        ▼
              Phase 7  Final sweep
```

### 12.1 Execution model

"Parallel" here means the phases touch disjoint files and carry no ordering dependency, so they can
be worked without waiting on each other. Concretely:

* Phase 5 runs alongside Phase 1 because it only creates files in a new slice.
* Phases 2, 3, and 4 are file-disjoint once Phase 1 has settled the shared patterns.
* Where two streams run at once, the second runs in a git worktree so neither blocks the other's
  build and test cycle.

Subagent use is deliberate and bounded, since this runs on Claude Pro. Two investigation units are
genuinely worth delegating and are the only ones planned: the diagnostic metric inventory (which
runtime metrics exist, what each costs to read, and which are worth exposing), and the test triage
classification pass over 1422 tests. Architectural reasoning stays with the advisor checkpoints
rather than being fanned out. No subagent is spawned merely to parallelize a small task.

### Phase 0 - Foundation

Goal: the structural and mechanical prerequisites every later phase depends on, and nothing else.

* create `Basil.Server`; move every file from `Basil.Application`, `Basil.Infrastructure`, and
  `Basil.Web` directly into its final slice or `Shared` folder, with namespaces rewritten. This is
  one atomic mechanical move with no semantic change - a partial move would leave circular
  assembly references, so it cannot be done slice by slice.
* split `Program.cs` into `Host/`
* rewrite `Basil.ArchitectureTests` for the new rules, including the cross-slice adjacency
  allowlist
* configuration source chain fix and its test
* introduce `Shared/Eventing`'s `LiveEventHub` abstraction; no slice adopts it yet
* introduce the localization loader and key-namespace mechanism; no content migrated yet
* introduce the logging taxonomy as a documented policy; no call sites changed yet
* xunit v3 migration and test project restructure, with tests otherwise unchanged

Verification: full solution builds in Release; the complete test suite passes with the same results
as before the move; architecture tests pass against the new rules; the configuration test proves
`Basil__Server__Port` no longer overrides.

Regression risk: high volume, low semantic risk. The mitigation is that the move is mechanical and
the test suite is the oracle.

Advisor checkpoint: is this a real restructure or a folder move? Are the slice boundaries real? Is
the adjacency allowlist small and honest, or is it papering over tangles?

### Phase 1 - Multiplayer

The largest phase, and the one that establishes the patterns Phases 2-4 follow. Everything in this
list lands together, because they touch the same files:

* `MatchSession` model encapsulation (section 5), including empty-room timer jitter
* mutation scope and version ownership (section 4.2)
* hub adoption with the zero-subscriber short-circuit (section 4.1); `IMatchLiveEvents` collapsed
* god-file decomposition (section 6)
* localization of the 47 hardcoded strings, plus `!mp` help ownership per subcommand
* logging pass against the taxonomy
* `MatchSubResourceRoutes` split
* tests triaged and rewritten against the slice's contracts
* `multiplayer.md`, `sse.md`, ADR-004 addendum

Verification: multiplayer integration tests; packet-byte tests unchanged and passing; a measurement
showing snapshot builds no longer occur with zero subscribers.

Regression risk: highest in the project. Multiplayer concurrency is the invariant most easily
broken, and `MatchSessionRaceTests` is the guard.

Advisor checkpoint: is the hub a real decoupling or an indirection layer? Did the mutation scope
remove the footgun or move it? Does any Phase 2-4 area now need re-touching?

### Phases 2, 3, 4 - parallel slices

Independent of each other in files; all depend on Phase 1 having settled the patterns.

* **Phase 2 - Chat, Bot, Irc**: command hierarchy localization, `!help` composed from registered
  commands, `CommandDispatcher` decomposition, IRC reply localization, logging, tests, `chat.md` and
  `irc.md`.
* **Phase 3 - Users, Auth, Social**: all of section 7 in one commit, plus the `AdminKey:LastChanged`
  runtime-stamp removal, `UserView`, `privileges.md` and `database.md`.
* **Phase 4 - Beatmaps, Scores, Content**: `ScoreDetailView` and DTO grouping, `ComparisonOperator`
  and the enum sweep tail, ingestion and mirror logging, tests, `beatmap-ingestion.md`.

Verification per phase: that slice's contract tests, plus the full suite before merge.

Advisor checkpoint: after all three, before Phase 7 - are the boundaries holding, did any new
accidental coupling appear, is the adjacency allowlist still small?

### Phase 5 - Diagnostic API

Section 10. Purely additive: new files in a new slice, no existing behavior changed. Depends only
on Phase 0's hub, so it can run in parallel with Phase 1.

Verification: each metric's semantics checked against a known process state; `/live` interval
measured; every SSE route confirmed to carry the `/live` suffix; admin key enforcement tested; a
slow consumer proven not to slow collection; overhead measured with the API idle and under
subscription.

### Phase 6 - Load harness

Section 11. Depends on Phase 5 for the diagnostic client.

Verification: kill the harness mid-run and confirm every sample up to that instant is on disk;
confirm load metrics and diagnostics align on one timeline.

### Phase 7 - Final sweep

* full test suite
* Release build
* architecture tests
* `docs/index.md` reconciliation
* full diff review for unrelated changes

Advisor checkpoint: final architectural review.

---

## 13. Risks

| Risk | Mitigation |
| --- | --- |
| The Phase 0 move breaks something the tests do not cover | Integration tests cover the HTTP and auth surfaces; packet tests cover the wire. The move is mechanical, and any behavior change in it is a bug, not a decision. |
| The cross-slice allowlist grows until it is meaningless | It is reviewed at every advisor checkpoint, and each entry must name why the edge exists. Growth is the signal that a boundary is wrong. |
| Multiplayer concurrency regression | The mutation scope makes the correct pattern the only convenient one; `MatchSessionRaceTests` and the lock-wait metric are the guards. |
| DTO grouping breaks API clients | Contract changes are deliberate and documented; where nesting would break a client for no gain, only the internal model is grouped and serialization stays flat. |
| The `006` rebuild loses data | The migration is transactional, copies explicitly by column, and is tested against a populated database fixture before it ships. |
| Test count drops without coverage being replaced | Triage is per-slice and reviewed; deletions are justified against the Implementation Test, and contract coverage is what replaces them. |
| Scope overruns the timebox | The phase graph puts additive work last; a partial delivery still leaves a coherent, migrated codebase. |
