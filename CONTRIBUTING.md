# Contributing to Basil

<img src="./assets/banner.png" alt="osuBasil Banner" width="727">

Thank you for considering contributing to Basil. Whether you're fixing a typo, improving the docs, or building a new feature, your help is appreciated.

This page is the entry point for contributors. It tells you how to set up a development environment, what to read before you start, and what a pull request has to meet. The README answers "what is Basil?". These docs answer "how do I contribute?". The `docs/` folder answers "how does it work in detail?".

## Read these first

Before you open an editor, read the pages below in order. They will save you from making changes that the project deliberately doesn't want.

- [`docs/architecture.md`](docs/architecture.md): how the code is organized, the dependency rule between projects, and how a login or a match flows through the system. Read this before any cross-layer change.
- [`docs/working-scopes.md`](docs/working-scopes.md): what Basil supports versus what it intentionally leaves out. A lot of bancho.py features were cut on purpose; don't assume one is missing by accident.
- [`docs/testing.md`](docs/testing.md): what the tests pin and how to write new ones.
- [`docs/docs-guideline.md`](docs/docs-guideline.md): how documentation under `docs/` is written and structured.
- The full HTTP API and BasilBot chat command reference is generated from the source and served at `api.<domain>/docs/` on a running instance, or on [GitHub Pages](https://thnhmai06.github.io/osuBasil/) without running one.

## Development setup

For the full guide on running Basil locally, deploying it, and connecting an osu! client, see [`docs/run-deployment.md`](docs/run-deployment.md).

## Project structure

```
src/     the application, five projects
tests/   the test projects, one per source project plus integration and architecture tests
docs/    architecture and guides, one topic per file
```

The five `src/` projects form a layered monolith. `Basil.Web` sits on top and every reference points inward, a direction that `tests/Basil.ArchitectureTests` enforces. `docs/architecture.md` walks through each layer and where new code belongs.

## Coding guidelines

The conventions are already written down; follow them instead of inventing new ones.

- Formatting and analyzer settings come from `.editorconfig` at the repository root.
- The project's development rules, covering XML doc comments, user-visible reply strings, and test writing, are collected in `CLAUDE.md` at the repository root.
- Public members carry XML documentation that describes observable behavior, never the implementation. See rule 6 in `CLAUDE.md`.

## Testing guidelines

All new code should include tests where appropriate. Behavior that a player or a client can observe is contract and gets pinned; internal detail is not.

- `docs/testing.md` defines what counts as contract and the test-writing conventions.
- Run a single project's tests when you're iterating:

```bash
dotnet test tests/Basil.Application.Tests
```

- The six test projects are `Basil.Domain.Tests`, `Basil.Protocol.Tests`, `Basil.Application.Tests`, `Basil.Infrastructure.Tests`, `Basil.IntegrationTests`, and `Basil.ArchitectureTests`.

## Documentation guidelines

- Public APIs should include XML documentation.
- User-facing features and behavior changes should be documented under `docs/`, following `docs/docs-guideline.md`.
- If your code change alters behavior, APIs, configuration, deployment, or user workflows, update the related docs in the same pull request.

## Pull request process

Before opening a PR:

- The build succeeds with `dotnet build --configuration Release`.
- The tests pass with `dotnet test`.
- New functionality includes tests.
- Documentation is updated if the change affects behavior, APIs, configuration, or workflows.
- The PR stays focused on one thing. Split unrelated changes into separate PRs.

The CI workflow runs restore, build, and test across the whole solution on every push, so a red check means something is genuinely wrong. Keep the diff small and reviewable.

## Commit messages

Commits use [Conventional Commits](https://www.conventionalcommits.org/). The message starts with a lowercase type, an optional scope in parentheses, and a short imperative summary, for example `feat(irc): gate JOIN on referee status`.

Add `[skip ci]` as a prefix to the commit message if you don't want the CI to run.

Common types:

```
feat:     new feature
fix:      bug fix
refactor: change that keeps behavior the same
docs:     documentation
test:     tests
perf:     performance
chore:    maintenance, tooling, dependencies
build:    build system or packaging
ci:       CI configuration
```

## Reporting issues

When reporting a bug, include:

- Basil version or commit
- OS
- Steps to reproduce
- Expected behaviour
- Actual behaviour

Include the relevant log lines from `Logs/` if you have them. For a server that runs fully offline, most bug reports come with what the osu! client showed versus what the tournament API returned; those two observations are worth stating explicitly.

## Code of conduct

This project has a [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you agree to abide by its terms.
