# Testing

## Overview

Basil is a server whose observable surface is a set of hard contracts: the bancho binary packet stream, the IRC wire protocol, the HTTP API, and the state transitions of a multiplayer match. Those contracts are cheap to pin down and expensive to get wrong, because the other end is a real osu! client or a tournament organizer. Tests exist to freeze that surface. Everything else — how a service is wired, what it logs, which internals it touches — is implementation, and pinning it only makes the suite brittle without protecting anyone.

This document is the guide for writing tests in this repository. The short version lives in `CLAUDE.md` rule 8; this is the full reasoning, the checklist, and the policy for existing code.

## Test categories

The six test projects follow the dependency direction of the solution:

- `Basil.Domain.Tests` and `Basil.Protocol.Tests` — pure logic and wire format. No mocks, no database, no host. Domain tests assert value calculators and enums; protocol tests assert byte-for-byte packet layouts.
- `Basil.Application.Tests` — use cases, packet handlers, and session state. Dependencies are mocked ports (`NSubstitute`) or in-memory fakes from `MultiplayerTestSupport`.
- `Basil.Infrastructure.Tests` — the implementations of those ports: SQLite repositories against a real temp database (`SqliteFixture`), filesystem services against real temp directories, media codecs, and the IRC TCP session.
- `Basil.IntegrationTests` — a real ASP.NET Core host over an in-memory `TestServer` transport. Asserts HTTP status codes, envelope shapes, SSE streams, and the admin-key gate end to end.
- `Basil.ArchitectureTests` — enforces the dependency-direction rules with NetArchTest; a violation fails CI, not just review.

A test belongs in the layer where the behavior is defined. Protocol bytes are tested in `Basil.Protocol.Tests`, not through an HTTP endpoint; a route that merely forwards to a service is tested in `Basil.IntegrationTests`, with the service's logic unit-tested in `Basil.Application.Tests`.

## Writing guidelines

### Contract first

Tests pin observable behavior. The same Implementation Test used for XML docs applies: if the whole implementation were swapped out but the behavior stayed the same, would the test still pass? If not, it is testing implementation.

Contract in Basil, assert it exactly:

- bancho packet bytes (`ServerPacketWriterTests` is the exemplar)
- IRC wire text and numerics (channel joins, parts, numeric replies)
- HTTP status codes, envelope shape, and JSON field names
- error codes (`invalid-request`, `incorrect-credentials`)
- multiplayer slot, team, host, referee, and access-control state transitions
- BasilBot `!mp` replies — these are user-visible and documented; a wording change is a public-behavior change and should fail the test

Not contract, do not assert word-for-word:

- Serilog messages and debug output
- exception message text (unless it reaches an API response)
- internal diagnostic strings
- the count or ordering of internal calls (`mock.Received(...)`), unless the call itself is the observable outcome

### Centralize contract literals

User-visible reply text is owned by the code that emits it, not by the tests. `MpReplies` in `src/Basil.Application/Services/Bot/` holds every `!mp` reply as a `const`; the services emit those constants and the tests assert against the same symbols. A contract string is never inlined in a test and never copy-pasted across the production call sites. Changing the wording is a deliberate, one-line edit to a named, documented constant, and the tests keep production and test pinned to the same symbol so they cannot drift.

```csharp
// Good — the service emits and the test asserts the same production constant.
Assert.Equal(MpReplies.MatchLocked, reply);

// Bad — the literal is scattered across the service and test call sites,
// and any wording tweak has to find and hit them all.
Assert.Equal("Locked the match", reply);
```

The same rule applies to any contract text a client sees — bancho packet strings, IRC messages, envelope error messages. When a test must reference a contract string, that string lives in the code that owns it.

### One behavior per test

Each test has one reason to fail. A test named `Login()` that asserts session state, a token, a registry entry, a log line, and a packet stream makes any failure ambiguous. Split it into `Login_ShouldCreateSession`, `Login_ShouldGenerateToken`, `Login_ShouldRegisterSession`, and so on.

Name tests `Condition_Should_ExpectedBehavior`: `JoinChannel_ShouldBroadcastJoinPacket`, `Kick_ShouldRemovePlayerFromMatch`, `Login_ShouldRejectIncorrectPassword`.

### Independence

Every test runs alone and in any order. No test may rely on state another test created, and no test may mutate shared state that another reads. This is why the shared fixtures are built per test class or per test, never as static singletons.

### Determinism

No real time, randomness, or GUIDs. Inject a port (`IClock`, `IRandom`, `IGuidGenerator`) when production uses `DateTime.UtcNow`, `Random.Shared`, or `Guid.NewGuid()` — adding the port to production if it does not have one. A test that passes today and fails tomorrow because a clock ticked is not a test.

No `Thread.Sleep`/`Task.Delay` to wait out asynchronous work. Use a deterministic signal: a `TaskCompletionSource`, a poll-with-timeout loop, or awaiting the async API directly. The one documented exception is a race-reproduction test that deliberately widens a timing window so an interleaving is reliably observed — see `MatchSessionRaceTests`, which states this in its doc comment.

### No environment dependence

No hardcoded absolute paths, no reliance on `CultureInfo.CurrentCulture` or `TimeZoneInfo.Local`. Temp directories and SQLite files are created and cleaned up per test, never assumed to pre-exist.

### Short Arrange

A fixture or builder beats fifty lines of setup per test. `MultiplayerTestSupport.Fixture`, `SqliteFixture`, and the integration `TestDoubles` exist for exactly this. When a test needs setup that another would share, extend the fixture rather than copying the block.

### Test through the public API only

No reflection into private members. If a private method is too complex to test through its callers, it should be a class with its own public surface and its own tests.

### Cover the edges

Happy path coverage alone misses where the bugs live. Every behavior should be exercised against: null, empty, duplicate, already-exists, offline, permission-denied, full match, banned, and overflow where they apply. Assert exceptions with `Assert.ThrowsAsync<T>` rather than letting the test crash.

### Regression first

Every bug fix ships with a regression test that reproduces the bug before the fix. That test is the guarantee the bug does not return.

## Examples

| Smell | Instead of | Write |
|-------|------------|-------|
| Log wording | `Assert.Equal("Player joined channel #osu", logger.Messages[0])` | `Assert.True(session.InChannel("#osu"))`, or `Assert.Contains("joined", logger.Messages[0])` if the message itself is the only signal |
| Multi-assert | one test asserting login + token + registry + packet | one test per outcome |
| Magic number | `Assert.Equal(17, packet.Length)` | `Assert.Equal(expected.Length, actual.Length)` or `const int HeaderSize = 7;` |
| Vague assert | `Assert.True(result)` | `Assert.Equal(LoginResult.Success, result)` |
| Implementation check | `loginService.Mock.Received(2).CallUserRepo()` | assert the observable outcome, e.g. the user session exists and is online |
| Mock sprawl | six collaborators mocked for one test | check the class under test — it may be doing too much; mock only its direct dependencies |

## Current known debt

The existing test suite predates these guidelines. Until the suite is fully migrated, contributors may encounter tests that do not follow the recommended style. Known areas of technical debt include:

- exact assertions against log messages
- inconsistent test naming
- mixed Arrange/Act/Assert sections
- duplicate setup code across fixtures
- tests that verify implementation details instead of observable behavior
- XML doc comments that describe provenance ("Ported from bancho.py's...") or mechanism instead of the behavior being verified

New tests should follow the guidelines in this document. Existing tests should be migrated opportunistically rather than rewritten solely for style.

## Migration policy

When modifying an existing test:

- Prefer leaving it better than you found it.
- Preserve behavior.
- Avoid large style-only rewrites.
- If substantial cleanup is needed, perform it in a dedicated pull request.

## Related code

- `tests/` — the six test projects described above
- `src/Basil.Application/Services/Bot/MpReplies.cs` — the production home of every `!mp` reply constant
- `tests/Basil.Application.Tests/Packets/MultiplayerTestSupport.cs` — shared multiplayer fixture
- `tests/Basil.Infrastructure.Tests/Persistence/SqliteFixture.cs` — per-class temp SQLite database
- `tests/Basil.IntegrationTests/TestDoubles.cs` and `InMemorySettingsRepository.cs` — shared endpoint test doubles
- `CLAUDE.md` rule 8 — the condensed version of these guidelines

## See also

- [`docs/architecture.md`](architecture.md): the dependency direction these tests enforce
- [`docs-guideline.md`](docs-guideline.md): how the docs themselves are written
