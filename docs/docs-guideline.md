# Documentation Guidelines

These guidelines apply to all Markdown files under `docs/`.

## Purpose

Documentation should help users understand and use Basil efficiently. Every page should have a clear scope and avoid covering multiple unrelated topics.

## Writing Style

- Write in clear, concise English.
- Prefer active voice.
- Explain *why* as well as *how*.
- Assume the reader has basic technical knowledge.
- Avoid filler words and marketing language.

## Terminology

Use the official project terminology consistently.

Examples:

- **Basil**
- **osu! stable client**
- **osu! lazer**
- **Bancho protocol**
- **API**
- **beatmap**
- **player**
- **match**
- **Menu Icon**

Avoid inventing alternate names or abbreviations.

## Formatting

- Use ATX headings (`#`, `##`, `###`).
- Use sentence case for headings.
- Keep paragraphs short.
- Use bullet lists instead of long prose where appropriate.
- Use fenced code blocks with language identifiers.

```bash
dotnet run
```

Use inline code for:

- commands
- configuration keys
- file names
- environment variables
- API routes
- identifiers

Example:

- `ServerOptions`
- `PUT /adminkey`
- `appsettings.json`

## Cross References

Prefer linking to existing documentation instead of duplicating content.

Use relative links:

```md
[Deployment](run-deployment.md)
```

## Code Samples

Every code sample should:

- be minimal
- be correct
- match the current implementation
- omit unrelated details

## Screenshots

Only include screenshots when they provide information that text cannot easily explain.

Avoid screenshots of code.

## Versioning

Documentation should describe the current `main` branch.

Remove outdated content instead of leaving obsolete instructions whenever possible.

## Page Structure

A documentation page should generally answer:

1. What is this?
2. Why does it exist?
3. How do I use it?
4. Additional details or caveats
5. Related pages

## Contributing

If a code change affects behavior, APIs, configuration, deployment, or user workflows, update the relevant documentation in the same pull request.