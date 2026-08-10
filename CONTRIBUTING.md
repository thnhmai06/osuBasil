# Contributing to Basil

<img src="./assets/banner.png" alt="osuBasil Banner" width="727">

Thank you for considering contributing to Basil. Whether you are fixing a typo, improving the documentation, fixing a
bug, or building a new feature, your contribution is welcome.

This page is the entry point for contributors. It explains what to read before changing the code, how to set up a
development environment, and what a pull request is expected to meet.

The documentation has three different purposes:

* The `README` answers **"What is Basil?"**
* This page answers **"How do I contribute?"**
* `docs/` answers **"How does Basil work?"**

## Read these first

Before changing the code, read these documents in order:

1. [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md): explains the project structure,
   dependency direction, layer responsibilities, and major request flows. Read this before making a cross-layer change.
2. [`docs/for-developers/working-scopes.md`](docs/for-developers/working-scopes.md): defines what Basil supports and
   what it deliberately does not support. Do not assume a feature missing from Basil was accidentally omitted from
   bancho.py.
3. [`docs/for-developers/testing.md`](docs/for-developers/testing.md): defines what tests should protect and how new
   tests should be written.
4. [`docs/for-developers/docs-guideline.md`](docs/for-developers/docs-guideline.md): explains how the documentation is
   organized and maintained.
5. [`docs/index.md`](docs/index.md): the complete documentation map.

For the complete HTTP API and BasilBot command reference, use the generated documentation at `api.<domain>/docs/` on a
running instance, or the published copy on [GitHub Pages](https://thnhmai06.github.io/osuBasil/).

## Development setup

See [`docs/for-developers/development.md`](docs/for-developers/development.md) for the complete development workflow,
including:

* setting up the [.NET SDK](https://dotnet.microsoft.com/en-us/download/dotnet)
* cloning and building Basil
* running the server locally
* running the test suite
* connecting an osu! client
* working with development dependencies

Follow that guide rather than creating an ad-hoc local setup.

## Project structure

```text
src/
    the five production projects

tests/
    one test project per source project,
    plus integration and architecture tests

docs/
    documentation split by audience:
    for-client/
    for-technicians/
    for-developers/
    for-agents/
```

The five `src/` projects form a layered monolith.

`Basil.Web` sits at the outermost layer, while dependencies point inward toward the domain. The dependency direction is
enforced by [`tests/Basil.ArchitectureTests`](tests/Basil.ArchitectureTests).

See [`docs/for-developers/architecture.md`](docs/for-developers/architecture.md) for the responsibilities of each
project and guidance on where new code belongs.

## Coding guidelines

Do not introduce new conventions when an existing project convention already covers the case.

* Formatting and analyzer rules are defined by the repository-level [`.editorconfig`](.editorconfig).
* Development rules are collected in [`CLAUDE.md`](CLAUDE.md).
* XML documentation rules, user-visible response strings, and testing conventions are covered by [`CLAUDE.md`](CLAUDE.md).
* Public members should have XML documentation describing their observable behavior rather than their implementation.

When in doubt, follow the existing architecture and the authoritative developer documentation before introducing a new
pattern.

## Testing

New behavior should have tests where appropriate.

The basic rule is:

> If a player, client, tournament tool, or another system can observe it, treat it as a contract and test it. Do not pin
> tests to implementation details unless the implementation itself is the contract.

See [`docs/for-developers/testing.md`](docs/for-developers/testing.md) for the complete testing policy.

The six test projects are:

```text
Basil.Domain.Tests
Basil.Protocol.Tests
Basil.Application.Tests
Basil.Infrastructure.Tests
Basil.IntegrationTests
Basil.ArchitectureTests
```

During development, run the smallest relevant test project first:

```bash
dotnet test tests/Basil.Application.Tests
```

Run the complete suite before opening a pull request:

```bash
dotnet test
```

## Documentation

Documentation is part of the change when behavior changes.

Update the relevant documentation when a change affects:

* user-visible behavior
* HTTP APIs
* chat commands
* configuration
* deployment
* authentication
* protocols
* architecture
* supported or excluded functionality
* developer workflows

Follow [`docs/for-developers/docs-guideline.md`](docs/for-developers/docs-guideline.md) when modifying `docs/`.

Do not duplicate generated API or BasilBot command reference material in Markdown. Use the generated OpenAPI
documentation as the detailed reference and keep Markdown focused on behavior, architecture, and operational guidance.

If a change modifies an authoritative topic, update its authoritative document in the same change.

## Pull request requirements

Before opening a pull request, verify:

* [ ] `dotnet build --configuration Release` succeeds.
* [ ] `dotnet test` passes.
* [ ] New behavior has appropriate tests.
* [ ] Documentation is updated when the change affects documented behavior.
* [ ] The change respects the project's architecture and scope.
* [ ] The PR has a focused purpose.
* [ ] Unrelated changes have been separated into another PR.

CI runs restore, build, and test across the solution on every push.

A failing CI check should therefore be treated as a real problem rather than something to ignore.

Keep pull requests small enough to review confidently.

## Commit messages

Basil uses [Conventional Commits](https://www.conventionalcommits.org/).

The subject uses:

```text
type(scope): imperative summary
```

For example:

```text
feat(irc): gate JOIN on referee status
```

The scope is optional.

Common types are:

```text
feat:     new feature
fix:      bug fix
refactor: behavior-preserving code change
docs:     documentation
test:     tests
perf:     performance
chore:    maintenance, tooling, dependencies
build:    build system or packaging
ci:       CI configuration
```

Use lowercase types and keep the summary concise.

Append `[skip ci]` to the commit subject when CI should intentionally be skipped:

```text
chore: fix typos [skip ci]
```

Use `[skip ci]` only when the change genuinely does not require CI validation.

## Reporting issues

When reporting a bug, include enough information to reproduce it:

* Basil version or commit
* operating system
* steps to reproduce
* expected behavior
* actual behavior

For server-side issues, include relevant log lines from `Logs/` when available.

Because Basil can operate fully offline, reports involving osu! clients should also distinguish between the two
observable sides of the system:

* what the osu! client observed
* what the Basil HTTP/tournament API reported

This distinction often makes protocol and state-transition bugs significantly easier to diagnose.

Avoid pasting unrelated or sensitive configuration data into an issue.

## Code of Conduct

Basil has a [Code of Conduct](CODE_OF_CONDUCT.md).

By participating in the project, you agree to follow its terms.
