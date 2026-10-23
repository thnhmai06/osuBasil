---
name: storage-services-split
description: "Application is four projects since 2026-10-06: Storage.Contracts/Storage.Implementations and Services.Contracts/Services.Implementations, joined by DI; next is migrating Infrastructure, hosts, tests, then docs."
metadata:
  node_type: memory
  type: project
  originSessionId: a5c6af71-b5b0-44e3-bdd8-0b8defbe36c1
  modified: 2026-10-03T11:24:48.466Z
---

`plans/storage-services-split-plan-20261003.md` is the authoritative plan (renamed from `...-20261001.md`
on 2026-10-03; approved; phases 1-6 implemented and pushed by 2026-10-04, `baaab73f`). Three projects: `Basil.Application.Storage` (persistent
ports, registries, runtime models, query records), `Basil.Application.Contracts` (`IXService`, capability
ports implemented by Infrastructure, events, inputs/results), `Basil.Application.Services` (internal sealed
implementations, registered by `AddApplicationServices()`). Infrastructure must not reference Services; hosts may (Services is a peer of
Infrastructure). Background work: contract declares one run, Services implements, Infrastructure loops;
one-shot timers stay in Services. Section 12 + appendix B hold the feature inventory of `main` and the
outdated develop Infrastructure/hosts; appendix C lists port sources.

Decisions 2026-10-03 (plan section 8, items 6–16): API actions pass the BasilBot connection as `by` (server authority; replaced the earlier `by = null` idea); BasilBot is an ordinary user id 0 in IUserRepository, auto-created at startup, name/country edited via the repository, config keeps only `Basil:Bot:CommandPrefix`, never deletable/silenceable/privilege-changeable and never allowed to log in by client whatever its stored password (plan 2.7); keep
friends/blocking/PM-privacy (fix working-scopes.md); drop the bancho.py-only Verified bit and every automatic ban/flag mechanism (hardware ban, empty-adapter check, no auto-ban; multi-accounts are not tracked; anticheat flags only warn: RoomService.ReportClientFlagsAsync emits RoomPlayerFlagged when a seated player reports cheat flags, the bot warns the room chat and DMs referees + creator) but keep login history (client + fingerprint) in `ILoginRepository` for lookup and for `ScoreService` submission checks;
`Basil.Host` gets a `--reset-admin-key` startup option (plan 12.9);
stats written with `CreateOrUpdateAsync`; the develop-vs-main divergence table (12.6) follows develop
(confirmed).

**Update 2026-10-06 (commits `276e11ea`, `986a304a`):** projects renamed to `Basil.Application.Storage.Contracts` (runtime models as concrete classes, ports, registry contracts `IUserRegistry`/`ILobby`/`IGeneralChannelRegistry` with `internal` mutators), `Basil.Application.Storage.Implementations` (internal in-memory registries, `AddApplicationStorage()`), `Basil.Application.Services.Contracts`, `Basil.Application.Services.Implementations` (`AddApplicationServices()`, never references Storage.Implementations). Room scope is `room.EnterAsync()`. The three-project description above is history; the old BasilBot decisions are superseded by [[identity-permissions]]. Baseline `plans/storage-services-split-baseline.cs` is now in the repo.

**Why:** see [[storage-vs-services]] and [[contract-naming-neutral]].

**How to apply:** Application is done; section 13 of the plan lists every deviation (no RecordRoundAsync, anticheat in RoomService, MatchWith deleted, SeatAsync limited to rooms the caller manages, deleted users store `ClientPrivileges.None` — existing DB rows need a migration). Next: migrate Infrastructure, hosts and tests to the new namespaces/contracts, then rewrite architecture.md, multiplayer.md, chat.md (deferred per AGENTS.md "rewrite docs when the code is migrated"). The behaviour baseline is a scratch `check.cs` (not in repo); port its scenarios into tests when the test projects are migrated. Delegate per [[opencode-implements-only]]; review line by line ([[review-quality-not-build]]).
