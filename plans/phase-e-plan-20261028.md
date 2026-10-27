# Phase E: module projects

Status: stage E0 (this document), 2026-10-28. Replaces section 2 of `module-events-plan-20261027.md` where they differ.

## 1. Why, what, how

**Why.** Application and Infrastructure are split by technique (Persistence, Capabilities, Runtime). The module graph is
now clean at the namespace level (`ModuleGraphTests`, no debt), so the split by module can follow: each module is a pair
of projects whose references the compiler enforces.

**What.** One pair `Basil.<Module>.Application` / `Basil.<Module>.Infrastructure` per module, one shared Domain, one
shared `Basil.Common.*` pair, one DbContext per module. Hosts and Protocol are untouched (they are outside the build).

**How.** In stages, one worker at a time (the moves all touch `Basil.slnx`, `DependencyInjection.cs` and
`TestApplication`), each stage buildable with the same test totals.

## 2. Decisions

| # | Decision | Source |
|---|---|---|
| E1 | One `Basil.Domain` project, shared. | owner, 2026-10-28 |
| E2 | One DbContext per module; one migration history per module. | owner, 2026-10-28 |
| E3 | Naming: `Basil.<Module>.Application`, `Basil.<Module>.Infrastructure`, namespaces equal the project name, tests `Basil.<Module>.Application.Tests` under `tests/Modules/<Module>/...` mirroring `src/Modules/<Module>/...`. | proposed |
| E4 | Users and Connections are two folders of ONE project, `Basil.Users.*` (they reference each other, and project references cannot form a cycle). The owner's "Connection riêng" holds at folder and namespace level. | forced by the compiler; to confirm |
| E5 | Anticheat, Auth and Servers are their own modules (A2). Anticheat -> Users; Auth -> (Domain only); Users -> Auth; Servers -> every module. | A2 |
| E6 | One composition project (`Basil.Composition`) references every module, calls `AddMediator` once and holds the only reference to `Mediator.SourceGenerator`; every module references `Mediator.Abstractions`. Each module registers itself with a public `Add<Module>()`; its contexts and constructors stay `internal`. | spike S2 (proposed) |
| E7 | Handlers stay `internal`; each module grants `InternalsVisibleTo("Basil.Composition")` (the generated code names them) and to its own test project. No other `InternalsVisibleTo`. Handlers registered by hand in addition to the generated ones run twice, so none are. | spikes S1, S2 (proposed) |
| E9 | Each module context maps the foreign tables it references as entity types with `ToTable("…", t => t.ExcludeFromMigrations())` and declares the FK with `HasOne<Row>().WithMany().HasForeignKey(...)`; only the owning context creates the table. 8 of the 16 FKs cross a module (all to `users`, plus `round_scores.score_id` -> `scores`). Migrations are applied in the FK order Common, Users, Auth, Beatmaps, Content, Scores, Multiplayer, Chat, one history table `__ef_migrations_<module>` each. | spike S3 (proposed) |
| E8 | Cross-module internals (18 real uses): `Servers` -> `Chat`/`Users` internals move into those modules (7 + 6 members); `Room.Enter` stays internal and `RoomChannels` gets a Multiplayer method that returns the participants; `HandlerStep`, `Room.Involves`, `ConnectionRules.AllowsMany` become public. | spike S1 (proposed) |

## 3. Modules and references (all pointing down, acyclic)

```text
Common      -> Domain
Auth        -> Common
Beatmaps    -> Common
Content     -> Common
Users       -> Common, Auth          (folders Users/ and Connections/)
Anticheat   -> Users
Scores      -> Users, Beatmaps
Multiplayer -> Users, Beatmaps, Scores
Chat        -> Users, Multiplayer
Servers     -> every module above
Bot         -> every module
```

What goes where (today's folders): Runtime `Common`, `Events` and Persistence `Common`, Infrastructure `Postgres`, `Memory`,
`Files` -> Common. Runtime `Users` + `Connections`, Persistence `Users`, Capabilities `Users` -> Users. Persistence
`Auth` -> Auth. Runtime `Anticheat` -> Anticheat. Persistence/Capabilities/Runtime `Beatmaps`, `Scores`, `Multiplayer`,
`Chat`, `Content`, `Servers` -> the module of the same name.

## 4. Stages and gates

Totals to hold at every stage (sum over all test projects, a per-module split must add up): Domain 330, Application.Runtime
766, Bot 950, Architecture 29, Infrastructure.Runtime 18, Infrastructure.Persistence 678 (+4 skipped); Infrastructure.Capabilities 69, Protocol.Bancho 507,
Protocol.Irc 117; all together 3464 passed and 4 skipped, by `plans/evidence/phase-e/count-tests.sh`.

- **E0 (this).** Plan; baseline counts script; access inventory; Mediator spike. Gate: both spikes reported.
- **E1 DbContext split, in place.** Inside `Infrastructure.Persistence`: one context per module, own
  `MigrationsHistoryTable`, `DatabaseMigrator` applies them in dependency order, shared `Database*`/`WriteTicketRow`
  infrastructure decided (generic over the context or owned by Common). Cross-module foreign keys are kept if EF can
  generate them without SQL text (see the spike); otherwise the owner decides. Gate: Persistence tests green, a fresh
  database migrates from empty, the `InitialCreate` precondition (no database applied the old migration) restated.
- **E1 done (2026-10-28).** Eight module contexts (Common, Users, Auth, Beatmaps, Content, Scores, Multiplayer, Chat) with one history table each, applied by `DatabaseMigrator` in the order of their registration; `Database`, `DatabaseWriter`, `DatabaseReader` generic over the context; one regenerated `InitialCreate` per module. Proof: a fresh database migrated by the new contexts has a `pg_dump -s` schema identical to the old one except for the history tables, 16 foreign keys; totals 3467 passed (3464 + 3 new), 4 skipped.
- **E2 Common.** Create `Basil.Common.Application` / `.Infrastructure`, move Common/Events/Postgres infra. Gate: totals.
- **E2 done (2026-10-28).** Added both Common projects, moved shared code and independent tests, and replaced the shared-kernel
  namespace check with a Common project-reference guard (same architecture test count). Total: 3467 passed, 4 skipped.
- **E3 Leaves.** Beatmaps, Auth, Content, one at a time.
- **E4 Users (with Connections), Anticheat.**
- **E5 Scores, Multiplayer, Chat.**
- **E6 Servers, Bot wiring, hosts' DI entry point.**
- **E7 Architecture tests.** `ModuleGraphTests` becomes a project-reference test with a positive control; delete the
  namespace ratchet or keep it for namespaces inside a project.

## 5. Spike results (2026-10-28)

Reports and the code that produced them are in `plans/evidence/phase-e/`.

* **S1 (internal access).** 3265 method bodies scanned, 226 references, 76 members. Beyond composition and generated
  code only 18 members are used across modules: `Servers` -> `Chat` (7) and `Users` (6), `Chat` -> `Multiplayer`
  (`Room.Enter`, `Room.Involves`), `Chat` -> `Users` (`ConnectionRules.AllowsMany`), several modules -> `Events`
  (`HandlerStep`). No `InternalsVisibleTo` is needed for any of them. `Bot` and `Basil.Testing` use only public members.
  Blind spots: reflection, expression trees, serializers.
* **S2 (Mediator).** Exactly one arrangement works: the generator runs in the one assembly that calls `AddMediator` and that
  assembly references every module. The generator finds messages and handlers of referenced assemblies, but its code names
  the handler types, so handlers are public or internal with `InternalsVisibleTo` to that assembly. Generator in several
  modules fails (`CS0121`); generator only in a shared base builds and throws `MissingMessageHandlerException`. Below 17
  notifications an unknown event is silently dropped, so a handler test must publish a real event and assert it ran.
  `QueuedNotificationPublisher` must be nameable from the composition project.

* **S3 (DbContext split).** EF keeps the cross-module foreign keys without SQL text when the referencing context maps the
  principal table as an entity type excluded from migrations. Applying a referencing module before its principal fails
  with `42P01`, so the migrator orders the contexts by the FK graph (Auth after Users even though the code reference
  goes Users -> Auth). Each context has its own history table and never touches another's. `Database`, `DatabaseWriter`,
  `DatabaseReader` and `WriteTicketRow` stay shared and become generic over the context type; every context maps
  `write_tickets`. Regenerating the single `InitialCreate` per module needs the same precondition as before.

## 6. Blockers still open before E2

None from the spikes. E1 must prove the schema is unchanged (see section 4).

## 7. Related

* `plans/module-events-plan-20261027.md` (D1..D25, progress).
* `docs/for-developers/architecture.md`, `tests/Architecture/ModuleGraphTests.cs`.
