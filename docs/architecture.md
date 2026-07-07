# Architecture

bancho-net follows **Monolith Clean Architecture**: one deployable, but with the dependency-inversion discipline of Clean Architecture enforced between layers. The rule is checked by an automated test suite (`tests/Bancho.ArchitectureTests`, using [NetArchTest](https://github.com/BenMorris/NetArchTest)), not just left as a convention — a PR that violates the dependency direction fails CI.

## The five projects

```
Bancho.Domain           →  no project references. Pure C#: enums, records, value calculators.
Bancho.Protocol         →  no project references. Bancho wire-format packet reading/writing.
Bancho.Application      →  references Domain, Protocol. Use cases, packet handlers, and *ports*
                            (interfaces) describing what Infrastructure must provide.
Bancho.Infrastructure    →  references Application (to implement its ports), Domain. MySQL/Redis/
                            filesystem/Rust-FFI implementations.
Bancho.Web               →  references Application, Infrastructure, Protocol. ASP.NET Core host:
                            subdomain-based routing, DI composition root, Program.cs.
```

The dependency rule: **Domain and Protocol depend on nothing else in the solution.** Application depends on Domain and Protocol, but never on Infrastructure or Web — it only knows about *interfaces* (`IUserRepository`, `IScoreRepository`, `IReplayStorage`, etc., under `Bancho.Application.Abstractions.*`). Infrastructure implements those interfaces but is never referenced by Application. Web is the only project allowed to know about all four others — it's the composition root that wires interfaces to their concrete implementations at startup.

This means every MySQL/Redis/filesystem detail lives in `Bancho.Infrastructure`, and `Bancho.Application`'s use cases can be unit-tested by substituting fakes for those interfaces — no database needed. `tests/Bancho.Infrastructure.Tests` is the one suite that talks to a real MySQL (spun up per-test-run via Testcontainers), verifying the concrete implementations actually match the schema.

## Project layout within `Bancho.Application`

The three largest folders are organized by feature area rather than left flat:

- **`PacketHandlers/`** — one class per bancho client packet, split into `Core/` (session lifecycle: login-adjacent packets, presence, stats), `Channels/` (chat), `Spectating/`, and `Multiplayer/` (match + tournament packets, the largest group).
- **`Abstractions/`** — the ports Infrastructure implements, split by domain concept: `Beatmaps/`, `Scores/`, `Users/`, `Channels/`, `Social/` (mail + relationships + moderation logging).
- **`Sessions/`** — in-memory session state (`PlayerSession`, `ChannelSession`, `MatchSession`) and the registries that track it, split into `Channels/` and `Multiplayer/` subfolders with player-level state at the root.
- **`UseCases/`** — one folder per feature (`Authentication/`, `Beatmaps/`, `Multiplayer/`, `Scores/`, `Spectating/`, `Mail/`, `Anticheat/`), each holding the actual business logic a packet handler or HTTP route delegates to.

`Bancho.Domain`, `Bancho.Protocol`, and `Bancho.Infrastructure/Persistence` follow the same pattern (topic subfolders like `Login/`, `Beatmaps/`, `Scores/`, `Multiplayer/`, `Users/`, `Repositories/`) — the namespace matches the folder path, so `grep`-ing an import tells you exactly where the file lives.

## Request flow: login

The osu! client sends a login as an HTTP POST with no `osu-token` header. There's no dedicated packet for it.

1. `Bancho.Web/Routing/BanchoHostGroups.cs` — the `c.`/`ce.`/`c4.`/`c5.`/`c6.` subdomain group's `POST /` route reads the raw body, resolves the client IP (`Bancho.Domain.Login.ClientIpResolver`), and calls `OsuLoginUseCase.ExecuteAsync`.
2. `OsuLoginUseCase` (`Bancho.Application/UseCases/Authentication/`) parses the login body (`LoginDataParser`, `OsuVersionParser`, `AdaptersStringParser` — all in `Bancho.Domain.Login`), authenticates against `IUserRepository`/`IPasswordHasher`, checks for an existing session, loads per-mode stats via `IStatsRepository`, and builds a `PlayerSession`.
3. The concrete `IUserRepository`/`IStatsRepository`/`IPasswordHasher` implementations (`MySqlUserRepository`, `MySqlStatsRepository`, `BCryptPasswordHasher`) live in `Bancho.Infrastructure` and are wired in at startup by `InfrastructureServiceCollectionExtensions`/`ApplicationServiceCollectionExtensions` — `OsuLoginUseCase` itself never references a concrete class, only the interfaces.
4. The response is a stream of bancho packets (`Bancho.Protocol.Packets.ServerPacketWriter`) — protocol version, login reply, privileges, channel list, and cached presence/stats for every already-online player.

Every subsequent client request carries an `osu-token` header; `BanchoHostGroups.cs` looks the session up by token and dispatches the packet body through `BanchoPacketDispatcher` to the matching handler in `PacketHandlers/`.

## Request flow: multiplayer match creation

1. Client sends the `CREATE_MATCH` packet → `BanchoPacketDispatcher` routes it to `CreateMatchHandler` (`PacketHandlers/Multiplayer/`).
2. `CreateMatchHandler` delegates to `MatchMembershipService.Create` (`UseCases/Multiplayer/`), which atomically allocates a match ID from the 64-slot `IMatchRegistry`, builds a `MatchSession` (`Sessions/Multiplayer/`), registers its dedicated chat channel, and joins the host into slot 0.
3. Every subsequent match packet handler (`MatchChangeSlotHandler`, `MatchReadyHandler`, `MatchStartHandler`, etc.) acquires `MatchSession.Lock` — a per-match `SemaphoreSlim(1, 1)` — before reading or mutating slot state, then broadcasts the updated match state before releasing it. This lock is bancho-net's own addition: the Python source relies on asyncio's single-threaded event loop for atomicity between `await` points, which ASP.NET Core's real thread pool doesn't give for free.
4. `tests/Bancho.Application.Tests/Sessions/MatchSessionRaceTests.cs` is the test that actually proves the lock works — it reproduces a genuine lost-write race with the lock removed, then shows the same scenario is race-free with it in place.

## Where to look when adding something new

- A new bancho packet → a new class in the matching `PacketHandlers/*` subfolder, registered in `ApplicationServiceCollectionExtensions`, counted in `CompositionRootTests`.
- A new piece of persisted state → a new method on an existing (or new) interface under `Abstractions/*`, implemented in `Bancho.Infrastructure/Persistence/Repositories/`.
- A new HTTP endpoint → a new route in `Bancho.Web/Routing/BanchoHostGroups.cs`, under whichever host group (`osu.`, `b.`, `api.`) it belongs to.
