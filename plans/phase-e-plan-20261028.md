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
- **E2 Common.** Create `Basil.Common.Application` / `.Infrastructure`, move Common/Events/Postgres infra. Gate: totals.
- **E3 Leaves.** Beatmaps, Auth, Content, one at a time.
- **E4 Users (with Connections), Anticheat.**
- **E5 Scores, Multiplayer, Chat.**
- **E6 Servers, Bot wiring, hosts' DI entry point.**
- **E7 Architecture tests.** `ModuleGraphTests` becomes a project-reference test with a positive control; delete the
  namespace ratchet or keep it for namespaces inside a project.

## 5. Blockers to settle before E2

1. **Internal access across modules.** Today `internal` reaches across modules. Known hits: `Room.Enter()` and
   `Room.Involves` used by Chat; `MutableX` sets; internal setters on `Connection`; internal constructors wired in
   `AddApplication`. Each hit becomes public with a reason, a moved behaviour, or a listed `InternalsVisibleTo`. No
   IVT to make a build green.
2. **Mediator across assemblies (A4).** Publishing an event type declared outside the generator's assembly threw
   `MissingMessageHandlerException` before. After the split events live in many assemblies. Spike: one leaf project,
   one real event published through the real publisher, handler ran. Fallback: `AddMediator` in the composing project.
3. **E1 foreign keys.** `ExcludeFromMigrations` on the foreign table in the referencing context: does EF still emit the
   FK constraint?

## 6. Related

* `plans/module-events-plan-20261027.md` (D1..D25, progress).
* `docs/for-developers/architecture.md`, `tests/Architecture/ModuleGraphTests.cs`.
