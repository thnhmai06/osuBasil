---
name: code-principles
description: The code principles of thnhmai06 (Mai Thành) - how they organize projects, name things, model domains, handle data and resources, write C#, document, test, and work with agents. Use this skill whenever you write, review, refactor, design, plan or organize code in any of thnhmai06's projects, especially C#/.NET, even when the request never mentions "style" or "principles". It applies when you decide where a file or type belongs, name a contract, member, event or folder, choose between a stream and a byte array, design a service or a model, lay out a layered backend or wire service events to handlers, write XML docs, Markdown docs or tests, review a delegated agent's diff, or turn a request into a plan. Project rules (AGENTS.md, CLAUDE.md, .editorconfig) still win over this skill.
---

# Code principles

This skill holds the code principles of thnhmai06: how they want code organized, designed, written,
documented and reviewed. It comes from the rules they wrote for their repositories, the commits they made
by hand, and the corrections they gave agents. Use it to write code they would have written themselves, so
they don't have to correct it afterwards.

The principles are language-neutral. The detailed syntax guidance is for C#. In another language, apply
the same intent with that language's idioms and its official conventions.

## Precedence

When two sources disagree, apply them in this order:

1. **The repository's own rules.** These are `AGENTS.md`, `CLAUDE.md`, `.editorconfig`, analyzer settings,
   and the conventions already used in the code you touch. A project may diverge on purpose. If a divergence
   looks accidental, mention it once and still follow the project.
2. **This skill.**
3. **Microsoft's official guidance**, before any other external source. Use it for any design or style
   question this skill does not settle. It covers the
   [Framework Design Guidelines](https://learn.microsoft.com/dotnet/standard/design-guidelines/),
   [C# coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions)
   and the .NET API docs. Look them up instead of recalling them, using the Microsoft Learn MCP server if it is
   connected. For other languages, use that language's official style guide.
4. General judgment, community conventions.

## The core

Apply these on every task. The reference files explain each one in depth.

1. **Think before coding.** State your assumptions. Name the interpretations when there are several. Surface
   tradeoffs, and ask when a requirement is really ambiguous. Don't invent requirements silently.
2. **Write the smallest complete change.** Add no speculative features, no abstractions with a single use,
   no unrequested configurability, and no handling for impossible states. Touch only what the task needs, and
   finish every caller, test and fixture it does need.
3. **Organize by responsibility, group by concept, share by necessity.** Use feature folders, not
   `Models/` + `Services/` piles. Put something in `Common` only when it is truly shared. Never flatten
   distinct concepts into one level, namespace, interface or API.
4. **Models are valid, services are meaningful, storage only stores.** A model guarantees its own values
   are valid. A service holds every action and guarantees the change makes sense. A repository, registry or
   storage holds data and nothing else.
5. **The object acted on owns the interaction.** The relation lives on the object acted on. The operation
   lives in that object's service. The event belongs to that object's category. The actor only calls.
6. **Prefer a reference over a value, and a stream over materializing.** Keep paths, ids, handles and object
   references. Load content on demand, stream it, and give every resource a clear owner and lifetime.
7. **Names carry meaning, not mechanism.** `ScanAsync`, not `ScanFileAsync`. Use one name for one concept
   everywhere, and full words, never abbreviations.
8. **Rank expression choices: correct, then clear, then consistent, then idiomatic and concise.** Formatting
   belongs to the IDE.
9. **Docs and tests pin contracts, not implementation.** Apply the Implementation Test: if the
   implementation changed and the behaviour stayed the same, would this sentence or test have to change? If
   it would, it describes the implementation and doesn't belong there.
10. **Verify. Compiling is not done.** Review every changed line for behaviour, races, edge cases and these
    rules. A green build is only the entry gate.

## Where to read next

Read only the files the task needs. Most tasks need two or three, not all of them.

| The task involves…                                                                                       | Read                                                         |
|----------------------------------------------------------------------------------------------------------|--------------------------------------------------------------|
| Where a file, type or folder goes; namespaces; `Common`; module boundaries; dependency direction          | [references/organization.md](references/organization.md)     |
| Laying out a backend into layer projects, or wiring service events to handlers                            | [references/architecture.md](references/architecture.md)     |
| Designing models, services (child services by part), storage, events, contracts, errors, concurrency, naming, permissions and restrictions, imported content | [references/design.md](references/design.md) |
| Holding data in memory, memory-first persistence, files, streams, `byte[]` vs `Stream`, assets, collection types, caches, disposal | [references/memory.md](references/memory.md) |
| Writing or refactoring C#: `?:` vs `if`, LINQ, patterns, records vs classes, async, properties, formatting | [references/csharp-style.md](references/csharp-style.md)     |
| XML doc comments, API endpoint docs, Markdown documentation                                               | [references/docs.md](references/docs.md)                     |
| Writing or reviewing tests                                                                                | [references/testing.md](references/testing.md)               |
| Planning, scope, reviewing work (including delegated agents' work), talking to the user                   | [references/workflow.md](references/workflow.md)             |
| Which Microsoft document backs a rule, or what Microsoft says on a question the skill doesn't settle      | [references/microsoft-docs.md](references/microsoft-docs.md) |

For a new feature, the usual order is workflow, then organization, then design, then the file-specific
references. For a new backend, read architecture before organization.

## Before you call it finished

Check the change against this list. Each item is a rule thnhmai06 has had to enforce in review.

- Each new file sits in the folder of the feature or concept it serves. It doesn't sit in a generic
  `Helpers/`, `Models/`, `Misc/` or `Shared/`.
- No interface has a single implementation, unless it is a capability contract used across a module
  boundary or a port to something outside the process.
- A value computed only from an object's own data is a derived property of that object, not a service
  method. A natural-language message for people never lives on a model or in a service; it belongs to the
  surface that shows it.
- A lock belongs to the object whose state it guards, never one global lock for many objects.
- No service method only forwards to a repository. No setter emits events or changes other state. No
  `Change*`, `Rename` or `With*` wrapper wraps a single field.
- No relation is stored on the actor (`user.Rooms`, `user.Join(room)`). It lives on the object acted on.
- Contract and member names survive a change of mechanism. Time, actor and lifecycle concepts use one name
  each (`Timestamp`, `StartedAt`/`EndedAt`, `CreatedAt`, `by`).
- Large content moves as a `Stream` or reader, not a `byte[]` or `string`, unless it is small or a boundary
  needs it. Every stream has an owner that disposes it.
- No operation waits for the database unless its result comes from the database. Memory is the truth, and
  "cache" means only a folder of rebuildable derived files.
- A collection is exposed through its read-only interface, not copied (`Mutable{X}` for the internal
  read-write twin). Assets are `AssetReference`s, not strings. Every stored time is UTC.
- A service whose object has parts has one child service per part; a service with no parts stays flat.
- Permission checks use the effective permissions (granted minus active restrictions), ask for the smallest
  permission that allows the action, and never look at the kind of connection.
- No `.ToList()` or `.ToArray()` out of habit. No LINQ hides side effects. No null is returned where a
  collection is expected.
- `?:` puts the normal case in the `?` branch. When the `if`s above are the same kind of decision, the
  last branch is also an `if`.
- Every `await` in library code uses `.ConfigureAwait(false)`, including `await using` and `await foreach`.
- Every public type and member has XML docs that describe behaviour, not implementation. That includes
  model properties, enum members and the obvious ones (`Id`, `Name`): scan each type you created or
  redesigned for an undocumented public member.
- Tests assert observable contracts, one behaviour each, deterministically.
- You read every changed line yourself, including lines a delegated agent wrote.

## When something isn't covered

This list is not closed. When you face a style or design choice the skill doesn't settle:

1. Check the language version, `.editorconfig`, analyzer and IDE inspections, and the surrounding code.
2. Look up Microsoft's guidance, or the language's official guidance.
3. Classify what you found. It is a **language rule** (semantics), a **tooling rule** (compiler, IDE,
   analyzer, `.editorconfig`), a **project convention**, or a **general principle**. Don't present a
   convention as a language or IDE requirement unless you verified it.
4. Generalize it into a principle that covers similar cases, with one example.
5. Propose the addition to the user. Don't silently edit the skill. When the user states a new principle
   during work, record it in the project's `AGENTS.md` so every agent learns it, and suggest adding it here if
   it holds across projects.
