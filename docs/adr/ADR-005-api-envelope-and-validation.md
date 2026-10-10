# ADR-005 — API envelope, validation, and JSON pipeline

> Status: **Accepted (retroactive).** Not a decision gate — the perf-investigation plan marks
> only ADR-001/003/004 `[GATE]`. Written after most of this ADR's scope was implemented, to
> record the decisions made and the scope still open, per the plan's rule that every rebuild
> gets an ADR + design contract.

## Problem

RC2/RC6 (this investigation's evidence): no exception→response mapping existed anywhere on the
`api.` host, so a genuine application exception thrown by a route handler propagated past
`EnvelopeMiddleware` to a bare, unenveloped 500 — breaking the envelope contract the host
promises on every response. Separately, `EnvelopeMiddleware` held its own
`JsonSerializerOptions` instance (plain web defaults), diverging from the shared
`BasilJsonOptions.Instance` every route handler actually serializes its body with — and the
default web encoder escapes ordinary ASCII punctuation (a literal `+`) as a 6-character Unicode
sequence, needlessly bloating and obscuring any string field that contains one.

## Decision (implemented this session)

1. **Exception → 500 envelope, api. host only.** `ExceptionLoggingMiddleware` — already the
   single insertion point logging every unhandled exception on every host group — now also
   writes a 500 envelope for the `api.` host specifically, provided the response hasn't started
   writing yet. Every other host group (bancho, osu-web, beatmap-assets, avatar) keeps its
   previous bare-response behavior unchanged; none of them carry an envelope contract to uphold.
   The `HasStarted` guard is what makes this safe for the `api.` host's own non-enveloped routes
   too: an SSE `/live` stream that has already flushed an event, or a file download mid-copy,
   both have `HasStarted == true` by the time an exception reaches this middleware, so they fall
   through to the unchanged rethrow rather than an attempted (and impossible) retroactive wrap.
2. **One shared `JsonSerializerOptions` instance.** `EnvelopeMiddleware`'s own instance was
   replaced with `BasilJsonOptions.Instance`, the same instance every route handler already used
   to serialize its body. This was already the only other stray instance in the codebase
   (`OpenApiExampleExtensions` already reused the shared one); there is now exactly one.
3. **Relaxed JSON encoding.** `BasilJsonOptions.Instance` sets
   `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Safe under this API's contract:
   responses are documented and consumed as JSON, never embedded into an HTML document, so the
   HTML-safety escaping the default encoder performs buys nothing and only bloats/obscures
   payloads. Precisely: the *parsed* JSON value a client's deserializer produces is identical
   either way — a string containing a literal plus sign decodes to the same value whether the
   encoder wrote that character as-is or as its escaped Unicode form; what changes is only the
   *raw wire representation* — a consumer doing anything below the JSON layer
   (byte-for-byte diffing, hashing/checksumming the raw response body, log-grepping raw bytes)
   would observe different bytes than before. No such consumer exists in this codebase today.
4. **N+1 fixes** (not strictly "pipeline" but the two concretely-evidenced query-count bugs
   found in this scope): `GET /beatmapsets` batches every page item's beatmap count into one
   `IBeatmapRepository.FetchCountsBySetIdsAsync` call instead of one `FetchAllBySetIdAsync` per
   item; `MatchReportService.BuildAsync` memoizes user/beatmap resolution per report build
   instead of re-resolving the same recurring player or beatmap once per round/event/score.

## Evidence

Every fix above was verified by writing a regression test, then temporarily reverting the fix
and confirming the test fails as expected, then restoring it — not just "it compiles":
`ExceptionEnvelopeEndpointTests`, `BasilJsonOptionsTests`, `BeatmapsetListEndpointTests`,
`MatchReportServiceTests.BuildAsync_SameUserAndBeatmapAcrossRounds_ResolvesEachOnlyOnce`.

## Explicitly not done in this pass (open scope)

- **Full naming/DTO/OpenAPI audit** against Issue #4's API cluster (messages, examples,
  pagination, DELETE body, a `text` field naming concern, and so on) — this is a broad,
  many-small-decisions audit better scoped as its own pass with its own checklist, not folded
  into this session's fixes. The osu!api v2 structural reference already gathered in the main
  investigation blueprint (event types, game object, score match-context) remains the reference
  point for that audit.
- **`EnvelopeMiddleware`'s own 2 internal throw sites** (`JsonNode.ParseAsync`,
  `GetValue<int>()`) — still `SUPPORTED` (code trace only), never reproduced. Left as a known
  limitation; low risk since a route handler that returns malformed JSON or a schema mismatch
  here would be its own bug to fix at the source, not a gap in this middleware's own exception
  safety. This is covered by the new `ExceptionLoggingMiddleware` mapping regardless of exactly
  where inside `EnvelopeMiddleware` the throw happens (both sites run after `await next(context)`
  returns, not inside it) — `EnvelopeMiddleware.InvokeAsync` in its entirety *is*
  `ExceptionLoggingMiddleware`'s `next`, so any exception anywhere in it, before or after its own
  inner `next` call, is still caught by the outer middleware's `try`.
- **Source-generated JSON** — considered per the original plan's mention, not pursued: no
  measured allocation/CPU problem in the JSON serialization path specifically (the confirmed
  bottleneck this whole investigation targets is the SQLite write path, RC1). Revisit only if a
  future profile shows serialization itself as a bottleneck.
- **Test gap found on review, not yet written**: `ExceptionEnvelopeEndpointTests` covers the
  case where a handler throws before writing anything. Missing: a regression test pinning that a
  handler which has already started writing its response *before* throwing does **not** get a
  retroactive envelope attempt (the `HasStarted` guard in `ExceptionLoggingMiddleware`) — this is
  exactly the SSE/file-download carve-out described in Decision item 1 above, and it currently has
  no test coverage of its own.
- **Test gap found on review, verified**: `ExceptionEnvelopeEndpointTests.
  RouteHandlerThrows_ReturnsEnvelopedServerError` asserts status code (500) and the deserialized
  envelope's `Success`/`Code` fields, but never asserts the response's `Content-Type` header
  explicitly (`ReadFromJsonAsync<T>` succeeds without requiring the header to be
  `application/json`) — a regression that dropped or wrong-typed the header would not fail this
  test today.

## Trade-offs

The relaxed encoder is a global setting on the one shared options instance — it affects every
JSON payload the server ever writes, not just the ones known to contain a `+`. This is the
correct scope for the fix (a shared instance is the whole point of consolidating to one), and
the trade-off (slightly less aggressive escaping) is accepted per the reasoning in Decision
item 3.

## Measurements

`BeatmapsetListEndpointTests` and `MatchReportServiceTests`'s new tests assert call counts
directly (1 batched call instead of N, or 1 resolution instead of N repeats) rather than timing
— a deterministic, environment-independent way to pin a query-count regression, matching this
investigation's general preference for asserting the mechanism rather than a wall-clock number
subject to machine variance.
