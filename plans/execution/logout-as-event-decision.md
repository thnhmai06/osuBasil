# How logout stops importing five slices

**Decided 2026-09-10. This is Task C3's design question, settled before a worker starts.**

## The question the plan does not answer

Task C3 says "Logout and login become events". Its verification line says what that has to achieve:
*the service imports no feature; Auth leaves the strongly connected component.*

Those are not the same requirement, and the gap matters. "Becomes an event" names a mechanism.
"Imports no feature" names a dependency direction. Only the second is what Stage C is for.

Checked before deciding: **there is no domain-event bus in this codebase.**
`src/Basil.Server/Shared/Eventing/` is entirely SSE machinery — `ILiveEventHub`, `StateStream`,
`LiveSubscription`, `SseSubscriberRegistry`, `BoundedSseChannel`, `SequenceGate`, `SseEndpoints`.
Task B5 has just finished making that hub carry deltas only, keyed by `StreamKey`, for HTTP streams.
It is a loudspeaker for browser clients, not a place to hang domain reactions.

So C3 as literally written means **building** a message bus. That is a design decision of the same
weight as `plans/execution/hub-adoption-decision.md`, and it should not be discovered by a worker
halfway through.

## What is actually there

`PlayerLogoutService` is 93 lines. It imports Irc, Bot, Multiplayer, Spectating and Chat, and it runs
a **coordinated, ordered** teardown with two paths:

* A `GameSession` leaves its match under the match lock, has its spectator relationships torn down,
  parts every joined channel, and — if unrestricted — has its logout broadcast to every other game
  session.
* An `IrcSession` never touches match or spectator state and never sends a presence-offline
  notification; both would be meaningless for a chat-only connection. It parts channels like any
  other session.

The order is load-bearing. Leaving the match before the logout broadcast is what stops other clients
seeing a ghost in a slot.

## The decision

**An ordered handler list, not an event bus.**

`Shared/Sessions` declares one abstraction:

```csharp
public interface IPlayerLogoutHandler
{
    int Order { get; }
    Task OnLogoutAsync(UserSession session, CancellationToken cancellationToken);
}
```

Each slice registers its own handler for the part it owns. `PlayerLogoutService` takes
`IEnumerable<IPlayerLogoutHandler>`, sorts by `Order`, and runs them.

The dependency inverts with the minimum machinery: the service depends on one abstraction it declares
itself, and the five slices depend on `Shared`, which is already the permitted direction. That is
precisely what the verification line asks for.

### Why not a publish/subscribe bus

Because both properties that are explicit today would become implicit:

* **Ordering.** A bus delivers in registration order, which is invisible at the call site and changes
  when DI registration is reshuffled. `Order` is a number a reader can grep and a test can assert.
* **Failure.** A bus has to define what happens when one subscriber throws, and the answer is not
  obvious. Here it is: **catch, log, continue.** A handler that fails must not abandon the remaining
  ones, because a half-cleaned session is exactly the ghost that `GhostDisconnectService` exists to
  mop up afterwards. That is a decision, and it belongs written down rather than inherited from a
  bus's defaults.

`CLAUDE.md` rule 2 also applies directly: the smallest solution that satisfies the request, and no
abstraction without a concrete requirement. One interface satisfies it; a bus adds dispatch,
subscription lifetime and delivery semantics that nothing here needs.

### Game sessions versus IRC sessions

Handlers receive the `UserSession` and decide for themselves, which is what the current code already
does by branching. The match and spectator handlers return immediately for an `IrcSession`. No
separate contract, no marker interface — the branch that exists stays where the knowledge is.

### What this does not do

It does not make login an event. C3's title says "logout **and login**", but login has not been
measured to import feature slices the way logout does. Whoever implements C3 measures login first and
either does the same thing or records that there was nothing to invert. Doing it on the strength of a
task title would be inventing a requirement.

## Verification

* `PlayerLogoutService` imports no `Basil.Server.Features.*` namespace.
* The `Shared_Should_Not_Reference_Features` pinned list loses its `PlayerLogoutService` entries. The
  test asserts exact set equality, so that deletion has to be deliberate — and the deletion is the
  proof.
* An edge counts as removed only when its `SliceAdjacency` row can be deleted with
  `Basil.ArchitectureTests` staying green. See `plans/execution/architecture-progress.md`, "The two
  instruments have opposite blind spots".
* Behaviour is unchanged: the same steps in the same order, and a logout still completes when one
  step fails.

## Related code

* `src/Basil.Server/Shared/Sessions/PlayerLogoutService.cs`
* `src/Basil.Server/Shared/Sessions/GhostDisconnectService.cs`
* `tests/Basil.Server.Tests/Shared/Sessions/PlayerLogoutServiceTests.cs`
