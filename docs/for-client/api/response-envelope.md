# Response envelope

## Overview

JSON responses from the `api.<domain>` host use a consistent response envelope.

A client can therefore use the same response-handling logic across API endpoints instead of implementing a different top-level structure for each endpoint.

File downloads and Server-Sent Events are exceptions because they do not return JSON. See [`sse.md`](sse.md).

---

## Response format

A typical successful response looks like:

```json
{
  "success": true,
  "code": 200,
  "message": "Retrieval successful",
  "data": {
    "...": "endpoint-specific payload"
  },
  "meta": null,
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

The envelope contains:

| Field       | Description                                                            |
| ----------- | ---------------------------------------------------------------------- |
| `success`   | `true` when `code` is below `400`; otherwise `false`.                  |
| `code`      | The HTTP status code represented by the response.                      |
| `message`   | Human-readable description of the result.                              |
| `data`      | The endpoint's response payload. `null` when the endpoint has no data. |
| `meta`      | Additional metadata, primarily used for pagination. Otherwise `null`.  |
| `errors`    | Error details when applicable. Otherwise `null`.                       |
| `timestamp` | Time at which the response was generated.                              |

The HTTP status code and the `code` field represent the same status.

---

## Successful responses

For a response containing data:

```json
{
  "success": true,
  "code": 200,
  "message": "Retrieval successful",
  "data": {
    "id": 123,
    "name": "PlayerOne"
  },
  "meta": null,
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

For an operation that does not return data:

```json
{
  "success": true,
  "code": 200,
  "message": "Operation successful",
  "data": null,
  "meta": null,
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

Basil uses `200 OK` with `data: null` for operations that would traditionally return `204 No Content`.

---

## Error responses

Failed requests use the same top-level envelope.

For example:

```json
{
  "success": false,
  "code": 404,
  "message": "User not found",
  "data": null,
  "meta": null,
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

A failed answer also names the failure in `errors`, which is the stable, machine-readable part:

```json
{ "success": false, "code": 403, "message": "Missing permissions.", "data": null, "meta": null,
  "errors": { "code": "permissionDenied", "missing": 4398046511104 }, "timestamp": "2026-08-03T12:00:00Z" }
```

`errors.code` is a camelCase code such as `notFound`, `locked`, `systemAccount`, `wrongPassword`, `rateLimited` or `invalid`. `errors.missing` is the bitmask of the permissions the caller lacks and is present only for a refusal for lack of permissions. The table that maps outcomes to codes and HTTP statuses is in [ADR-009](../../adr/ADR-009-rest-v1.md).

Clients should use the HTTP status code or `code` field to determine whether the request succeeded.

The `message` field is intended for human-readable information and should not normally be used as a stable machine-readable error identifier.

---

## Pagination

Paginated endpoints put pagination information in `meta`.

For example:

```json
{
  "success": true,
  "code": 200,
  "message": "Retrieval successful",
  "data": [
    {
      "id": 1,
      "name": "PlayerOne"
    },
    {
      "id": 2,
      "name": "PlayerTwo"
    }
  ],
  "meta": {
    "offset": 0,
    "limit": 20,
    "total": 42
  },
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

`data` contains the result items directly.

The pagination fields are `offset`, `limit` and `total`. Lists take `offset` (default 0) and `limit` (default 25, at most 100; a larger value is answered with 422).

---

## Embedded references

API responses include the information required to display referenced objects where appropriate.

For example, a user reference contains:

```json
{
  "id": 123,
  "name": "PlayerOne",
  "country": "vn"
}
```

Clients should not assume that a reference is represented only by an ID.

Beatmap references can similarly contain information about both the difficulty and its parent beatmapset.

---

## Enum values

Enum fields are serialized as camelCase strings, for example `"status": "loved"`. Sets of permissions and privileges are bitmasks and are serialized as numbers, for example `"permissions": 19`.

`Country` is a lowercase two-letter country code, for example `"country": "vn"`.

---

## PUT and PATCH requests

`PUT` and `PATCH` use different request schemas.

### PUT

`PUT` represents a full replacement.

Its request body contains all required fields for the resource.

### PATCH

`PATCH` represents a partial update.

Its request body contains only the fields that should be changed; every field is optional.

Do not assume that the `PUT` and `PATCH` request bodies have the same schema.

The generated OpenAPI documentation contains the authoritative schemas:

```text
https://api.<domain>/docs/
```

---

## Exceptions

The response envelope applies to JSON responses from the `api.` host.

It does **not** apply to:

* file downloads;
* `/live` Server-Sent Events streams.

These responses use their own content types and formats.

See [`sse.md`](sse.md) for the SSE protocol.

---

## See also

* [`overview.md`](overview.md): the `api.` host HTTP surface
* [`sse.md`](sse.md): live Server-Sent Events
* [`authentication.md`](../bancho/authentication.md): Bancho authentication
* [`protocol.md`](../bancho/protocol.md): Bancho binary protocol
* [`response-envelope.md`](../../for-developers/response-envelope.md): server-side envelope implementation
