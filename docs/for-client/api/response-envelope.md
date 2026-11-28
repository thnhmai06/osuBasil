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

Basil uses `200 OK` with `data: null` for operations that would traditionally return `204 No Content`. The one `204` of
the API is outside the envelope: a live stream that has already ended answers it to a reconnecting browser (see
[`sse.md`](sse.md)). An operation accepted for work that finishes later answers `202`, and a created resource `201`.

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
{ "success": false, "code": 403, "message": "privilegeDenied", "data": null, "meta": null,
  "errors": { "code": "privilegeDenied" }, "timestamp": "2026-08-03T12:00:00Z" }
```

`errors.code` is a stable camelCase code such as `notFound`, `locked`, `systemAccount`, `wrongPassword`, `rateLimited` or `invalid`. A refusal by the rules of the system carries the name of the refusal as its code, for example `roomClosed` or `nameTaken`. The status says the kind of failure:

| Status | Meaning | `errors.code` |
| --- | --- | --- |
| `401` | A valid Bearer token is required, or the sign-in credentials are wrong. | `unauthorized`, `invalidCredentials` |
| `403` | The caller lacks the privilege, the room role or the right to act on that account, or the password is wrong. | `privilegeDenied`, `wrongPassword` and the name of the refusal |
| `404` | The thing does not exist, or the caller may not see it. | `notFound` |
| `409` | The state of the system does not allow it, or an area is locked. | `locked`, `systemAccount` and the name of the refusal |
| `410` | The room or channel is closed. | the name of the refusal |
| `413` | The body is larger than the route allows. | `tooLarge` |
| `422` | The input is not valid; `message` names the field. | `invalid`, or a specific code such as `invalidName` |
| `429` | Too many requests; `Retry-After` says when to try again. | `rateLimited` |
| `500` | An unexpected failure; the answer carries no detail. | `internalError` |
| `503` | The server is stopping; `Retry-After` says when to try again. | `shuttingDown` |

A body that is missing, is not a JSON object, lacks a required field or holds a mistyped one is answered with `422` and `invalid`, and `message` names the field. A parameter that cannot be read (a query value that is not a number, or a number out of range) is answered with `400` and `invalid`. A page request outside `offset >= 0` and `limit` from 1 to 100 is `422`. The generated reference lists the statuses each route can answer.

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
    "hasNext": true
  },
  "errors": null,
  "timestamp": "2026-08-03T12:00:00Z"
}
```

`data` contains the result items directly.

The pagination fields are `offset`, `limit` and `hasNext`, which tells whether items follow this page; the total number of items is not reported. Lists take `offset` (default 0) and `limit` (default 25, at most 100; a larger value is answered with 422).

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

Enum fields are serialized as camelCase strings, for example `"status": "loved"`. A set of flags (privileges, mods) is an array of names, for example `"privileges": ["player", "supporter"]`, in a response and in a request.

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
https://api.<domain>/docs/api/
```

---

## Exceptions

The response envelope applies to JSON responses from the `api.` host.

It does **not** apply to:

* file downloads;
* Server-Sent Events streams.

These responses use their own content types and formats.

See [`sse.md`](sse.md) for the SSE protocol.

---

## See also

* [`overview.md`](overview.md): the `api.` host HTTP surface
* [`sse.md`](sse.md): live Server-Sent Events
* [`authentication.md`](../osu-stable/authentication.md): osu! (stable) authentication
* [`protocol.md`](../osu-stable/protocol.md): osu! (stable) binary protocol
* [`response-envelope.md`](../../for-developers/response-envelope.md): server-side envelope implementation
