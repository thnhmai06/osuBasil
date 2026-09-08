# Documentation guidelines

These guidelines apply to every Markdown file under `docs/`, regardless of audience. They cover:

* `for-client/`
* `for-technicians/`
* `for-developers/`
* `for-agents/`

They are stored under `for-developers/` because developers are the most common documentation authors, but they govern the entire documentation tree.

Site navigation is defined in [`index.md`](../index.md).

## Purpose

Documentation should help readers understand and use Basil efficiently.

Each page should have one clear scope. Do not combine unrelated topics into a single page.

When a topic already has an authoritative page, link to it instead of duplicating its content.

## Writing style

Write clear, concise English.

* Prefer active voice.
* Explain **why** as well as **how** when the reasoning affects how the system should be used or changed.
* Assume basic technical knowledge.
* Prefer concrete statements over vague descriptions.
* Avoid filler, repetition, and marketing language.
* State important limitations and invariants explicitly.

Documentation should describe the system as it actually works, not how it is intended to work in the future.

## Terminology

Use the project's established terminology consistently.

Use:

* **Basil**
* **osu! stable client**
* **osu! lazer**
* **Bancho protocol**
* **API**
* **beatmap**
* **player**
* **match**
* **Menu Icon**

Do not introduce alternate names or abbreviations for established concepts.

When a technical identifier has an official name in the codebase, prefer that name over a newly invented term.

## Formatting

Use ATX headings:

```md
# Page title
## Section
### Subsection
```

Use sentence case for headings.

Keep paragraphs short. Use lists when information is naturally enumerated.

Use fenced code blocks with language identifiers:

```bash
dotnet run
```

Use inline code for technical identifiers, including:

* commands;
* configuration keys;
* file names;
* environment variables;
* API routes;
* class and method names.

For example:

* [`ServerOptions`](../../src/Basil.Application/Configurations/ServerOptions.cs)
* `PUT /settings/adminkey`
* `appsettings.json`

## Cross-references

Prefer linking to existing documentation instead of repeating information.

Use relative links:

```md
[Deployment](../for-technicians/deployment.md)
```

A page should not become a second copy of another page merely to provide context.

When another document is the authoritative source for a topic, link to it and keep only the context necessary to understand the current page.

## Code samples

Code samples must be:

* minimal;
* correct;
* representative of the current implementation.

Remove unrelated configuration and boilerplate.

Do not use examples that depend on behavior that no longer exists.

If a command or configuration example is version-sensitive, keep it synchronized with the current `main` branch.

## Screenshots

Use screenshots only when they communicate information that text cannot easily convey.

Do not use screenshots as substitutes for documenting configuration, commands, API behavior, or source code.

Avoid screenshots of code.

## Versioning

Documentation describes the current `main` branch.

When behavior changes, update the documentation to match the new implementation.

Remove obsolete instructions rather than preserving them as historical notes unless the historical information is itself useful.

Do not document unreleased or speculative behavior as if it were current behavior.

## Page structure

A page should generally answer these questions, in roughly this order:

1. **What is this?**
2. **Why does it exist?**
3. **How does it work or how is it used?**
4. **What important details, limitations, or invariants apply?**
5. **Where can I find related information?**

Not every page needs every section. Use only the structure that fits the topic.

Developer-facing pages should additionally identify relevant code when doing so helps contributors find the implementation.

## Accuracy and invariants

Documentation is part of the project's technical contract.

When a page describes behavior that code must preserve, state the invariant explicitly.

Examples include:

* which component is the source of truth;
* whether an operation is idempotent;
* which identifiers are stable;
* whether a feature is intentionally unsupported;
* whether two execution paths share the same implementation.

Avoid vague wording where a precise rule is possible.

## Configuration and API documentation

Use the exact configuration keys and API routes from the current implementation.

For example:

```text
Server:Domain
Bot:CommandPrefix
```

Do not replace implementation names with informal descriptions when readers need to locate the corresponding configuration or code.

When documenting an API, distinguish clearly between:

* required and optional fields;
* supported and unsupported behavior;
* local and external dependencies;
* success and failure behavior.

## Audience

Write for the audience of the current directory.

### `for-client/`

Explain how players or client operators use Basil-compatible client functionality.

Do not expose implementation details unless they are necessary for client configuration or troubleshooting.

### `for-technicians/`

Explain installation, configuration, deployment, operation, maintenance, and troubleshooting.

Assume the reader is responsible for running Basil but does not need to modify its source code.

### `for-developers/`

Explain architecture, implementation contracts, development workflows, testing, and code-level design decisions.

Prefer documenting invariants and ownership boundaries over repeating obvious code behavior.

### `for-agents/`

Provide precise information needed by automated coding agents and other tooling.

Favor explicit contracts, constraints, file locations, and verification requirements over prose explanations.

## Contributing

Documentation changes should be made together with the code or configuration change that caused them.

If a change affects any of the following:

* behavior;
* APIs;
* configuration;
* deployment;
* client workflows;
* operational procedures;

update the relevant documentation in the same pull request.

Documentation should not be treated as follow-up work after the implementation is complete.

## Related documentation

* [`index.md`](../index.md): documentation navigation
