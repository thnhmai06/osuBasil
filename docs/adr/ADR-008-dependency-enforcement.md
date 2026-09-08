# ADR-008 — Dependency rules must be able to see constants

> Status: **Accepted 2026-09-09. Implemented in `ed48716`.**

## Context

`Basil.ArchitectureTests` enforces which slice may reference which, from a declared allowlist in
`SliceAdjacency`, and forbids `Shared` from referencing `Features` except for a pinned list of known
offenders asserted for exact equality.

Both rules use NetArchTest, which reads compiled IL. The C# compiler inlines `const` values at the
use site, so a type that consumes only constants from another type leaves no assembly reference
behind. The rule sees nothing and passes.

This is not a corner case for this codebase. Policy names, authentication scheme names, system user
ids and route-documentation strings are exactly what a codebase expresses as constants, and they are
exactly the values that cross-cutting concerns hand around. The blind spot sat where the coupling
accumulates.

Measured on 2026-09-08: the source contained 44 live cross-slice edges while the allowlist declared
38, and the architecture suite was green.

## Decision

**Shared values that cross a slice boundary are `static readonly`, not `const`.**

A `static readonly` field access emits a reference to its declaring type, so the rule sees it.

The alternative was to stop reading IL and verify dependencies from source with Roslyn, which sees
constant references regardless of how they compile. That is more complete and considerably more
work, and it was only worth doing if the cheap remedy failed.

It did not fail. The experiment is recorded in
`plans/execution/const-visibility-experiment.md`: four members changed from `const` to
`static readonly` — `AdminKeyDefaults.Scheme`, `.Policy`, `.Role`, and `BotBootstrapService.BotId` —
and the suite went from 6 passed to 4 passed / 2 failed, naming nineteen types across seven
undeclared edges.

The experiment also found something it was not looking for, which is the stronger argument for the
decision: `Shared.Http.OpenApi.SecuritySchemeTransformers` reads `AdminKeyDefaults.Policy`, so
`Shared` had been reaching into `Features` in a thirteenth place. A test asserting exact set
equality had been asserting it against an incomplete set — the rule most relied on to be a ratchet
was the one most quietly weakened.

## Consequences

* A `const` is still correct for a value used only inside the type or slice that declares it. The
  rule applies to values that cross a boundary.
* Constants that cross a boundary are a smell in their own right. All seven newly visible edges run
  through two types, and both move to the business layer during the migration, which removes every
  one of them. Declaring the edges is how the rule tells the truth in the meantime, not an
  acceptance of the coupling.
* The allowlist grew from 38 edges to 45. That number is honest; the difference is not new coupling,
  it is coupling that was always there.
* This is a prerequisite, not a step. Every later architecture rule — the business layer referencing
  no framework, no host referencing another host, the feature graph staying acyclic — is a
  dependency rule, and each would otherwise report a success it had not earned.

## Related code

* `tests/Basil.ArchitectureTests/SliceAdjacency.cs`
* `tests/Basil.ArchitectureTests/SliceBoundaryTests.cs`
* `src/Basil.Server/Features/Auth/AdminKeyAuthenticationHandler.cs`
* `src/Basil.Server/Features/Bot/BotBootstrapService.cs`
* `plans/execution/const-visibility-experiment.md`
