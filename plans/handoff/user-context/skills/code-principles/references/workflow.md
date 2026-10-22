# Working with thnhmai06

How to approach a task, how to plan, how to review, and how to talk to the user. These rules come from
corrections the user gave; each one cost them time once.

## Contents

- [Before coding](#before-coding)
- [Simplicity and surgical changes](#simplicity-and-surgical-changes)
- [Plans](#plans)
- [Scope](#scope)
- [Respect decisions already made](#respect-decisions-already-made)
- [Reviewing work](#reviewing-work)
- [Delegating to other agents](#delegating-to-other-agents)
- [Talking to the user](#talking-to-the-user)

## Before coding

**Don't assume. Don't hide uncertainty. Surface tradeoffs.**

- State your important assumptions.
- When several interpretations are plausible, name them. Don't pick one silently.
- When a simpler solution exists, say so and explain the tradeoff. Push back when warranted.
- When a requirement is genuinely ambiguous, stop and ask before coding. When it is only a routine choice,
  take the conventional default and mention it.
- Define a concrete success criterion before implementing. For multi-step work, write a short plan:

  ```text
  1. [change] → verify: [check]
  2. [change] → verify: [check]
  ```

- Read the project's `AGENTS.md`, the plan or design documents it points to, and the code you will touch.
  Use the code-intelligence tools that are connected (code graph, IDE MCP, documentation MCP) before grepping
  and reading by hand.

## Simplicity and surgical changes

- Write the smallest solution that satisfies the request: no speculative features, no unused
  abstractions, no unrequested configurability, no future-proofing without a concrete need, no error handling
  for impossible states. If 200 lines could be 50, rewrite it.
- Change only what the task requires. Preserve existing behaviour unless the task changes it. Match the
  surrounding style. Don't refactor unrelated code, rewrite adjacent comments, reformat untouched code or
  delete unrelated dead code; mention it instead.
- Remove imports, variables and members only when **your** change made them unused.
- Every changed line traces back to the request.
- Use semantic refactoring tools (the IDE's rename, move, safe delete, change signature) instead of text edits
  where they exist.

## Plans

- **A request for a plan means a plan only.** "Write a plan" or "not implementing yet" means no code at all,
  not even scaffolding, packages or a schema. That holds even after the user answers the plan's questions:
  answering policy questions approves policies, it doesn't approve coding. Wait for an explicit go ("triển
  khai").
- List the current business rules a plan relies on, so the user can review them.
- Keep the detail in the plan file. In chat, give a few key points. The user doesn't read long plans in chat.
- When a plan's decision is reversed later, update the plan or `AGENTS.md`. The newest decision wins, and
  stale text must not mislead the next agent.

## Scope

- Respect the project's declared scope. Don't implement a feature because an upstream project has it.
- If a request conflicts with the documented scope or an invariant, surface the conflict before coding. Don't
  quietly weaken the invariant.
- Prefer extending an existing abstraction over adding a parallel mechanism.

## Respect decisions already made

- **Before you call the user's code a bug, check whether it encodes a deliberate convention or a domain
  rule.** Seats numbered from 1, or a resize that counts free seats instead of cutting a range, can look
  like bugs and be exactly what the domain needs. "Fixing" them to 0-based and positional breaks the domain.
  Ask if unsure.
- Don't recreate something the user deliberately removed (a wrapper class, a method set) to make a compile
  error go away. Look for the property or mechanism that replaced it.
- Don't rename back what the user renamed. Their hand-made edits show their taste. Learn from them.
- When the user answers "không phải vấn đề" ("not a problem") to a proposed change, it means *go ahead and do
  it however you see fit*, not *skip it*. When you can't tell approval from rejection, ask.

## Reviewing work

Accepting work, your own or a delegated agent's, needs a **line-by-line quality review**. Build and grep
passing is only the entry gate, never the verdict.

For each change, check and report:

- behaviour against the spec and the domain's real semantics;
- concurrency: locks, check-then-act, races with cleanup;
- edge cases: empty, closed, re-entry, idempotency, cancellation;
- event payloads and names;
- the project's `AGENTS.md` rules and this skill: organization, design, naming, docs;
- user-visible strings;
- anything unrelated that slipped into the diff.

Check delegated code against the rules that slipped through review before, and don't let them back in:

- a relation stored on the actor (`user.Channels`, `user.Join(channel)`), or a session emitting events;
- an event handler inside the core, or a rule or timer on a runtime model or registry;
- a service that only wraps a repository read, or a registry used through its implementation instead of its
  contract;
- a contract name that reveals its mechanism;
- a reference from the services implementation to the storage implementation, or from infrastructure to
  either implementations project;
- a special actor ("the server acts", a fixed system id), an action without a real `by`, or authority decided
  by connection kind;
- a check on granted instead of effective permissions, or stored or configured privilege flags;
- natural-language text produced by the core instead of the surface that shows it.

Report concrete findings, not "build green, grep empty". When earlier work was accepted without a deep
review, re-review it.

Before a change is complete:

1. Run the smallest relevant tests while iterating, then the full suite.
2. Run a Release build when production code changed.
3. Run the architecture tests for cross-layer changes.
4. Update the authoritative documentation when behaviour or design changed (see [docs.md](docs.md) for the
   rework exception).
5. Review the final diff for unrelated changes.

## Delegating to other agents

When the user has their own instructions about delegation (which agents or models to use, how to spend
quota), follow them. This skill adds:

- **You design, the agent types.** A delegated prompt carries the full design: exact type and member
  signatures, file locations, doc text and control flow, the conventions that apply (for example "seats are
  numbered from 1"), the tools and skills to use, and the exact verification command. Leave no open choices,
  such as "same file or a new one?".
- Review the agent's diff with the same line-by-line standard, and fix design drift yourself.
- Never let a rule violation through because "the agent wrote it".

## Talking to the user

- Keep chat summaries short: the key points and the decisions needed. Put details in files.
- When you ask a question, give options with a recommendation and its reason. Check every fact the options
  rely on first (what an analyzer reports, what a library does): the user decides on what you tell them.
- Report outcomes faithfully. If tests fail, say so. If you skipped a step, say which. If you said something
  that turned out wrong, correct it as soon as you know.
- Answer in the user's language. Code, identifiers, commits and documentation stay in English.
