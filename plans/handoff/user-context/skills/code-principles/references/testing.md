# Testing

**Tests pin observable contracts, not implementation details.**

## The Implementation Test for tests

> If the implementation changed while the observable behaviour stayed the same, would this test still pass?

If it wouldn't, the test is probably checking the implementation.

**Contracts** worth pinning include:

- wire bytes of a protocol, and protocol text with its numeric codes;
- HTTP status codes, JSON and envelope shapes, API error codes;
- user-visible replies;
- state transitions of the domain (a room's participants after a kick, an order's state after payment);
- the outcome a member returns, and the exceptions that are part of its contract.

**Don't normally assert:**

- log messages, debug output or internal diagnostic strings;
- exception messages that never reach a client;
- internal call counts or call order;
- private state reached through reflection.

## Rules

- **One behaviour per test, one reason to fail.**
- **Deterministic.** No real-time sleeps. Control time through `TimeProvider` (a fake time provider). No
  uncontrolled randomness. No dependence on the environment: machine, locale, time zone, network, or test order.
- **Independent.** Tests share no mutable state, and each one sets up what it needs.
- **Through public APIs.** Test the contract the way a caller uses it. To reach `internal` types, expose them
  to the test project with `InternalsVisibleTo`; don't make them public.
- **Short Arrange sections.** Long setup means the API is hard to use, or the test checks too much.
- **Cover the edges**: empty, `null`, boundaries, closed or disposed objects, re-entry, idempotent repeats,
  concurrent callers where the contract promises safety.
- **A regression test for every reproducible bug.** Reproduce it as a failing test first, then fix it.
- **Reference contract strings from production code.** A user-visible string is centralized in the code that
  owns it, and tests use that constant instead of duplicating the literal.
- **Don't write unit stubs that bypass the core behaviour.** Work that needs real files, licensed engines or
  native libraries belongs in integration tests. Pure helpers lifted out of that work can be unit-tested.

## Naming and layout

- Test methods are named `Method_Scenario_ExpectedResult`, for example
  `Load_SettingsFileNotFound_ReturnsFalse` or `JoinAsync_RoomFull_ReturnsRoomFull`.
- Test projects mirror the source projects (`Project.Tests` for `Project`), and test files mirror the folders
  of the code they test.
- Follow the repository's test stack. For a new .NET project, use xUnit v3 (`xunit.v3`).

Microsoft: [Unit testing best practices for .NET](https://learn.microsoft.com/dotnet/core/testing/unit-testing-best-practices)
(the same three-part test name, Arrange-Act-Assert, no infrastructure in unit tests),
[Testing with FakeTimeProvider](https://learn.microsoft.com/dotnet/core/extensions/timeprovider-testing).

## Verifying work

- While iterating, run the smallest relevant tests. Before you call the change finished, run the full suite
  and a Release build.
- When a change crosses layers or touches project references, run the architecture tests.
- "Make it work" isn't a success criterion. Turn the task into checks: "add validation" means tests for
  invalid input, then make them pass. "Fix the bug" means a test that reproduces it, then make it pass.
  "Refactor X" means the same tests pass before and after.
