# Response Envelope

## Why

A hand-built API tends to drift: one endpoint returns `{ error: "..." }` on failure, another returns a bare string, a paginated list here carries `total` and there carries `totalCount`. None of that is wrong in isolation, but it means a client (or a generated SDK) can't handle "an error" or "a page of results" generically: it has to special-case every endpoint. Basil picks one envelope shape and applies it everywhere on this host, so generic client code works everywhere too.

## Overview

Every JSON response on the `api.` host (matches, beatmapsets, scores, users, and everything else under the tournament API) follows one consistent shape, so a client can write one response-handling path instead of one per endpoint.

## Contract

Every response body looks like this:

```json
{
  "success": true,
  "code": 200,
  "message": "Retrieval successful",
  "data": { "...": "the actual response payload" },
  "meta": null,
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

- `success`/`code` mirror the real HTTP status (`success` is `code < 400`).
- `message` is a short human-readable summary: a generic success phrase, or the original failure's error text.
- `data` is the endpoint's actual payload. For a paginated list, `data` is just the `items` array, and `page`/`pageSize`/`totalRecords`/`totalPages` move into `meta` instead.
- Every route that used to return `204 No Content` now returns `200 OK` with `data: null`. `204` can't carry a body, so this is the standard's only bodyless-to-full-body change.
- **File downloads and every `/live` SSE stream are excluded.** Their `Content-Type` is never JSON, so wrapping them wouldn't make sense; see [`sse.md`](sse.md) for the SSE side of that distinction.

## Concepts

- Every reference embeds full data instead of a bare id. A user shows up as `{ id, name, country }` everywhere, not a numeric `userId`; a beatmap reference embeds its difficulty and parent beatmapset. A client never has to make a second request just to render a name next to an id.
- Enums stay enums on the wire. Every enum field serializes as its numeric value, the same convention System.Text.Json uses by default, except `Country`, which serializes as a lowercase two-letter code (`"vn"`, `"xx"`) because a raw country enum number means nothing to a human reading a response. This is a deliberate, documented exception, not an inconsistency.
- A `PUT` body and a `PATCH` body are genuinely different shapes, not one all-optional record reused for both: the `PUT` (full replace) record has every field required, the `PATCH` record has every field optional. This lets the generated OpenAPI schema correctly mark which fields are actually required for each verb.

## Design

**Why embed full objects instead of ids the client can look up?** Every embedded reference (a `UserBrief`, a beatmap's difficulty and parent set) comes from an in-memory cache keyed by id, invalidated on write, so embedding costs nothing extra per response beyond what a client would have paid for anyway with a follow-up lookup, and it removes an entire round trip from every consumer.

**Why does a beatmap reference resolve by content hash instead of id?** A beatmap's numeric id can survive a re-ingestion that changes its actual content (someone re-uploads the same set with edits), but its file hash can't. Keying the embed by hash means a reference to "this exact difficulty as it was played" stays correct even if the id later points at something different. It just resolves to `null` once that specific content is gone.

**Why exclude `204` entirely?** A bodyless response can't carry the envelope, and the standard doesn't want a second "no body" exception alongside file downloads and SSE. Returning `200` with `data: null` keeps exactly one rule: every JSON-producing route on this host returns the envelope, full stop.

## Related code

- `Basil.Web/Middleware/EnvelopeMiddleware.cs`
- `Basil.Web/OpenApi/EnvelopeBuilder.cs`
- `Basil.Web/OpenApi/EnvelopeSchemaTransformer.cs`
- `Basil.Application/Services/Multiplayer/MatchLiveSnapshotBuilder.cs` (the `UserBrief` embed)

## See also

- [`sse.md`](sse.md): why live streams are excluded from this envelope
- [`database.md`](database.md): where the cached lookups behind every embedded reference come from
