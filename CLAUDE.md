# CLAUDE.md

This file provides guidance to Claude Code (`claude.ai/code`) when working in this repository.

## What this is

Basil is a private [osu!](https://osu.ppy.sh/) stable server focused on offline multiplayer tournaments.

It is built on [bancho.py](https://github.com/osuAkatsuki/bancho.py), but it is **not a full bancho.py port**. Basil deliberately has a smaller feature surface. pp calculation, friends, clans, a general-purpose public v1/v2 API, the full bancho.py chat-command set, and other unrelated features are intentionally out of scope.

Before porting or recreating anything from bancho.py, read:

* [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md) — authoritative feature scope
* [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) — system structure and dependency direction
* [`README.md`](README.md) — project overview

The generated HTTP API and BasilBot command reference is available at `api.<domain>/docs/` on a running instance and on [GitHub Pages](https://thnhmai06.github.io/osuBasil/). Do not create a second hand-written API reference in Markdown.

## Rules

### 1. Think before coding

**Do not assume. Do not hide uncertainty. Surface tradeoffs.**

Before implementing:

* State important assumptions.
* If multiple interpretations are plausible, identify them.
* If a simpler solution exists, prefer it and explain the tradeoff.
* If a requirement is genuinely ambiguous, ask before coding.

Do not silently invent requirements.

### 2. Simplicity first

Implement the smallest solution that satisfies the request.

Do not add:

* speculative features
* unused abstractions
* unnecessary configurability
* future-proofing without a concrete requirement
* error handling for impossible states

If a solution is significantly more complicated than the problem requires, simplify it.

### 3. Surgical changes

Change only what the task requires.

When modifying existing code:

* preserve existing behavior unless the task changes it
* match the surrounding style
* do not refactor unrelated code
* do not rewrite adjacent comments or formatting
* do not remove unrelated dead code

Remove imports, variables, methods, or other artifacts only when **your own changes** make them unnecessary.

Every changed line should have a clear connection to the task.

### 4. Goal-driven execution

Define a concrete success criterion before implementing.

Examples:

```text
"Add validation"
→ Add tests for invalid input, then make them pass.

"Fix the bug"
→ Reproduce the bug with a regression test, then make it pass.

"Refactor X"
→ Verify behavior before and after the refactor.
```

For multi-step work, use a short plan:

```text
1. [change] → verify: [check]
2. [change] → verify: [check]
3. [change] → verify: [check]
```

Do not stop at "the code looks right". Verify the result.

### 5. API documentation

Every `.WithSummary` and `.WithDescription` on an `api.` host route describes the **public API contract**, never its implementation.

Apply the Implementation Test:

> If the implementation were replaced while the endpoint's behavior remained identical, would this sentence need to change?

If yes, it is implementation detail and does not belong in the API documentation.

#### Summary

Use one sentence describing what the endpoint does.

```csharp
.WithSummary("Retrieve a match report.")
```

Do not describe storage, frameworks, services, or internal mechanisms.

#### Description

Use Markdown paragraphs in this order:

1. what the endpoint provides
2. important special cases
3. related or live endpoints
4. important errors

Do not restate information already represented by the OpenAPI schema, such as the HTTP method, path, ordinary parameter definitions, or declared response type.

Prefer raw strings for multi-paragraph descriptions.

Never document implementation details such as:

* SQLite
* Redis
* DI
* middleware
* `Channel<T>`
* background services
* cache keys
* internal file paths
* internal service or type names

The exception is when the mechanism itself is part of the public contract, for example:

```text
The response is streamed using Server-Sent Events.
```

### 6. XML documentation

Every `///` comment describes **responsibility and observable behavior**, never implementation.

Apply the same Implementation Test as rule 5.

#### Summary

Use one sentence answering:

> What is this member, and what does it do?

Do not simply restate its name.

#### Remarks

Use remarks only for additional caller-visible behavior that does not fit in the summary.

Never document:

* temporary files
* locks
* cache keys
* dictionaries
* reflection
* switch statements
* framework implementation
* dependency injection
* internal collaborators

Do not document historical implementation decisions unless the member exists specifically for backward compatibility.

`<param>` and `<returns>` describe the meaning of the value, not how it is produced.

Use `<see cref>` only when it improves navigation for the reader.

### 7. Markdown documentation

`docs/*.md` explains **why the system exists, how it works, and how its components fit together**.

XML documentation is reference material. Markdown documentation is architecture and guidance.

Follow these rules:

* Explain **Why → What → How**.
* Keep one major topic per file.
* Start with an overview before implementation details.
* Separate public contract from implementation.
* Put implementation details in a dedicated Design/Implementation section.
* Include design rationale where useful.
* Prefer diagrams for multi-step flows.
* State important system invariants explicitly.
* Use short illustrative examples.
* Do not paste large amounts of source code.
* Link to related documentation instead of duplicating it.
* Include a `Related code` section when source locations are useful.
* Do not duplicate the generated HTTP API or BasilBot command reference.

Authoritative topic ownership is defined by [`docs/index.md`](docs/index.md).

If a topic already has an authoritative document, update that document instead of creating a competing description elsewhere.

### 8. Test writing

**Tests pin observable contracts, not implementation details.**

Apply the Implementation Test:

> If the implementation changed but the observable behavior stayed the same, would this test still pass?

If not, the test is probably testing implementation.

Contract examples include:

* bancho packet bytes
* IRC wire text and numerics
* HTTP status codes
* JSON and envelope shapes
* API error codes
* user-visible BasilBot replies
* multiplayer state transitions

Do not normally assert:

* Serilog messages
* debug output
* internal diagnostic strings
* exception messages that never reach a client
* internal call counts or ordering

See [`docs/for-developers/testing.md`](docs/for-developers/testing.md) for the complete policy.

Important rules:

* one behavior per test
* one reason for failure
* deterministic tests
* no real-time sleeps
* no uncontrolled randomness
* no environment-dependent state
* test through public APIs
* cover relevant edge cases
* add a regression test for every reproducible bug fix

User-visible contract strings must be centralized in the production code that owns them. Tests reference those constants rather than duplicating literals.

### 9. User-visible reply strings

User-visible chat text is public behavior.

Each chat surface owns its reply constants.

Examples:

* `MpReplies` for `!mp`
* `IrcReplies` for IRC responses

Do not scatter user-visible strings across handlers.

Changing the wording of a user-visible response is a contract change. Update the named production constant and its tests deliberately.

Internal diagnostics and logs are not subject to this rule.

## Commands

Restore and build:

```bash
dotnet restore
dotnet build --configuration Release
```

Run Basil locally:

```bash
dotnet run --project src/Basil.Web
```

Run all tests:

```bash
dotnet test
```

Run one test project:

```bash
dotnet test tests/Basil.Application.Tests
```

Run a specific test class:

```bash
dotnet test tests/Basil.Application.Tests \
  --filter "FullyQualifiedName~MatchSessionRaceTests"
```

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow.

See [`docs/for-technicians/configuration.md`](docs/for-technicians/configuration.md) for runtime configuration and data directories.

See [`docs/for-technicians/docker.md`](docs/for-technicians/docker.md) for Docker.

## Architecture

Basil is a five-project Clean Architecture monolith.

```text
Basil.Domain
    ↓
Basil.Application
    ↓
Basil.Infrastructure
    ↓
Basil.Web
```

`Basil.Protocol` is an independent protocol layer used by the application and web layers.

The intended project dependencies are:

```text
Basil.Domain
    no project references

Basil.Protocol
    no project references

Basil.Application
    → Domain
    → Protocol

Basil.Infrastructure
    → Application
    → Domain

Basil.Web
    → Application
    → Infrastructure
    → Protocol
```

`Basil.ArchitectureTests` enforces these boundaries.

Read [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) before making a cross-layer change.

### Important invariants

#### Multiplayer concurrency

`MatchSession` is mutable shared state.

Operations that read and then mutate match state must follow the match's existing synchronization model and hold the match lock across the complete state-transition and broadcast sequence.

Do not introduce a second synchronization mechanism for the same state.

Do not hold the match lock across long-lived or unrelated waits.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) for the complete model.

#### Authority

Referee, host, and creator are separate concepts.

Do not treat them as interchangeable.

The creator has permanent match authority. Referee membership and host state have different lifecycles.

See [`docs/for-developers/multiplayer.md`](docs/for-developers/multiplayer.md) before changing match permissions or `!mp` authorization.

#### No pp in gameplay

Basil does not use performance points for scoring, leaderboards, or match win conditions.

Locally calculated difficulty/star-rating information is display-only.

Do not introduce pp-dependent gameplay behavior.

#### Stable protocol

Bancho packet layouts are wire contracts.

Changes to packet encoding or decoding must be treated as protocol changes and covered by protocol tests.

#### SSE

Live HTTP streams use dedicated `/live` endpoints.

Do not introduce content negotiation between JSON and SSE on the same route.

See [`docs/for-developers/sse.md`](docs/for-developers/sse.md).

#### API responses

The `api.` host uses a common response envelope for JSON responses.

The generated OpenAPI schema must describe the same contract that the runtime returns.

Do not manually work around the envelope in individual routes without first checking the API middleware and schema transformation rules.

See [`docs/for-client/response-envelope.md`](docs/for-client/response-envelope.md).

#### User-visible enums

Keep API record properties typed as their actual enum types.

Do not replace enums with `int` or `string` merely to influence serialization.

Serialization behavior belongs to the API contract and is documented separately.

#### Naming

Use `Privilege`, not `Priv`, for new code and public models.

Do not reintroduce the old abbreviation.

## Scope

Basil is deliberately smaller than bancho.py.

Do not implement a bancho.py feature simply because it exists upstream.

Before adding:

* a chat command
* a persistence model
* an API surface
* a social feature
* a scoring feature
* a compatibility endpoint

check [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md).

If the requested behavior conflicts with the documented scope, surface that conflict before coding.

## Repository documentation

Use [`docs/index.md`](docs/index.md) to find the authoritative document for a topic.

Important developer documents:

* [`architecture.md`](docs/for-developers/architecture.md) — architecture and dependency direction
* [`working-scopes.md`](docs/for-developers/working-scopes.md) — feature scope
* [`testing.md`](docs/for-developers/testing.md) — test policy
* [`development.md`](docs/for-developers/development.md) — local development
* [`multiplayer.md`](docs/for-developers/multiplayer.md) — match/session behavior
* [`sse.md`](docs/for-developers/sse.md) — live HTTP streams
* [`database.md`](docs/for-developers/database.md) — persistence design
* [`logging.md`](docs/for-developers/logging.md) — logging design
* [`docs-guideline.md`](docs/for-developers/docs-guideline.md) — documentation rules
* [`known-limitations.md`](docs/for-developers/known-limitations.md) — open RC items, unproven hypotheses, and the dependency inventory

Technician documentation:

* [`configuration.md`](docs/for-technicians/configuration.md) — configuration and data directories
* [`https.md`](docs/for-technicians/https.md) — TLS requirements
* [`docker.md`](docs/for-technicians/docker.md) — Docker deployment

Agent documentation:

* [`guidelines.md`](docs/for-agents/guidelines.md) — agent workflow
* [`domain.md`](docs/for-agents/domain.md) — domain-modeling guidance
* [`issue-tracker.md`](docs/for-agents/issue-tracker.md) — GitHub Issues workflow

## Agent skills

### Issue tracker

Issues live in GitHub Issues for [`thnhmai06/osuBasil`](https://github.com/thnhmai06/osuBasil).

See [`docs/for-agents/issue-tracker.md`](docs/for-agents/issue-tracker.md).

### Domain modeling

Domain-modeling work uses:

```text
CONTEXT.md
docs/adr/
```

These files are created lazily by the `/domain-modeling` workflow.

See [`docs/for-agents/domain.md`](docs/for-agents/domain.md).

## Final verification

Before considering a code change complete:

1. Run the smallest relevant tests while iterating.
2. Run the complete test suite for the final change.
3. Run a Release build when the change affects production code.
4. Check architecture tests for cross-layer changes.
5. Update authoritative documentation when behavior or design changes.
6. Review the final diff for unrelated changes.

The goal is not merely to produce compiling code. The goal is a verified change that respects Basil's architecture, scope, contracts, and documentation.
