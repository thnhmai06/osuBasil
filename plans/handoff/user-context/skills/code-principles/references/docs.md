# Documentation

Three kinds of documentation, each with one job:

- **XML doc comments** are reference material for one member: what it is and how it behaves.
- **API docs** (endpoint summaries and descriptions, OpenAPI) are the public contract of an endpoint.
- **Markdown docs** explain why the system exists, how it works and how its parts fit together.

All three pass the same test.

## The Implementation Test

> If the implementation were replaced while the observable behaviour stayed identical, would this sentence
> have to change?

If yes, the sentence describes the implementation. Remove it from XML docs and API docs, or move it to the
Design/Implementation section of a Markdown page. The exception is a mechanism that is itself part of the
contract: "The response is streamed using Server-Sent Events."

## XML doc comments

Every public type and member has a `///` comment, including model properties, record parameters and enum
members. Write it in English. It describes **responsibility and observable behaviour**, never
implementation.

No member is too obvious to document. `Id` and `Name` get a summary too, because the summary says what the
value means to a caller: "The identifier the store assigned to the room, used for lookups." "The name other
participants see." When you create or redesign a type, document every public member of it before you call it
finished, not only the members you thought about.

- **`<summary>`** is one sentence answering *"What is this member, and what does it do?"* It doesn't restate
  the name. Write "Keeps the one live instance of each stored item, by key, for as long as anyone holds it",
  not "Identity map".
- **`<remarks>`** holds only additional behaviour the caller can see that doesn't fit the summary.
- **`<param>`** and **`<returns>`** describe the meaning of the value, not how it is produced. Say what
  `null` or empty means: "The text reader, or `null` when no entry has that name."
- Use `<see cref>` only when it helps the reader navigate.
- **Never document** temporary files, locks, cache keys, dictionaries, reflection, switch statements,
  framework internals, dependency injection or internal collaborators.
- Don't record history ("previously this did X") unless the member exists for backward compatibility.
- Document the exceptions that are part of a member's contract.
- Use `/// <inheritdoc />` on implementations of a documented contract.

Microsoft: [Recommended XML tags](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/recommended-tags).

```csharp
/// <summary>Opens a writer that replaces an entry's text.</summary>
/// <param name="entry">The name of the entry.</param>
/// <param name="cancellationToken">A token that cancels the operation.</param>
/// <returns>
///     A writer whose text becomes the entry's text once the writer is disposed, replacing any text stored under
///     that name. Disposing it without writing stores an empty entry.
/// </returns>
Task<StreamWriter> CreateOrUpdateAsync(string entry, CancellationToken cancellationToken = default);
```

## API endpoint documentation

- **Summary**: one sentence saying what the endpoint does ("Retrieve a meeting report."). It never mentions
  storage, frameworks, services or internal mechanisms.
- **Description**: Markdown paragraphs, in this order:
  1. what the endpoint provides;
  2. important special cases;
  3. related or live endpoints;
  4. important errors.
- Don't restate what the schema already says: the method, the path, ordinary parameter definitions, the
  declared response type.
- Prefer raw string literals for multi-paragraph descriptions.
- Never name PostgreSQL, Redis, dependency injection, middleware, `Channel<T>`, background services, cache keys,
  internal file paths or internal types.
- The generated reference (OpenAPI, command help) is the reference. Don't write a second, hand-written API
  reference in Markdown.

Microsoft: [Include OpenAPI metadata](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/include-metadata),
[OpenAPI XML documentation comment support](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/openapi-comments).

## Markdown documentation

### Purpose and structure

- **Why, then What, then How.** A page answers, roughly in this order: what is this, why does it exist, how
  does it work or how is it used, which details, limits and invariants apply, and where to read more. Use only
  the sections that fit the topic.
- **One major topic per file.**
- **One authoritative document per topic.** When another page needs the information, link to it instead of
  copying it. When a topic already has an authoritative page, update that page rather than writing a
  competing description. A docs index states which page owns which topic.
- Start with an overview before details. Keep the public contract separate from the implementation. Put
  implementation in a dedicated Design or Implementation section.
- State important invariants explicitly: the source of truth, idempotency, stable identifiers, what is
  intentionally unsupported, whether two paths share an implementation.
- Prefer diagrams for multi-step flows.
- Use short, correct, current examples. Don't paste large amounts of source code.
- Add a **Related code** section when source locations help a contributor.
- Write for the audience of the folder: clients, operators, developers or agents. Don't duplicate one contract
  across audience folders.

### Writing

- Clear, concise English, active voice. Explain the *why* when it affects how the system is used or changed.
- Concrete statements, no filler, no marketing language.
- Use the project's established terms consistently, and the exact identifiers from the code (configuration
  keys, routes, type names).
- ATX headings in sentence case, short paragraphs, fenced code blocks with a language, inline code for
  identifiers, relative links.
- Describe the system as it works now. Remove obsolete instructions; don't keep them as history unless the
  history is useful.

Microsoft: [Microsoft Writing Style Guide](https://learn.microsoft.com/style-guide/welcome/),
[Microsoft Learn style and voice quick start](https://learn.microsoft.com/contribute/content/style-quick-start).
Follow them for word choice, voice and punctuation that this page doesn't cover.

### When to update

- Update the documentation in the same change as the code, configuration or behaviour it describes.
- **During a rework**, when the user says the source is still moving, documentation and XML docs may lag
  behind. Don't review, flag or rewrite them until the code they describe has settled, unless asked. Principles
  the user states during the rework still go into `AGENTS.md` immediately.

## Agent instruction files

When a repository has an `AGENTS.md`, it is the authoritative instruction source for every agent, and
`CLAUDE.md` only imports it (`@AGENTS.md`). When the user states a design principle or corrects one during
work, add it to the matching section of that file in the same change, so every agent learns it, not only the
current session.
