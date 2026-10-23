---
name: infrastructure-rewrite
description: "Infrastructure rewrite plan (2026-10-07): 3 projects Storage/Services/Runtime + BasilBot Application; decisions on ids, events, watcher"
metadata:
  node_type: memory
  type: project
  originSessionId: b978cba2-cc34-44cd-be1a-bcf5af19573d
  modified: 2026-10-07T11:54:26.498Z
---

Plan `plans/infrastructure-plan-20261007.md`, approved 2026-10-07. Old Infrastructure/Hosts (commit `40990186`) are
reference only.

- Projects: `src/Infrastructure/{Storage,Services,Runtime}` (`Basil.Infrastructure.*`, implementing
  Storage.Contracts / Services.Contracts capability ports / event pumps + handlers + background loops) and
  `src/Bot/Application` (`Basil.Bot.Application`, references only `Basil.Domain`, owns its own ports and DTOs;
  HTTP/SSE adapter comes with the hosts once Host.Api exists).
- Ids are assigned by the store, never by services: models follow `X { Id, Value: XData }` (Beatmap, Beatmapset
  converted); `CreateAsync(XData, int? onlineId)` uses the online id when given, else a local id ≥ 1e9.
- Room/lobby events carry `User By` (kick, ban, host, referees, `LobbyRoomClosed.By` null = closed by emptiness).
- Beatmapset import has no actor; host gates by policy; a watcher imports `.osz` dropped in `Data/Imports`.
- New baseline DB schema (no upgrade from alpha), SQLite + Dapper + BCrypt, `PRAGMA user_version` migrations.
- Hosts and tests ignored this round ([[ignore-downstream]]).

**Why:** user decisions in the 2026-10-06/07 planning session.
**How to apply:** follow the plan's phase table; see [[delegation-chain]] for model choice.

Status 2026-10-07: done and pushed to `develop` (P0 `0ea6c795` … `2b8ec3fc`). Infrastructure smoke and BasilBot smoke
(scripts in the session scratchpad, outside the repo) both ALL PASS. Bot `!mp` room scope: room channel first, then the
`!mp in` target (PM), then the sender's seat; bot reads rooms for the sender via `IBasilRooms.GetForAsync`.
Next: migrate the hosts (Bancho/IRC/API notification handlers, Host.Api routes that become BasilBot's adapter), then tests.

2026-10-07 final (`f62fdc8f`): memory is the truth, DB a snapshot; DatabaseBatcher (batches due at 100 writes or
50 ms, reads in the same batch) + DatabaseWorker (only DB user, savepoint per operation); writes snapshot values when
queued, failures return to caller, never retried; cache keeps live instances + 5 idle minutes (MemoryCache sliding +
WeakReference); try-operations on DB for uniqueness/ids. Supersedes the "never evicted" and WriteBuffer notes below.

2026-10-07 later: repositories became the runtime source of truth (`Storage/Caching`: IdentityMap, WriteBuffer,
WriteFlusher, CachedRepository; `bfbddb89`, `b562f8ee`). Changes checked against other rows (user rename, beatmap
hash) go to the DB first and change the live object after commit (`d8e3f51f`, rule in AGENTS.md). Smoke script in
the session scratchpad covers it (ALL PASS).
