# Testing

## Why

Basil exposes hard contracts: the bancho binary stream, IRC wire text, the HTTP API, multiplayer state transitions and
user-visible BasilBot replies. Real osu! clients, IRC clients and tournament tools consume them, so a test must verify
**observable behaviour**, not how it happens to be implemented.

> If the implementation were replaced while the externally observable behaviour stayed identical, would this test still
> pass?

If not, the test is coupled to an implementation detail.

## Test layers

| Layer | Project | Checks |
|---|---|---|
| Domain | `tests/Domain` | rules and values, no I/O |
| Application | `tests/Application/<Module>` | outcomes, `Require`, locks, sessions, restrictions, room state |
| Infrastructure | `tests/Infrastructure/<Module>` | repositories and storage against PostgreSQL and temporary files |
| Protocol | `tests/Protocol/{Bancho,Irc}` | wire bytes and text, round-tripped from hand-written pairs |
| Host | `tests/Hosts/<Transport>` | the real transport in process over real sockets: Bancho packets, IRC lines, REST status/envelope/SSE |
| End-to-end | `tests/Hosts/Host` | several hosts together through the simulated clients |
| Architecture | `tests/Architecture` | project graph, layering, composition and outcomes |

Host test projects run the real server (`BasilHost`) in the test process: Kestrel and IRC on random ports, the real
`Basil.Host` composition, a `FakeTimeProvider`, temporary data directories and a PostgreSQL database of their own.
Clients talk to it over the network exactly as real ones do.

Shared support lives under `tests/Support`:

* `Basil.Testing.Clients` — `OsuClient` (Bancho packets and legacy web endpoints), `IrcClient` (CAP, SASL, numerics,
  IRCv3 tags), `RestClient` and `SseReader` (Bearer or anonymous, ordered events, cancellation), and `BasilHost`.
* `Basil.Testing.Persistence` — `PersistenceFixture`, which starts a PostgreSQL container or uses the server named by
  `BASIL_TEST_POSTGRES`, and creates an empty database per test.
* `Basil.Testing` — shared application fixtures (`RoomFixture`, `ScoreSubmissionFixture`, `BeatmapsetWorld`,
  `TestApplication`).

Sample content lives in `tests/Assets`. Do not use a real `Data/` directory or a developer database.

## Running tests

Run **one project at a time**; never the whole solution in one command:

```bash
dotnet test tests/Hosts/Rest -c Release --no-build --nologo -v q
dotnet test tests/Application/Users --filter "FullyQualifiedName~<TestClassName>"
```

Projects that need PostgreSQL use Docker by default. Point the client at the daemon when it is not on the default
socket:

```bash
export DOCKER_HOST=unix:///home/haith/.docker/desktop/docker.sock
```

To use an existing server instead of a container, set `BASIL_TEST_POSTGRES` to a connection string.

`tests/Architecture` also runs the container check: it calls `AddBasil()` and `AddBot()`, builds the provider with
`ValidateOnBuild`, resolves every registration (factory lambdas included) and checks that every event handler is
registered, against a deadline. A dependency cycle shows up as a hang, not as an error.

## Writing tests

### Specification first

An expected value comes from `AGENTS.md`, the osu! rules, the protocol or bancho.py behaviour — never from running the
code under test and pasting its output. A red test means the code is wrong: report it, do not edit the test green.

### Blackbox and whitebox

Derive cases from the contract (equivalence classes, boundaries, decision tables, state transitions), then from the
structure (every branch and guard). Coverage is used only to find branches no case reached.

### Pin the right thing

* Pin **properties** of computed values (valid, finite, monotone under a modifier), not the number.
* Pin the exact value only when the value is a contract: wire bytes, accuracy and grade of the ruleset, the score
  submission checksum. Use hand-written input/output pairs, never a copy of the formula.
* A refusal checks the missing mask of `Result<TOutcome>`; every `Require` has a test that goes red when it is removed.
* One behaviour per test, one reason for failure.
* Test through public APIs; do not assert log text, debug output or internal call counts.
* Add a regression test for every reproducible bug.

### Keep tests deterministic

* No real-time sleep: advance the `FakeTimeProvider` and wait for the observable effect.
* No uncontrolled randomness; a generated id is not an expected value.
* No shared mutable state between tests; each test creates its own database and data directories.
* A failing assertion names the value it expected and the value it saw.

### Contract strings

User-visible reply text is public behaviour but its wording belongs to the operator. Production types own reply
constants (`MpReplies`, `IrcReplies`); tests reference those constants instead of duplicating literals, and assert the
reply's key or rendered pattern, not a copy of the current English sentence.

## Architecture tests

`tests/Architecture` enforces what the compiler does not: the module reference graph, layering inside a module, the
absence of SQL text, the outcome convention and the composition. `HostGraphTests` enforces the host rules: every
transport references `Basil.Host.Shared`, transports never reference each other, and Rest/Cli never reference
`Protocol.Bancho`. Each such rule has a positive control — an assertion that the opposite claim is detected — so a
rule that always passes is itself caught.

## REST contract tests (Postman)

The HTTP contract is also checked from outside with Postman, against a running server. The collection, test scripts and
environment live in `tests/Hosts/Rest/postman/`. OpenAPI generated from the code is the source of truth. xUnit tests
own behaviour; Postman owns the HTTP contract shape: routes, status codes, envelope, schema, and the public/token matrix.

## Related code

* `tests/Support/Basil.Testing.Clients/` — simulated clients and the in-process host.
* `tests/Support/Basil.Testing.Persistence/` — the PostgreSQL fixture.
* `tests/Architecture/` — dependency, host and composition rules.
* `tests/Hosts/Rest/postman/` — the HTTP contract collection.

## See also

* [`architecture.md`](architecture.md) — dependency rules the architecture tests enforce
* [`development.md`](development.md) — building and running locally
* [`../for-agents/identity-permissions.md`](../for-agents/identity-permissions.md) — permission rules behind refusal tests
