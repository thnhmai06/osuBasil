# Testing

## Overview

Basil exposes several hard contracts:

* the bancho binary packet stream
* the IRC wire protocol
* the HTTP API
* multiplayer state and transitions
* user-visible BasilBot responses

These contracts are consumed by real osu! clients, IRC clients, and tournament tooling. Tests protect those contracts
from accidental changes.

A test should therefore verify **observable behavior**, not how that behavior happens to be implemented.

The guiding question is:

> If the implementation were replaced while the externally observable behavior stayed identical, would this test still
> pass?

If the answer is no, the test is probably coupled to an implementation detail.

This document defines where tests belong, what they should assert, and how existing tests should be migrated.

## Test projects

Basil's test projects follow the solution's dependency direction.

| Project                      | Scope                                                   | Typical dependencies                                    |
|------------------------------|---------------------------------------------------------|---------------------------------------------------------|
| `Basil.Domain.Tests`         | Domain rules and pure calculations                      | None                                                    |
| `Basil.Protocol.Tests`       | osu! packet encoding/decoding and wire formats          | None                                                    |
| `Basil.Application.Tests`    | Use cases, packet handlers, sessions, multiplayer state | Mocks and in-memory fakes                               |
| `Basil.Infrastructure.Tests` | Concrete infrastructure implementations                 | Real temporary resources                                |
| `Basil.IntegrationTests`     | End-to-end HTTP behavior                                | Real ASP.NET Core `TestServer`                          |
| `Basil.ArchitectureTests`    | Architectural constraints                               | [NetArchTest](https://github.com/BenMorris/NetArchTest) |

### `Basil.Domain.Tests`

Test pure domain behavior:

* value calculations
* domain rules
* enums and mappings
* state transitions that belong to the domain

These tests should not require a database, host, network connection, or mocking framework.

### `Basil.Protocol.Tests`

Test the wire format itself.

For binary bancho packets, assert the exact encoded bytes. `ServerPacketWriterTests` is the reference implementation for
this style.

Protocol tests belong here rather than behind an HTTP or application-level test.

### `Basil.Application.Tests`

Test application behavior such as:

* use cases
* packet handlers
* authentication
* session lifecycle
* multiplayer operations
* access control
* application-level error handling

Application dependencies should normally be replaced with mocks or deterministic in-memory fakes.

`MultiplayerTestSupport.Fixture` provides shared infrastructure for multiplayer tests.

### `Basil.Infrastructure.Tests`

Test concrete implementations of application ports.

Use real temporary resources where practical:

* SQLite repositories use `SqliteFixture`
* filesystem services use temporary directories
* media codecs use real codec implementations
* the IRC implementation uses its real TCP session

The purpose is to verify that the infrastructure implementation correctly fulfills the contract expected by the
application layer.

### `Basil.IntegrationTests`

Integration tests run a real ASP.NET Core host through an in-memory `TestServer` transport.

Use them for behavior that crosses application and web boundaries, including:

* HTTP status codes
* JSON response envelopes
* JSON field names
* authentication and authorization gates
* SSE streams
* endpoint behavior

A route that only forwards to an application service does not need its business logic duplicated here. Test the service
in `Basil.Application.Tests` and the HTTP contract here.

### `Basil.ArchitectureTests`

Architecture tests enforce dependency-direction rules with [NetArchTest](https://github.com/BenMorris/NetArchTest).

These tests are architectural constraints, not ordinary feature tests. A dependency violation should fail CI even when
the application itself still compiles and runs.

## Writing tests

### Test the contract

Tests should assert behavior that another component can observe.

Good contract assertions include:

* exact bancho packet bytes
* IRC wire text and numeric replies
* HTTP status codes
* response envelope shape
* JSON field names
* API error codes such as `invalid-request` and `incorrect-credentials`
* multiplayer slot, team, host, referee, and access-control state
* BasilBot `!mp` replies

BasilBot replies are public behavior. Changing their wording is therefore a contract change and should be reflected
deliberately in the tests.

Do not normally assert implementation details such as:

* Serilog messages
* debug output
* internal diagnostic strings
* exception messages that never reach a client
* the number or order of internal method calls

For example, this couples a test to implementation:

```csharp
loginService.Mock.Received(2).CallUserRepo();
```

Prefer asserting the resulting behavior:

```csharp
Assert.True(session.IsOnline);
```

If a method call itself is the observable behavior, then asserting the call is appropriate. Otherwise, prefer the
resulting state or response.

### Centralize contract strings

Contract text belongs to the production code that owns that contract.

For example, `src/Basil.Application/Services/Bot/MpReplies.cs` contains the `!mp` response constants. Services emit
those constants and tests assert against the same symbols.

```csharp
// Good
Assert.Equal(MpReplies.MatchLocked, reply);
```

Avoid duplicating the literal in both production and test code:

```csharp
// Bad
Assert.Equal("Locked the match", reply);
```

This rule applies to any client-visible text, including:

* BasilBot replies
* bancho packet strings
* IRC messages
* API error messages

A contract string should have one production owner. Tests should reference that owner rather than maintaining a second
copy of the contract.

### One behavior per test

Each test should have one primary reason to fail.

Avoid tests that verify several independent outcomes:

```text
Login()
 ├── session state
 ├── token generation
 ├── registry entry
 └── packet output
```

Split these into focused tests:

```text
Login_ShouldCreateSession
Login_ShouldGenerateToken
Login_ShouldRegisterSession
```

Use the naming convention:

```text
Condition_Should_ExpectedBehavior
```

Examples:

```text
JoinChannel_ShouldBroadcastJoinPacket
Kick_ShouldRemovePlayerFromMatch
Login_ShouldRejectIncorrectPassword
```

Focused tests make failures immediately actionable.

### Keep tests independent

Every test must be executable:

* by itself
* in any order
* repeatedly
* in parallel where the framework permits it

Tests must not depend on state created by another test.

Do not use static mutable state as shared test storage.

Shared fixtures should provide reusable setup without sharing mutable state between unrelated tests. Prefer per-test or
per-test-class fixtures where appropriate.

### Make tests deterministic

Tests must not depend on wall-clock timing, uncontrolled randomness, or generated identifiers.

When production code uses:

```csharp
DateTime.UtcNow
Random.Shared
Guid.NewGuid()
```

inject an appropriate abstraction when deterministic testing requires control:

```text
IClock
IRandom
IGuidGenerator
```

The test can then provide deterministic values.

Avoid:

```csharp
await Task.Delay(1000);
```

and:

```csharp
Thread.Sleep(100);
```

These make tests slower and flaky.

Prefer:

* awaiting the asynchronous API directly
* `TaskCompletionSource`
* deterministic synchronization primitives
* a poll-with-timeout loop when polling is intrinsic to the behavior

The exception is a test that intentionally reproduces a race condition. Such a test may deliberately widen a timing
window when that is necessary to make the interleaving reproducible. `MatchSessionRaceTests` is an example and documents
this purpose in its XML comment.

### Avoid environment dependence

Tests must run consistently on developer machines and CI.

Do not depend on:

* hardcoded absolute paths
* the current working directory
* `CultureInfo.CurrentCulture`
* `TimeZoneInfo.Local`
* pre-existing files or directories
* a developer's local database

Use temporary directories and temporary SQLite databases created by the test infrastructure.

Resources must be cleaned up after the test.

### Keep Arrange sections short

Large setup blocks usually indicate that the test needs a fixture or builder.

Prefer:

```csharp
var fixture = new MultiplayerTestSupport.Fixture();
var match = fixture.CreateMatch();
```

over repeating dozens of lines of object construction in every test.

Existing shared infrastructure includes:

* `MultiplayerTestSupport.Fixture`
* `SqliteFixture`
* integration `TestDoubles`
* `InMemorySettingsRepository`

When several tests require the same setup, improve the fixture instead of copying the setup into every test.

### Test through public APIs

Tests should use the same public surface that production callers use.

Do not use reflection to invoke private methods or inspect private state.

If private logic is too complex to test through its callers, that is usually a design signal: extract the behavior into
a class with a meaningful public surface and test that class independently.

Avoid tests such as:

```csharp
typeof(SomeService)
    .GetMethod("PrivateMethod", BindingFlags.NonPublic)
    ...
```

The test should exercise the behavior through the class's public contract.

### Test the edges

Happy-path tests are not enough.

Where applicable, cover:

* `null`
* empty input
* duplicate input
* already-existing resources
* offline users
* permission denial
* full matches
* banned users
* invalid state
* overflow and boundary values

Assert expected exceptions explicitly:

```csharp
await Assert.ThrowsAsync<InvalidOperationException>(
    () => service.DoSomethingAsync());
```

A test should make it obvious whether the input is expected to succeed, return an error, or throw.

### Add regression tests for bugs

Every bug fix should include a regression test when the bug is reproducible at the test level.

The preferred sequence is:

1. reproduce the bug with a test
2. verify that the test fails against the buggy implementation
3. fix the implementation
4. verify that the regression test passes

The regression test becomes the permanent specification for the behavior that previously failed.

## Examples

| Smell                | Avoid                                                            | Prefer                                                            |
|----------------------|------------------------------------------------------------------|-------------------------------------------------------------------|
| Log wording          | `Assert.Equal("Player joined channel #osu", logger.Messages[0])` | Assert the resulting channel/session state                        |
| Multiple outcomes    | One test covering login, token, registry, and packets            | One test per independently observable behavior                    |
| Magic number         | `Assert.Equal(17, packet.Length)`                                | Compare with the expected packet or use a named constant          |
| Vague assertion      | `Assert.True(result)`                                            | `Assert.Equal(LoginResult.Success, result)`                       |
| Implementation check | `Received(2).CallUserRepo()`                                     | Assert the observable state or response                           |
| Mock sprawl          | Six mocks for one small behavior                                 | Mock only direct dependencies and reconsider class responsibility |

A mock-heavy test is often a design signal. If a single behavior requires many collaborators to be configured, consider
whether the class under test is responsible for too much.

## Existing test debt

The current test suite predates these guidelines. Existing tests may therefore contain patterns that are no longer
recommended.

Known debt includes:

* exact assertions against log messages
* inconsistent test names
* inconsistent Arrange/Act/Assert structure
* duplicated fixture setup
* implementation-detail assertions
* XML comments describing provenance rather than behavior

This debt does not require an immediate rewrite.

New tests must follow this document.

Existing tests should normally be improved when they are already being modified, rather than rewritten solely for
stylistic consistency.

## Migration policy

When modifying an existing test:

1. Preserve its intended behavior.
2. Prefer making the test better than you found it.
3. Remove implementation coupling when doing so is local and low-risk.
4. Avoid large style-only rewrites.
5. Move substantial test cleanup into a dedicated change when necessary.

Do not mix a large test migration with an unrelated feature or bug fix unless the cleanup is required to make the change
safe.

## Test placement

When deciding where a test belongs, identify the contract being verified.

| Behavior                     | Test project                 |
|------------------------------|------------------------------|
| Domain calculation           | `Basil.Domain.Tests`         |
| Binary packet encoding       | `Basil.Protocol.Tests`       |
| Packet handler behavior      | `Basil.Application.Tests`    |
| Multiplayer state transition | `Basil.Application.Tests`    |
| SQLite repository behavior   | `Basil.Infrastructure.Tests` |
| Filesystem implementation    | `Basil.Infrastructure.Tests` |
| IRC TCP behavior             | `Basil.Infrastructure.Tests` |
| HTTP status/envelope         | `Basil.IntegrationTests`     |
| SSE endpoint behavior        | `Basil.IntegrationTests`     |
| Dependency-direction rule    | `Basil.ArchitectureTests`    |

A useful rule is:

> Test the behavior at the lowest layer that owns the contract.

Do not turn a unit-level behavior into an integration test merely because it eventually becomes visible over HTTP.

## Related code

* [`tests/`](../../tests/): all Basil test projects
* [`src/Basil.Application/Services/Bot/MpReplies.cs`](../../src/Basil.Application/Services/Bot/MpReplies.cs): production owner of `!mp` reply constants
* [`tests/Basil.Application.Tests/Packets/MultiplayerTestSupport.cs`](../../tests/Basil.Application.Tests/Packets/MultiplayerTestSupport.cs): shared multiplayer test fixture
* [`tests/Basil.Infrastructure.Tests/Persistence/SqliteFixture.cs`](../../tests/Basil.Infrastructure.Tests/Persistence/SqliteFixture.cs): temporary SQLite fixture
* [`tests/Basil.IntegrationTests/TestDoubles.cs`](../../tests/Basil.IntegrationTests/TestDoubles.cs): integration-test doubles
* [`tests/Basil.IntegrationTests/InMemorySettingsRepository.cs`](../../tests/Basil.IntegrationTests/InMemorySettingsRepository.cs): in-memory settings repository
* `CLAUDE.md` rule 8: condensed testing rules

## See also

* [`architecture.md`](architecture.md): dependency direction and architectural boundaries
* [`docs-guideline.md`](docs-guideline.md): documentation conventions
