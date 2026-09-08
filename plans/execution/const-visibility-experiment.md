# Task A1 — does `static readonly` close the const blind spot?

**Date:** 2026-09-09
**Answer: yes.** The cheap remedy works. Verifying dependencies from source with Roslyn is not
needed, and ADR-2 records `static readonly` as the chosen mechanism.

## The blind spot

`Basil.ArchitectureTests` enforces the slice allowlist with NetArchTest, which reads IL. The C#
compiler inlines `const` values at the use site, so a slice that consumes only constants from
another slice leaves no assembly reference behind and the rule sees nothing.

Measured before the change: the code contains 44 live cross-slice edges while `SliceAdjacency`
declares 38, and the suite is green anyway.

## What was changed

Four members, from `const` to `static readonly`:

* `Features/Auth/AdminKeyAuthenticationHandler.cs` — `AdminKeyDefaults.Scheme`, `.Policy`, `.Role`
* `Features/Bot/BotBootstrapService.cs` — `BotId`

Checked first that none of them is used where the language requires a compile-time constant: every
use is a method argument (`RequireAuthorization(...)`, `IsInRole(...)`, `GetByUserId(...)`), none is
an attribute argument, a `case` label or a default parameter value.

## Result

Before: **6 passed, 0 failed.**
After: **4 passed, 2 failed** — the rule now sees what the source always said.

`Slices_Should_Only_Reference_Declared_Slices` failed on nineteen types across seven undeclared
edges:

| Edge | Through | Types |
|---|---|---|
| `Auth -> Bot` | `BotBootstrapService.BotId` | `ClientIntegrityService`, `LoginService` |
| `Beatmaps -> Auth` | `AdminKeyDefaults` | `BeatmapsetRoutes`, `BeatmapsetAssetRoutes` |
| `Content -> Auth` | `AdminKeyDefaults` | `AnnounceRoutes`, `FaqRoutes`, `MenuBannerRoutes`, `MenuIconRoutes`, `MenuSeasonalRoutes`, `MirrorSettingsRoutes`, `MotdSettingsRoutes` |
| `Content -> Bot` | `BotBootstrapService.BotId` | `AnnounceRoutes` |
| `Multiplayer -> Auth` | `AdminKeyDefaults` | `MatchRoutes`, `MatchSubResourceRoutes` |
| `Multiplayer -> Bot` | `BotBootstrapService.BotId` | `MatchControlService`, `MatchLiveSnapshotBuilder`, `MatchMembershipService`, `MatchChangeSettingsHandler` |
| `Users -> Bot` | `BotBootstrapService.BotId` | `AvatarRoutes`, `UserRoutes` |

`Shared_Should_Not_Reference_Features` failed too, which the experiment was not looking for and
which is the more useful half of the result: **`Shared.Http.OpenApi.SecuritySchemeTransformers`
reads `AdminKeyDefaults.Policy`**, so `Shared` has been reaching into `Features` in a thirteenth
place that the pinned list could not see. A rule asserting exact set equality was quietly asserting
equality against an incomplete set.

## What was then done (Task A2)

The seven edges are declared in `SliceAdjacency`, each with the justification naming the type that
needs it, and `SecuritySchemeTransformers` is added to the pinned `Shared` offenders. The suite is
green again at 6 passed.

None of these edges is permanent. `AdminKeyDefaults` and the system user ids move to `Basil.Domain`
in Stage C, which removes all seven and the new `Shared` offender with them. They are declared so
that the rule tells the truth in the meantime, not because the coupling is accepted.

## Consequence for the plan

Stage A's remaining risk is gone. The allowlist is now 45 edges rather than 38, and that number is
honest — the difference is not new coupling, it is coupling that was always there.
