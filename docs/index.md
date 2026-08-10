# Basil Documentation

Basil's documentation is organized by **audience**. Start with the folder that matches what you are trying to do.

| Audience                                                   | Documentation                                         | Start here                                                   |
| ---------------------------------------------------------- | ----------------------------------------------------- | ------------------------------------------------------------ |
| Player or tool using the HTTP API, bancho protocol, or SSE | [`for-client/`](for-client/bancho/getting-started.md) | [`getting-started.md`](for-client/bancho/getting-started.md) |
| Tournament or server operator                              | [`for-technicians/`](for-technicians/deployment.md)   | [`deployment.md`](for-technicians/deployment.md)             |
| Contributor working on the codebase                        | [`for-developers/`](for-developers/architecture.md)   | [`architecture.md`](for-developers/architecture.md)          |
| Agent or automated tooling                                 | [`for-agents/`](for-agents/guidelines.md)             | [`guidelines.md`](for-agents/guidelines.md)                  |

## Documentation by audience

### `for-client/`

Use this section when interacting with a running Basil instance as a client or external tool.

It covers:

* bancho protocol usage
* HTTP API usage
* SSE
* client-facing setup and behavior

Start with [`getting-started.md`](for-client/bancho/getting-started.md).

### `for-technicians/`

Use this section when deploying, configuring, or operating a Basil server.

It covers:

* installation and deployment
* configuration
* HTTPS and certificates
* server operation
* operational requirements

Start with [`deployment.md`](for-technicians/deployment.md).

### `for-developers/`

Use this section when changing Basil itself.

It covers:

* architecture
* development workflow
* testing
* protocol and application boundaries
* scope decisions
* design constraints

Start with [`architecture.md`](for-developers/architecture.md).

### `for-agents/`

Use this section when working on Basil through an automated coding agent or similar tooling.

It contains guidance for navigating the repository, respecting architectural boundaries, and making changes safely.

The authoritative agent instructions remain in [`CLAUDE.md`](../CLAUDE.md). `for-agents/guidelines.md` explains how those instructions relate to the documentation layout.

## API and command reference

The complete machine-readable HTTP API and BasilBot command reference is generated from the running application.

For a running instance, it is available at:

```text
https://api.<domain>/docs/
```

A published copy is also available on [GitHub Pages](https://thnhmai06.github.io/osuBasil/).

These generated references are the source for the detailed:

* HTTP routes
* request and response schemas
* status codes
* BasilBot commands
* protocol reference exposed through the generated documentation

The Markdown documentation does **not** duplicate the generated reference.

Instead, Markdown explains:

* why the system is designed this way
* how components interact
* how to operate and develop Basil
* which behaviors and contracts are authoritative
* which features are intentionally in or out of scope

When you need to know **what an endpoint or command accepts**, use the generated reference. When you need to know **why it exists or how it fits into Basil**, use the Markdown documentation.

## Authoritative documents

Each important topic has one document that owns the accepted version of the truth.

When two documents disagree, the authoritative document wins.

| Topic                                                  | Authoritative document             |
| ------------------------------------------------------ | ---------------------------------- |
| Scope decisions: what exists and what was removed      | [`for-developers/working-scopes.md`](for-developers/working-scopes.md) |
| Configuration surface, admin key, and data directories | [`for-technicians/configuration.md`](for-technicians/configuration.md) |
| HTTPS and certificates                                 | [`for-technicians/https.md`](for-technicians/https.md)                 |
| Privilege flags and their meanings                     | [`for-developers/privileges.md`](for-developers/privileges.md)         |
| System architecture                                    | [`for-developers/architecture.md`](for-developers/architecture.md)     |

Other documents may link to or summarize these topics, but they must not silently establish a conflicting definition.

### When changing an authoritative topic

If a code change changes an authoritative contract, update the corresponding document in the same change.

For example:

* adding a configuration option → update [`configuration.md`](for-technicians/configuration.md)
* changing certificate requirements → update [`https.md`](for-technicians/https.md)
* changing a privilege flag → update [`privileges.md`](for-developers/privileges.md)
* changing a dependency boundary → update [`architecture.md`](for-developers/architecture.md)
* adding or removing a supported feature → update [`working-scopes.md`](for-developers/working-scopes.md)

This keeps the documentation and implementation synchronized.

## Documentation rules

The documentation follows a simple ownership model:

> **One topic, one authoritative source. One audience, one documentation section.**

Avoid creating a second document that independently describes the same contract.

If another page needs the information, link to the authoritative document instead.

This prevents the documentation from developing multiple competing versions of the same behavior.

## See also

* [`for-developers/architecture.md`](for-developers/architecture.md): system architecture and dependency direction
* [`for-developers/working-scopes.md`](for-developers/working-scopes.md): supported and excluded functionality
* [`for-technicians/configuration.md`](for-technicians/configuration.md): server configuration
* [`for-technicians/https.md`](for-technicians/https.md): TLS requirements
* [`for-agents/guidelines.md`](for-agents/guidelines.md): documentation and repository guidance for agents
* [`CLAUDE.md`](../CLAUDE.md): authoritative instructions for coding agents
