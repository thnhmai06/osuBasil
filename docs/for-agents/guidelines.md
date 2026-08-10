# Guidelines for agents

## Purpose

This folder contains documentation for AI agents and automated development workflows working on Basil.

The repository's root [`CLAUDE.md`](../../CLAUDE.md) is the **authoritative instruction source for agents**. This page
does not replace it.

Before making code changes:

1. Read [`CLAUDE.md`](../../CLAUDE.md).
2. Read the relevant documentation listed below.
3. Check the existing implementation and tests.
4. Follow the documented scope and architectural constraints.
5. Make the smallest change that correctly solves the task.
6. Run the relevant tests before considering the work complete.

Do not treat this page as a substitute for the repository instructions or developer documentation.

---

## Documentation by question

The documentation is organized by audience in [`docs/index.md`](../../docs/index.md).

For code changes, start with the developer documentation:

| Question                                            | Read                                                       |
|-----------------------------------------------------|------------------------------------------------------------|
| How is the system structured?                       | [`architecture.md`](../for-developers/architecture.md)     |
| What is in scope and what is deliberately excluded? | [`working-scopes.md`](../for-developers/working-scopes.md) |
| How should tests be written?                        | [`testing.md`](../for-developers/testing.md)               |
| How should documentation be written?                | [`docs-guideline.md`](../for-developers/docs-guideline.md) |
| How does configuration and persistent data work?    | [`configuration.md`](../for-technicians/configuration.md)  |

Use the documentation as the design contract. If implementation and documentation appear to disagree, investigate the
discrepancy rather than silently inventing a new behaviour.

---

## Agent-specific documentation

This folder contains two documents for agent workflows.

### `domain.md`

Explains how engineering skills use Basil's domain documentation, including:

* `CONTEXT.md`;
* domain documentation;
* architecture decisions under `docs/adr/`;
* the `/domain-modeling` workflow.

Read this when working on domain modelling or making changes that introduce or modify domain concepts.

### `issue-tracker.md`

Explains how agents interact with the repository's GitHub Issues through `gh`.

Read this when a task involves:

* finding an existing issue;
* updating issue status;
* creating an issue;
* linking implementation work to an issue.

---

## Architectural invariants

The following rules are load-bearing. Do not violate them without explicitly changing the architecture and its
corresponding tests and documentation.

### Dependency direction

The dependency rule is enforced by architecture tests:

```text
Domain      → nothing
Protocol    → nothing
Application → Domain, Protocol
Infrastructure → Application
Web         → Domain, Protocol, Application, Infrastructure
```

`Basil.ArchitectureTests` enforces these boundaries in CI.

Do not introduce a dependency that points inward across these boundaries simply because it is convenient.

See [`architecture.md`](../for-developers/architecture.md).

### Multiplayer mutations

Multiplayer state mutations must hold [`MatchSession.Lock`](../../src/Basil.Application/Sessions/Multiplayer/MatchSession.cs).

Any handler, command, or route that performs a read-then-mutate operation on match slots must hold the per-match
semaphore across the complete operation:

```text
read → mutate → broadcast
```

Do not release the lock between these steps.

See [`bancho.md`](../for-developers/bancho.md) and [`multiplayer.md`](../for-developers/multiplayer.md).

### One source of truth

Each topic has one authoritative document.

For example:

* scope decisions → [`working-scopes.md`](../for-developers/working-scopes.md);
* configuration → [`configuration.md`](../for-technicians/configuration.md);
* TLS → [`https.md`](../for-technicians/https.md);
* privileges → [`privileges.md`](../for-developers/privileges.md).

Do not create a second document that defines the same behaviour differently.

When another document needs the information, link to the authoritative source instead of copying it.

### Client contract vs. server implementation

Keep client-facing and server-facing documentation separate.

* `for-client/` describes what clients need to know.
* `for-developers/` describes implementation and development details.
* `for-technicians/` describes deployment and operational procedures.
* `for-agents/` describes agent workflows and constraints.

When documenting a protocol or behaviour, put the information in the appropriate audience's documentation and cross-link
where necessary.

Do not duplicate the same contract across multiple audience folders.

---

## Scope boundaries

### No pp calculation

Basil does **not** calculate performance points.

Star rating and difficulty values are display-only and must not become gameplay-affecting dependencies.

Do not introduce pp calculation, pp-based gameplay logic, or hidden dependencies on pp calculation.

See [`working-scopes.md`](../for-developers/working-scopes.md) and [
`beatmap-ingestion.md`](../for-developers/beatmap-ingestion.md).

### Privilege terminology

Use the canonical type name:

```text
Privilege
```

Do not abbreviate it to `Priv`.

This applies to code, documentation, tests, and new APIs.

### User-visible reply strings

User-visible replies are centralized.

For multiplayer `!mp` replies, use:

```text
MpReplies
```

For IRC replies, use:

```text
IrcReplies
```

Do not introduce duplicate string literals for existing user-visible replies.

Tests should reference the same centralized symbols where appropriate so that production behaviour and test expectations
remain aligned.

See [`testing.md`](../for-developers/testing.md).

---

## Before changing code

Before implementing a change, determine:

1. **Which layer owns the behaviour?**
2. **Which existing abstraction already represents it?**
3. **Which documentation defines the intended behaviour?**
4. **Which tests currently enforce the behaviour?**
5. **Is the requested behaviour inside the project's declared scope?**
6. **Does the change introduce a new architectural dependency or invariant?**

Prefer extending an existing abstraction over introducing a parallel mechanism.

If the requested change conflicts with an existing documented invariant, stop and resolve the conflict explicitly rather
than silently weakening the invariant.

---

## After changing code

At minimum:

1. Run the tests relevant to the changed behaviour.
2. Run architecture tests when changing project dependencies or layer boundaries.
3. Update the authoritative documentation when behaviour or scope changes.
4. Check that no duplicate source of truth was introduced.
5. Review user-visible strings for consistency with the centralized reply definitions.
6. Keep the change focused on the requested behaviour.

A change is not complete merely because it compiles. The implementation, tests, architecture, and authoritative
documentation must remain consistent.

---

## See also

* [`CLAUDE.md`](../../CLAUDE.md): authoritative agent instructions
* [`domain.md`](domain.md): domain documentation and modelling workflow
* [`issue-tracker.md`](issue-tracker.md): GitHub Issues workflow
* [`architecture.md`](../for-developers/architecture.md): architecture and dependency rules
* [`working-scopes.md`](../for-developers/working-scopes.md): project scope
* [`testing.md`](../for-developers/testing.md): testing conventions
* [`docs-guideline.md`](../for-developers/docs-guideline.md): documentation conventions
