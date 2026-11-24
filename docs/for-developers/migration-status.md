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
| Writes take no `CancellationToken`; reads keep it; cancellation tokens are named `ct` | Done |
| Hosts hold no repository, storage or `ScopeTo`; reads are query messages answered in the owning module | Done, checked in review |
| `Scoped*` views of `Server` by category (`Faq`, `Banners`, `Seasonals`, `Icon`, `Screenshots`, `Settings`, `Channels`, `Beatmapsets`) | Done |
| Every change of state a host asks for (sign-in, resume, sign-out, activity, registration) is a Mediator message handled in Infrastructure; the hosts keep lookups only | Done; `SessionRegistry` exposes these operations as `internal` |
| `SessionRegistry` split by responsibility (`SessionRegistry`, `AccountRegistration`, `BotSessions`, `SessionMaintenance`, `ActivityLog`) | Done |
| One handler per event type, no state in a handler (state sits in a singleton service such as `RoomAnnouncements`) | Done |
| Concurrency of the session registry (per-account gate), room countdowns and the SSE hub | Done |
| XML documentation on every public member (`CS1591` is an error) | Done |
| Static `IsValidName`/`TryParse` checks instead of catching constructor exceptions | Done for users, MD5 hashes, channels and content names |

## What is still target

| Area | Today | Target |
|---|---|---|
| Admission control | `Basil.Gateway` exists and answers `Allow`/`Deny`; the rate limiter and upload limits still sit partly in `Basil.Host.Shared` and the hosts | The hosts only enforce the gateway's answer, and every entry point declares a policy |
| Options binding | `Basil.Host.Irc`, `Basil.Host.Rest` and `Basil.Infrastructure.Beatmaps` bind their own options; they read single keys (`GetValue`) rather than binding a section; `Basil` binds the options it defines (updates, telemetry) | Every options type bound as a section by the project that defines it |
| Infrastructure part of "Rest and Cli never reference `Protocol.Game.Stable`" | Enforced for the project references of the hosts | Also for the Infrastructure projects |
| Tests naming | Most test methods follow `Condition_Result` | All of them |

## Related code

* The dependency rules above are checked in review; the repository has no test that enforces them yet.
* [`architecture.md`](architecture.md): the target architecture.

## Known gaps

* `!mp` kick, unban, team and removeref have no test for an unknown name.
* The `MissingPrivileges` reply constants keep their names and keys so reply files that override them still apply, though a refusal no longer names the privileges.
* No load test exists for the 1k-concurrent-users target; the write pool (8 by default) and the single event reader are the first limits to measure.
* The real binary has been run once against a database (health, a refusal, stop signal) but not as a full per-protocol check.
