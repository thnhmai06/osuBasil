# Migration status

## Why this page exists

The architecture pages describe the target. The code reaches it module by module, so for a while the two differ. This
page is the one place that lists where they differ; the other pages describe the target state and link here instead of
carrying their own "(target)" notes.

## What is migrated

| Area | State |
|---|---|
| Module split (Domain, Application and Infrastructure per module, Protocol, Gateway, Hosts, composition root) | Done; the graph is checked in review, and no test in the repository enforces it yet |
| Awaited persistence (cache-aside), no write-behind queue | Done |
| PostgreSQL is the only database; the provider seam (`IDatabaseProvider`) is gone and Npgsql is set up in `Database` directly; one baseline migration per module | Done |
| Writes take no `CancellationToken`; reads keep it; cancellation tokens are named `ct` | Done |
| Hosts hold no repository, storage or `ScopeTo`; reads are query messages answered in the owning module | Done, checked in review |
| `Scoped*` views of `Server` by category (`Faq`, `Banners`, `Seasonals`, `Icon`, `Screenshots`, `Settings`, `Channels`, `Beatmapsets`) | Done |
| Every change of state a host asks for (sign-in, resume, sign-out, activity, registration) is a Mediator message handled in Infrastructure; the hosts keep lookups only | Done; `SessionRegistry` exposes these operations as `internal` |
| `SessionRegistry` split by responsibility (`SessionRegistry`, `AccountRegistration`, `BotSessions`, `SessionMaintenance`, `ActivityLog`) | Done |
| One handler per event type, no state in a handler (state sits in a singleton service such as `RoomAnnouncements`) | Done |
| Concurrency of the session registry (per-account gate), room countdowns and the SSE hub | Done |
| XML documentation on every public member (`CS1591` is an error) | Done |
| Static `IsValidName`/`TryParse` checks instead of catching constructor exceptions | Done for users, MD5 hashes, channels and content names |
| Every rule of a wire lives in its Protocol project (`Basil.Protocol.Irc`: `IrcWire`, `IrcVerb`, `IrcLines`, `IrcText`, `IrcCapabilities`, `IrcIsupport`, `ProxyProtocol`; `Basil.Protocol.Game.Stable`: `OsuWebWriter`, `RoomLink`; the host maps slot numbers with `WireMap`) | Done |
| Options binding (D44): `Basil.Infrastructure.Shared`, `Basil.Infrastructure.Beatmaps`, `Basil.Host.Irc` and `Basil.Host.Rest` bind the options they define in their `Add...(IConfiguration)`; `Basil` binds only what it defines (server, logging, telemetry, updates); key paths are unchanged | Done |
| Reply catalogs (D43): one `IReplies<TReply>` in `Basil.Application.Shared`, one implementation in `Basil.Infrastructure.Shared` reading `Data/Localizations/<surface>.json`; BasilBot (`BotReply`) and IRC (`IrcReply`) use it, the Stable host keeps its texts as constants | Done |
| The edge (D29 to D37): Basil serves plain HTTP and plain IRC behind nginx; the client address comes only from a trusted proxy; IRC requires a PROXY header from it; stored files go out through `X-Accel-Redirect`; no certificate, mDNS, native restart or self-update in Basil | Done; see [`edge.md`](edge.md) |
| The `Data/` layout (D26, D29): one `Data/` volume with `Data/metadata.json` and an ordered step list, shipped files copied where missing, `Cache/`, `Logs/` and `Temp/` beside it; a start is refused on a newer layout or an unknown database migration | Done; see [`configuration.md`](../for-technicians/configuration.md) |
| Uploads (D27): early authorization, a streaming guarded read with magic-byte checks, each storage writes and moves its own temporary file; no spool and no quarantine | Done |
| The osu! calculation stack (D39 to D41) loaded from `Data/Rulesets/` and checked against an embedded manifest | Done |
| Admission control (D31): one tier, the subject is a signed-in user; the hosts call the Gateway and enforce its answer; named rules, per-class allowances, `Elevated` for `Tournament` | Done; see [`gateway.md`](gateway.md) |
| The relationship registry (D45) and the presence filter | Done |
| One room-view rule in Domain, used by BasilBot, IRC, the API and the match list | Done |

## What is still target

| Area | Today | Target |
|---|---|---|
| Infrastructure part of "Rest and Cli never reference `Protocol.Game.Stable`" | Enforced for the project references of the hosts | Also for the Infrastructure projects |
| Tests | The repository holds no test projects; the policy is in [`testing.md`](testing.md) and D42 lists the seven projects planned before 1.0 | The core test projects, one per frozen contract |
| An entry point that declares no admission policy | It is admitted as `Interactive` | A check that enumerates every route, packet type and IRC command and fails on one without a declaration |

## Related code

* The dependency rules above are checked in review; the repository has no test that enforces them yet.
* [`architecture.md`](architecture.md): the target architecture.

## Known gaps

The first four were recorded while the last waves landed and not re-checked since; the rest are still true.

* `POST /v1/announce` and BasilBot name lookups resolve a name before the privilege check.
* `MatchQuery.LimitTo` keeps its own copy of the view rule for closed matches.
* A chunked upload without a declared length is not charged to the user's byte allowance (nginx still caps its size).
* The `DashboardModel` output list is never filled.
* The field order of the osu! upload form is unverified against a real client (both orders are handled).
* A multi-line IRC action is split without repeating the framing.
* The `MissingPrivileges` reply constants keep their names and keys so reply files that override them still apply, though
  a refusal no longer names the privileges.
* A silenced IRC sender gets no numeric reply: the post outcome `Silenced` has no case in the IRC host's reply mapping.
* `!mp` kick, unban, team and removeref have no test for an unknown name.
* No load baseline exists yet. `tools/LoadHarness` and [`PERF.md`](../../PERF.md) define the scenario and keep the ledger,
  and the write pool (8 by default) and the single event reader per lane are the first limits to measure.
* The real binary has been run against a database but not as a full per-protocol check with real clients.
