# Response envelope implementation

## Overview

Basil wraps JSON responses from the `basilapi` route group in a common envelope at the ASP.NET Core middleware layer.

The envelope is an implementation-wide convention rather than something individual route handlers construct themselves.
OpenAPI schemas are transformed separately so the generated API contract matches the runtime response shape.

This page describes the implementation and the rules developers must preserve when adding or modifying API routes.

For the client-visible wire format, see [`response-envelope.md`](../for-client/api/response-envelope.md).

## Architecture

The response flow is:

```text
API route
    │
    ▼
ASP.NET Core response
    │
    ▼
EnvelopeMiddleware
    │
    ├── JSON response ───────────────► Envelope<T>
    │
    ├── file response ───────────────► unchanged
    │
    └── /live SSE response ──────────► unchanged
```

OpenAPI uses a separate transformation path:

```text
Route metadata
    │
    ▼
EnvelopeSchemaTransformer
    │
    ▼
T → Envelope<T>
```

Runtime wrapping and OpenAPI transformation therefore have to evolve together. Changing one without the other produces a
mismatch between the actual HTTP response and the generated API description.

## Envelope middleware

`EnvelopeMiddleware` is responsible for wrapping JSON responses produced by the `basilapi` group.

It must not wrap:

* file downloads;
* `/live` SSE streams.

SSE detection is based on the literal `live` route path segment. It does not inspect the `Accept` header.

This distinction matters because a request can fail before an SSE stream is established. A synchronous JSON error from a
`/live` endpoint is still a normal JSON response and is enveloped.

The middleware therefore distinguishes the endpoint's response type and route shape rather than treating every request
with an SSE-related `Accept` header as a stream.

### Response rules

| Response            | Envelope |
|---------------------|----------|
| JSON `200`          | Yes      |
| JSON error response | Yes      |
| JSON `4xx` / `5xx`  | Yes      |
| File download       | No       |
| `/live` SSE stream  | No       |
| `204 No Content`    | Not used |

A route that produces JSON should not manually construct `Envelope<T>` unless it has a specific reason to bypass the
normal middleware pipeline.

### Error paths

`EnvelopeMiddleware` only sees requests that already matched a `basilapi`-tagged endpoint. Two error paths never
reach it that way, and are covered separately so the `api.` host's envelope contract still holds for them:

* **A route that never matches at all** (a 404 with no route found, a 405 method mismatch, or a route-constraint
  overflow such as an `{id:int}` too large for `Int32`) resolves no endpoint at all. `EnvelopeMiddleware` treats an
  unmatched endpoint on the `api.` host as equivalent to a matched `basilapi` one for envelope purposes, so these
  responses are still wrapped instead of falling through as a bare, empty body.
* **An unhandled application exception** is caught by a separate middleware (`ExceptionLoggingMiddleware`, registered
  before routing) that logs it and, on the `api.` host only, maps it to an enveloped `500` -- provided the response
  hasn't already started writing. `BadHttpRequestException` (parameter-binding failures, e.g. that same route-id
  overflow) is treated as its own already-correct client-error status rather than a `500`. Every other host group
  keeps its previous bare-response behavior on an unhandled exception; only `api.` has an envelope contract to
  uphold.

### Internal invariant: `EnvelopeMiddleware` never re-parses untrusted bytes

`EnvelopeMiddleware` calls `JsonNode.ParseAsync` on the buffered response body, and `EnvelopeBuilder.BuildMeta`
calls `GetValue<int>()` on a paginated body's `page`/`pageSize`/`totalRecords` fields. Neither call is wrapped in a
`try`/`catch`, but neither is reachable by any current `basilapi` route: every enveloped body is JSON the
framework itself serialized from a typed C# object (`Results.Json`, `TypedResults`, etc.), never a re-parse of
request input, so the bytes are guaranteed well-formed and `page`/`pageSize`/`totalRecords` are guaranteed
numeric. A route that manually writes non-standard JSON bytes with a JSON content type would violate this and
should not do so. Should either call ever throw regardless, `ExceptionLoggingMiddleware` (registered earlier in
the pipeline, so it wraps `EnvelopeMiddleware`) still catches it and maps it to a correctly enveloped `500` --
there is no path where either throw reaches the client unwrapped.

## OpenAPI schema transformation

`EnvelopeSchemaTransformer` keeps the generated OpenAPI contract consistent with the runtime behavior.

A route declaring:

```text
T
```

is exposed in the OpenAPI schema as:

```text
Envelope<T>
```

The transformer operates on the declared response schema rather than modifying individual route definitions.

When adding a new JSON API endpoint, developers therefore normally only declare the underlying response type. The
envelope is applied centrally.

### Runtime/schema invariant

The following must remain true:

```text
Runtime response:  Envelope<T>
OpenAPI response: Envelope<T>
```

If a middleware change alters the runtime shape, the corresponding OpenAPI transformation must be updated in the same
change.

## Embedded references

API response models embed frequently referenced resources instead of exposing only foreign-key identifiers.

### Users

User references use:

```text
UserBrief
├── id
├── name
└── country
```

`Country` is serialized using its lowercase country acronym.

A response should not introduce a second user-reference representation for the same purpose.

### Beatmaps

Beatmap references are represented by records derived from `BeatmapView`, such as:

* `BeatmapDetail`;
* `BeatmapInSet`.

Beatmap references are resolved using the stored `Md5` content hash rather than the database id.

This is intentional: the hash identifies the exact beatmap content, while a numeric database id may refer to different
content after re-ingestion.

If the referenced content no longer exists, the embedded beatmap reference resolves to `null`.

## Cached reference resolution

Embedding references must not turn an API response into an N+1 query pattern.

The infrastructure repositories provide cached wrappers:

```text
CachingUserRepository
CachingMapRepository
CachingBeatmapsetRepository
        │
        ▼
IMemoryCache
        │
        ▼
SQLite repositories
```

The cache uses:

* a TTL for normal expiration;
* explicit invalidation whenever the underlying resource is written.

This allows response builders to resolve individual references without issuing a database query for every occurrence.

The cache is an optimization only. SQLite remains the source of persistent state.

### Why cache at the repository boundary?

Reference resolution is performed by multiple application services and response builders. Caching at the repository
boundary keeps the optimization independent of individual endpoints.

This also prevents each response builder from implementing its own cache and invalidation rules.

Developers adding a new consumer of users, maps, or beatmapsets should use the existing repository abstraction rather than
querying SQLite directly.

## Beatmap identity

Beatmap references use content identity:

```text
Md5
 │
 ▼
Beatmap
 │
 └── Beatmapset
```

They do not use the numeric `Beatmaps.Id` as the externally meaningful identity for an embedded beatmap.

The distinction is important during re-ingestion. A beatmap id can remain associated with a database row while the
underlying content changes. The content hash represents the specific difficulty that was referenced.

This also preserves historical references: if that exact content is removed later, the reference becomes unresolved
rather than silently pointing at a different difficulty.

See [`beatmap-ingestion.md`](beatmap-ingestion.md) for the filesystem and re-ingestion model.

## No `204` JSON responses

Basil does not use `204 No Content` for JSON API operations covered by the envelope convention.

A successful operation that has no meaningful payload should return:

```json
{
	...
	"data": null,
	...
}
```

rather than introducing a second bodyless-response convention.

This keeps the rule simple:

> Every JSON-producing route in the `basilapi` group uses the envelope.

File responses and SSE are the explicit transport-level exceptions.

## Adding or modifying an API route

When adding a normal JSON endpoint:

1. Define the response model.
2. Register the route in the appropriate API route group.
3. Return the underlying model/result rather than manually wrapping it.
4. Verify that the endpoint is included in the expected OpenAPI group.
5. If the response embeds users, beatmaps, or beatmapsets, use the existing repository abstractions so caching and
   invalidation remain centralized.
6. Add explicit handling only if the endpoint intentionally produces a file or SSE stream.

Do not add endpoint-specific envelope middleware or duplicate envelope construction.

## Common pitfalls

### Manually wrapping a response

Avoid:

```csharp
return Results.Ok(new Envelope<MyResponse>(response));
```

when the route already passes through `EnvelopeMiddleware`.

Doing so can produce a nested envelope.

### Adding an SSE endpoint without following the `/live` convention

The middleware identifies SSE routes through their `live` path segment. A new streaming endpoint should follow the
existing routing convention rather than relying on `Accept: text/event-stream` to opt out of wrapping.

### Querying repositories directly for every embedded reference

This can silently introduce an N+1 query pattern. Use the caching repository abstractions.

### Using database ids for historical beatmap references

Use the beatmap content hash when the reference represents a specific difficulty/content version.

### Returning `204` for an enveloped JSON operation

Use an enveloped `200` response with `data: null` instead.

## Related code

* [`Basil.Web/Middleware/EnvelopeMiddleware.cs`](../../src/Basil.Web/Middleware/EnvelopeMiddleware.cs): runtime response wrapping
* [`Basil.Web/Middleware/ExceptionLoggingMiddleware.cs`](../../src/Basil.Web/Middleware/ExceptionLoggingMiddleware.cs): unhandled-exception logging and the `api.` host's 500-envelope mapping
* [`Basil.Web/OpenApi/EnvelopeBuilder.cs`](../../src/Basil.Web/OpenApi/EnvelopeBuilder.cs): envelope schema construction
* [`Basil.Web/OpenApi/EnvelopeSchemaTransformer.cs`](../../src/Basil.Web/OpenApi/EnvelopeSchemaTransformer.cs): OpenAPI response transformation
* [`Basil.Application/Services/Multiplayer/MatchLiveSnapshotBuilder.cs`](../../src/Basil.Application/Services/Multiplayer/MatchLiveSnapshotBuilder.cs): `UserBrief` embedding
* [`Basil.Infrastructure/Cache/`](../../src/Basil.Infrastructure/Cache/): cached reference resolution

## See also

* [`response-envelope.md`](../for-client/api/response-envelope.md): client-visible response format
* [`sse.md`](sse.md): live stream behavior
* [`database.md`](database.md): persistent data and repository structure
* [`beatmap-ingestion.md`](beatmap-ingestion.md): beatmap identity and ingestion
