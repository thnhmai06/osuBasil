# ADR-009 - REST API v1

> Status: Accepted. Derived from the approved plan (sections 8.2-8.4); the generated OpenAPI document is the
> route reference, this record only fixes the rules every route follows.

## Why

Basil's features other than the game protocols (tournament tooling, overlays, administration) are clients of the
server. They need one HTTP contract that is stable, documented by the code, and maps one to one onto what a referee
can do in game with `!mp`.

## Decisions

1. **Prefix and host.** Every route is under `/v1` on `api.<domain>`. `/health` and `/docs` sit outside `/v1`. Image
   hosts (`a.`, `assets.`, `osu.<domain>/ss`) are served by the same project but are not part of `/v1`.
2. **Authentication is a Bearer token only.** No cookie, so no CSRF. `POST /v1/auth/login` returns the token once;
   the server keeps only its hash. A token opens an `Api` connection; the host takes the actor from it and works
   through scopes (`x.ScopeTo(connection)`). The host contains no business rules.
3. **Public or token.** A resource without a permission limit is public (marked "public" in the OpenAPI document).
   A route that is permission limited, or that needs the caller as a representative, needs a token and answers 401
   without one. Anonymous callers have no permissions, so `CanView` hides private rooms, matches and hidden
   beatmapsets from them with 404.
4. **Envelope.** Every JSON response uses the envelope of `docs/for-client/api/response-envelope.md` (ADR-005 covers the exception to 500 case). Images, files and SSE are not enveloped. Errors
   carry a stable camelCase `code`; a permission refusal also carries the missing permission mask.
5. **Pagination.** `offset` and `limit`, default 0 and 25, maximum 100; a larger `limit` is 422.
6. **Enums** are camelCase strings in JSON.
7. **One route per room command.** `POST /v1/rooms/{id}/start`, `/kick`, `/ban` and so on mirror the `!mp`
   commands one to one, each with its own body in the schema. This deliberately departs from the "nouns in URIs"
   guidance: the mapping to `!mp` and the ability to describe and test each body outweigh URI purity. There is no
   `{command}` route.
8. **Aliases.** `/v1/me/...` and `/v1/sessions/...` only authenticate and answer `307` to the
   `/v1/users/{id}/...` form, keeping method and body.
9. **Live data is SSE on dedicated routes**, never content negotiation (see `docs/for-developers/sse.md`). Every
   stream follows one design: a root (one player, room, match or the server) owns a buffer; once a second the root
   bundles what arrived, serialises it once and fans the same bytes out to every stream; the first event is a
   snapshot; each stream has a bounded channel that drops the oldest bundle; the stream is tied to its Api
   connection and ends when that connection closes; every event marks the connection active; a final `server`
   event is sent at shutdown. Data is up to about one second old, and the API descriptions say so. All SSE routes
   need a token except `GET /v1/matches/{id}`.
    - *Boundary.* Application supplies only sources: attach and detach a watcher and a feed that never waits.
      The one-second tick, the buffer, the bundle, serialising once and the fan-out belong to Host.Rest, so no
      runtime model or registry holds a timer. A client that lags too long is disconnected and receives a new
      snapshot when it reconnects.
    - *Match report* (`GET /v1/matches/{id}`, public). The first event is the full report with its entries. The
      event `id:` is the stored order of the entry, not a counter of the root (the root is destroyed when its last
      viewer leaves), and events carry `retry:`. A closed match creates no root: the snapshot is built from the
      report and the stream ends. A reconnect whose `Last-Event-ID` is the last event of a closed match gets 204;
      reconnecting to a running match always gets a fresh snapshot. This supersedes the counter-based ids of
      `docs/for-developers/sse.md` for this route only.
10. **Images.** Every image is an ImageSharp.Web `IImageProvider`, which runs before routing. `CanView` is checked
    inside the provider; the provider never reads Bearer. The rate limiter runs before ImageSharp.Web, images use
    the `Anonymous.Assets` bucket and a 304 is not counted. The envelope skips images; OpenAPI uses stub endpoints
    so the images stay in the document.
11. **Rate limits.** Mutations are partitioned per authenticated user, anonymous requests per IP, images in their
    own bucket; failed logins per (IP, account) with a per-account backoff.
12. **CORS** allows no credentials and allows the `Authorization` header.
13. **Runtime policy.** CORS origins and rate limits are read from `ServerSettings` on each request or partition
    creation, so a change applies without a restart. A partition created earlier keeps its old limit until it idles.
14. **Concurrent streams** count toward `RateLimits.Connections.PerIp`, and token streams also per user.

## Outcome to HTTP

| Outcome | HTTP | Code |
|---|---|---|
| Missing permissions | 403 | `permissionDenied` (+ mask) |
| Not viewable (private room or match, hidden set); unknown session id or one of another user (`UserOutcome.NotFound`) | 404 | `notFound` |
| Wrong password | 403 | `wrongPassword` |
| State conflict (full, already seated, in another room, banned, not in room, target offline) | 409 | by outcome |
| Locked area | 409 | `locked` |
| Fixed BasilBot record (id 1) | 409 | `systemAccount` |
| Room closed | 410 | `roomClosed` |
| Invalid data, `limit` above 100 | 422 | `invalid` |
| Rate limit | 429 + `Retry-After` | `rateLimited` |
| Missing, expired or revoked token on a token route | 401 | `unauthorized` |
| Shutting down | 503 + `Retry-After` | `shuttingDown` |
| Reconnect to a closed match with its last event id | 204 | none |

The outcome to error-code table lives in `Basil.Host.Shared` (the console shares it); Rest adds the HTTP status.
Each row has a test.

## Consequences

- OpenAPI generated from code is the only reference; descriptions follow AGENTS.md rule 5.
- Postman checks the HTTP contract (status, envelope, schema, public/token matrix, redirects); xUnit checks
  behaviour. A rule is not tested twice.
- Remote logout of an osu! client only asks the device for its password again; changing the password is the real
  block. The route descriptions say so.

## Related code

`src/Hosts/Rest`, `src/Hosts/Shared`, `tests/Hosts/Rest`.
