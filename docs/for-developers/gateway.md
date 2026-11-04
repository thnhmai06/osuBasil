# Gateway

## Overview

The gateway is the admission control of Basil: for every request that wants to enter the system it answers `Allow` or
`Deny`, using only facts that exist before any business logic runs. A request that is oversized, too frequent, too
concurrent or arriving while the server is overloaded never reaches a module.

> **Migration status.** The rate limiter, the login-attempt guard and the second tier (`Admission`, `AdmissionSubject`,
> `GatewayLimits`) live in `Basil.Gateway`. No host calls the second tier yet: mapping a `Connection` to an
> `AdmissionSubject` in `Basil.Host.Shared` and enforcing the decision is **(target: phase 7)**. The Stable and Rest hosts
> still have their own upload limit (they depend on ASP.NET Core and the runtime settings, so they cannot move as they
> are). The load policies are **(target)**.

## Why

Limits written inside each host are repeated, drift apart and are easy to forget on a new route. Putting them in one
place makes the policy visible, testable and the same for every protocol. The gateway is kept apart from the business on
purpose: a ban or a restriction is a business fact, decided at login and by capabilities (see
[`../for-agents/identity-permissions.md`](../for-agents/identity-permissions.md)); the gateway only protects the process.

## What

* **A leaf project.** `Basil.Gateway` references nothing of Basil, only the rate-limiting library of the platform. The four
  hosts reference it; it references no user type.
* **Allow or Deny only.** A decision carries a reason code and, when useful, a retry delay. It does not translate, route or
  dispatch.
* **The host enforces.** The host reports a denial in its own protocol: HTTP 429, 413 or 503 (with `Retry-After` when there
  is a delay) on Rest and the web endpoints of Stable, and the refusal form of IRC and the console on those hosts.
* **No admission facts from the business.** There is no interface through which the gateway asks the business whether a user
  is banned or restricted.

### Policies

| Policy | Limits | Keyed by |
|---|---|---|
| Size | bytes of a body or upload, pixels of an image, length of a line | the entry point |
| Rate | requests or attempts per window | client address (tier 1), user or API key (tier 2) |
| Concurrency | requests in flight | the kind of work (a bulkhead), the user |
| Load | refuse or shed work while the server is overloaded | the priority of the work |

### Two tiers

1. **Before authentication.** Raw facts only: client address, size, concurrency. This tier guards login and token lookup,
   where nothing is known about the caller. Failed login attempts keep their own counter, because a limiter that is
   consumed by time does not give a permit back on success.
2. **After authentication.** Per user. The host turns the `Connection` into an opaque `AdmissionSubject` (a key and a tier)
   that the gateway defines; the mapping lives in `Basil.Host.Shared`. A signed-in user gets a higher allowance, and
   all of a user's connections share one key. The tier is recomputed for each request from effective permissions and is not
   stored. The gateway never sees a Users type.

### Load policies (target)

Taken from well-known cloud design patterns, limited to what this single process needs:

* **Throttling** caps the rate at which a client or kind of work is served.
* **Bulkhead** bounds concurrency per kind of work so that one slow kind (a beatmapset import, a leaderboard query) cannot
  use up every worker.
* **Priority shedding** under overload drops the least valuable work first: leaderboards and search before gameplay and chat.
* **Queue-based load leveling** is used only for work that can be derived again (rebuilding a derived file, a re-scan).
  Never for a request whose answer the client waits for or whose loss would lose data.

## How

```mermaid
flowchart LR
    R[Request] --> T1{Tier 1: address, size, concurrency}
    T1 -- Deny --> D[Host reports denial in its protocol]
    T1 -- Allow --> A[Authenticate: resolve the Connection]
    A --> T2{Tier 2: AdmissionSubject}
    T2 -- Deny --> D
    T2 -- Allow --> M[Send message to the module]
```

Policies read an in-memory snapshot of their configuration, never the database on each request. The factors per tier are
runtime-configurable.

## Invariants

* The gateway is a leaf; it references no user, room or other business type.
* Every entry point (an HTTP route, a packet type, an IRC command) declares an admission policy. The rule is
  `AdmissionPolicyTests`: an entry point declares it by carrying `AdmissionPolicyAttribute` in its metadata, and a
  positive control proves an entry point without it is detected. The test enumerates every route of the Rest host and of
  the two osu! routers (as mapped endpoints), every packet type of the osu! client (the whole `ClientPacketType` enum) and
  every IRC command (the commands of the IRC reference). The attribute only declares; a policy that applies no limit
  still counts as declared. The images served on `a.`/`assets.` are middleware, not routes, and are not enumerated.
* A denial is decided before business logic runs, and the refusal is the same whether or not the target exists.
* Bans and restrictions are not gateway concerns.

## Related code

* [`RateLimiter.cs`](../../src/Gateway/RateLimiter.cs) and [`LoginAttemptGuard.cs`](../../src/Gateway/LoginAttemptGuard.cs) — the limiter and login guard.
* [`Admission.cs`](../../src/Gateway/Admission.cs) and [`GatewayLimits.cs`](../../src/Gateway/GatewayLimits.cs) — tier 2 and its swappable limits snapshot.
* [`UploadLimit.cs`](../../src/Hosts/Rest/Http/UploadLimit.cs) and [`UploadLimit.cs`](../../src/Hosts/Stable/Support/UploadLimit.cs) — the two upload limits. They stay in the hosts: they differ and depend on ASP.NET and the stored settings, so they do not move to the gateway.

## See also

* [`architecture.md`](architecture.md): where the gateway sits
* [`diagnostics.md`](diagnostics.md): rejected requests are counted there
