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
2. **One shared `JsonSerializerOptions` instance.** `EnvelopeMiddleware`'s own instance was
   replaced with `BasilJsonOptions.Instance`, the same instance every route handler already used
   to serialize its body. This was already the only other stray instance in the codebase
   (`OpenApiExampleExtensions` already reused the shared one); there is now exactly one.
3. **Relaxed JSON encoding.** `BasilJsonOptions.Instance` sets
   `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Safe here: every consumer of this
   API parses the response as JSON, never embeds it into an HTML document, so the HTML-safety
   escaping the default encoder performs buys nothing and only bloats/obscures payloads.
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
  safety (which the new `ExceptionLoggingMiddleware` mapping now also covers, since these throws
  happen inside `next(context)` from `ExceptionLoggingMiddleware`'s perspective).
- **Source-generated JSON** — considered per the original plan's mention, not pursued: no
  measured allocation/CPU problem in the JSON serialization path specifically (the confirmed
  bottleneck this whole investigation targets is the SQLite write path, RC1). Revisit only if a
  future profile shows serialization itself as a bottleneck.

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
