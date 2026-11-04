# Response envelope implementation

## Overview

Every JSON response of the `api.` host has the same envelope. Routes build it explicitly through the helper `Api`, and the
OpenAPI document describes the same shape, so the generated contract and the runtime response cannot drift apart as long
as the rules below are kept.

This page describes the implementation and the rules developers must preserve when adding or modifying API routes.

For the client-visible wire format, see [`response-envelope.md`](../for-client/api/response-envelope.md).

## Why

A client that talks to many routes should parse one shape, whether the call succeeded, was refused or failed. Making the
route author choose the envelope helper (instead of wrapping after the fact) keeps the status code, the message and the
machine-readable error next to the code that decides them, and keeps a streamed or binary response out of any
rewriting.

## How the response is built

```text
API route
    │
    ├── Api.Ok / Created / Done / Paged ───────► Envelope<T>   (success)
    │
    ├── Api.Error / NotFound / Invalid /
    │   Unauthorized / result.Refusal() ───────► Envelope<object> with errors.code   (failure)
    │
    ├── file or image response ────────────────► unchanged
    │
    └── /live SSE response ────────────────────► unchanged
```

`Envelope<T>` is `{ success, code, message, data, meta, errors, timestamp }`:

* `success` is `true` below status 400 and `code` repeats the HTTP status.
* `meta` is `{ offset, limit, total }` for a page (`Api.Paged`) and `null` otherwise.
* `errors` is `{ code, missing }` for a failure: `code` is a stable camelCase string, and `missing` is the permission mask
  the caller lacks when the refusal is for lack of permissions.
* JSON is camelCase and enums are camelCase strings (`ApiJson.Options`).

Outcomes of the Application objects reach the client through `OutcomeErrors` (in `Basil.Host.Shared`), which maps each
outcome to an error kind and therefore to a status; `result.Refusal()` and `result.Answer()` turn a `Result<TOutcome>`
into an envelope result. A new member of an outcome enum needs a row in that mapping and in the exhaustive table of
`OutcomeErrorsTests`.

### Response rules

| Response            | Envelope |
|---------------------|----------|
| JSON `200` / `201`  | Yes      |
| JSON error response | Yes      |
| File or image       | No       |
| `/live` SSE stream  | No       |
| `204 No Content`    | Not used |

A route that produces JSON returns one of the `Api` results. It does not write JSON of its own.

### Paths that never reach a route

The pipeline of the `api.` host keeps the envelope contract for requests that no handler answers:

* **A request that matches no route** (404, or a 405 method mismatch) is answered by the fallback with an enveloped
  `404`.
* **An unhandled exception** is caught by `RestPipeline`, which logs it and answers an enveloped `500` with the code
  `internalError`, provided the response has not started. A cancelled request is not an error and is not mapped.
* **The server is stopping:** `RestPipeline` answers an enveloped `503` (`shuttingDown`) with a `Retry-After`.
* **A request over a rate limit** is answered with an enveloped `429` and a `Retry-After`.

Only the `api.` host has an envelope contract; the other hosts keep their own response forms.

## OpenAPI

The OpenAPI document is generated from the endpoint metadata and shaped by `OpenApiSetup`:

* the schema `Envelope` is declared once, with the members above;
* an operation transformer removes the handler's own `2xx` response and describes the success answer as an `Envelope`
  (or as an event stream for a `/live` route), adds the `Bearer` security requirement and the `401` and `403` answers of a
  token route, and adds the shared `404`, `409`, `422` and `429` answers;
* `.WithSummary` and `.WithDescription` document the public contract (see `AGENTS.md`, Rule 5), not the implementation.

### Runtime/schema invariant

The following must remain true:

```text
Runtime response:  Envelope<T>
OpenAPI response:  Envelope
```

If the shape of the envelope changes, `Api` and the `Envelope` schema in `OpenApiSetup` change in the same commit, and
the Postman contract (`tests/Hosts/Rest/postman`) is regenerated from the new document.

## Embedded references

API response models embed small references to the resources they mention instead of exposing only foreign-key
identifiers. A user is embedded as `UserRef` (`id` and `name`); a response must not introduce a second representation of
a user reference for the same purpose. A beatmapset and its difficulties are shown as `SetView` and `BeatmapView`.

References are resolved from the in-memory state of the objects and registries that own them, which is the source of
truth of a running server, so a response builder does not issue a database query per embedded reference. See
[`database.md`](database.md) and `docs/for-agents/persistence.md` for the storage model (memory-first today; cache-aside is the target).

## No `204` JSON responses

Basil does not use `204 No Content` for JSON API operations covered by the envelope convention.

A successful operation that has no meaningful payload answers `Api.Done(...)`:

```json
{
	...
	"data": null,
	...
}
```

rather than introducing a second bodyless-response convention.

This keeps the rule simple:

> Every JSON-producing route of the `api.` host uses the envelope.

File responses and SSE are the explicit transport-level exceptions.

## Adding or modifying an API route

When adding a normal JSON endpoint:

1. Define the response model (a view in `Basil.Host.Shared` when the console also shows it).
2. Register the route in the appropriate route group, with `.RequireToken()` when it needs a signed-in user.
3. Take the actor from the connection of the request and work through a scope (`x.ScopeTo(connection)`); the route
   holds no permission rule of its own.
4. Return an `Api` result: `Ok`, `Created`, `Done` or `Paged` for success, and `result.Refusal()` / `result.Answer()` for
   an outcome.
5. Write `.WithSummary` and `.WithDescription` as public contract only.
6. Add explicit handling only if the endpoint intentionally produces a file or an SSE stream.

Do not wrap a result in an `Envelope<T>` by hand and do not add endpoint-specific envelope middleware.

## Common pitfalls

### Writing JSON by hand

A route that writes its own JSON bytes (or returns `Results.Json` with a different shape) breaks the envelope contract
and is invisible to the OpenAPI transformer. Use `Api`.

### Adding an SSE endpoint without following the `/live` convention

A streaming endpoint has its own `/live` route, marked with `.Sse()`, and does not negotiate between JSON and SSE with
the `Accept` header. A failure that happens before the stream starts is still a normal enveloped JSON response. See
[`sse.md`](sse.md).

### Returning `204` for an enveloped JSON operation

Use an enveloped `200` response with `data: null` (`Api.Done`) instead.

### Forgetting the outcome mapping

A new member of an outcome enum that has no entry in `OutcomeErrors` is answered with the default error kind of its
enum, which is rarely the status the member means. Add the mapping and its test row.

## Related code

* [`Api.cs`](../../src/Hosts/Rest/Http/Api.cs): the envelope and the helpers that build every result
* [`RestPipeline.cs`](../../src/Hosts/Rest/Http/RestPipeline.cs): exceptions, shutdown and rate limits as envelopes
* [`OpenApiSetup.cs`](../../src/Hosts/Rest/Http/OpenApiSetup.cs): the `Envelope` schema and the operation transformer
* [`OutcomeErrors.cs`](../../src/Hosts/Shared/OutcomeErrors.cs): outcome to error kind and status
* [`UserViews.cs`](../../src/Hosts/Shared/Views/UserViews.cs), [`BeatmapViews.cs`](../../src/Hosts/Shared/Views/BeatmapViews.cs): the embedded references

## See also

* [`response-envelope.md`](../for-client/api/response-envelope.md): client-visible response format
* [`sse.md`](sse.md): live stream behavior
* [`database.md`](database.md): persistent data and repository structure
* [`beatmap-ingestion.md`](beatmap-ingestion.md): beatmap identity and ingestion
