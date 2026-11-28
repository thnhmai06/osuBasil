# Documentation guidelines

These guidelines apply to every Markdown file under `docs/`, regardless of audience. They cover:

* `for-client/`
* `for-technicians/`
* `for-developers/`
* `for-agents/`

They are stored under `for-developers/` because developers are the most common documentation authors, but they govern the entire documentation tree.

Site navigation, and which page owns each topic, is defined in [`index.md`](../index.md). These guidelines apply rule 7 of
[`AGENTS.md`](../../AGENTS.md); if the two differ, `AGENTS.md` wins.

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
* Write in plain prose, one idea per paragraph. Use a list only for data that is list-shaped (steps in order, a set of
  names, a table of values), never to chop an explanation into fragments.

Documentation should describe the system as it actually works, not how it is intended to work in the future.

## Terminology

Use the project's established terminology consistently.

Use:

* **Basil**
* **osu! stable client**
* **osu! lazer**
* **osu! (stable) protocol**
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

* [`ServerOptions`](../../Basil/ServerOptions.cs)
* `PATCH /v1/settings/{category}`
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

A page explains **why** the system exists, **what** it is and **how** its parts fit together, in that order, and starts
with an overview before any implementation detail. A page generally covers:

1. an **overview**: what the topic is and why it exists;
2. the **public contract**: what a client, operator or caller can rely on;
3. the **design or implementation**: how it works, with the rationale where the reasoning is not obvious;
4. the **invariants and limitations** that must hold;
5. **related code** and **see also** links.

Not every page needs every section. Use only the structure that fits the topic, and keep one major topic per file.

Keep the public contract apart from the implementation: contract sentences must stay true if the implementation is
replaced, and implementation detail goes in its own Design or Implementation section. Prefer a diagram for a flow of
several steps, and a short example over a description. Do not paste large amounts of source code; link to the file.

A developer-facing page includes a `Related code` section when source locations help contributors find the
implementation.

## Generated references

The HTTP API, the BasilBot commands and the IRC server write their own reference from the running application, and the
Markdown documentation never copies them. Do not describe routes, request schemas, status codes per route, command lists
or the commands an IRC server answers on a page of its own: link to the generated reference
(`api.<domain>/docs/`) and explain only why the surface exists and how it fits together. The routes the osu! (stable)
client calls have no generated reference; [`osu-stable.md`](osu-stable.md) owns their description. The summaries and
descriptions that feed the generated API reference describe the public contract only, as `AGENTS.md` (rule 5) sets out.

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
Basil:Server:Domain
Logging:File:Retention:MaxAge
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
