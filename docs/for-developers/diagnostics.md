# Diagnostic API

## Overview

Basil runs as a private tournament server, usually operated by one person watching a match rather
than by a fleet behind an APM vendor. When something looks wrong during a live tournament -- the
process using more memory than expected, requests slowing down, a match stuck -- there is no
external monitoring stack to check. The Diagnostic API exists so an operator (or BasilBot's admin
surface, or a future dashboard) can ask the running process directly, over the same `api.<domain>`
host every other admin action already uses.

It is deliberately not a general-purpose metrics or tracing system. It reports the process's own
resource usage and Basil's own live counts -- nothing more.

## What it provides

Every diagnostic category is available two ways, both under `/diagnostic` and both gated by the
admin key (see [`configuration.md`](../for-technicians/configuration.md) for how the key is
configured):

* `GET /diagnostic/{category}` -- a point-in-time reading.
* `GET /diagnostic/{category}/live` -- the same reading pushed over Server-Sent Events, about once
  a second.

The categories are `process`, `gc`, `threadpool`, `runtime`, `exceptions`, `http`, and
`application`. `GET /diagnostic/live` is a curated overview stream: a small, hand-picked subset of
the categories above (process CPU and memory, time in GC, active HTTP requests, exceptions thrown,
logged-in sessions, running matches, connected live-stream subscribers) meant for one screen an
operator glances at during a tournament, not the union of everything.

`POST /diagnostic/gc/collect` is the one supported action: force a blocking collection, to tell
retained memory apart from memory that simply has not been collected yet.

The exact request/response shapes, field meanings, and error responses are documented on the routes
themselves and appear in the generated API reference at `api.<domain>/docs/` (see
[`docs/index.md`](../index.md)). This page does not repeat that reference; it explains why the
category exists and how the live side of it is built.

## How it works

### Samplers, not a metrics pipeline

Each category has its own sampler (`ProcessSampler`, `GcSampler`, `HttpSampler`,
`ExceptionsSampler`, `ApplicationSampler`, plus `ThreadPoolSnapshot`/`RuntimeSnapshot` for values
cheap enough to read inline). A sampler's job is narrow: read whatever the .NET runtime or Basil's
own state already exposes, and shape it into the record the route returns. Nothing here builds a
generalized time-series store -- there is no history, no retention window beyond the `http`
category's own rolling duration distribution, and no cross-process aggregation.

`RuntimeMeterListener` is the one long-lived exception to "sample on demand." Several counters
(exceptions thrown, active HTTP requests, SSE subscriber counts, active matches and channels) are
only ever exposed as push-based `System.Diagnostics.Metrics` instruments, several of which do not
exist until other parts of the host construct them. A listener created on demand would have no
baseline to report against, so exactly one `RuntimeMeterListener` runs for the whole process
lifetime, and every sampler that needs one of its counters reads its accumulated fields.

### One collection per tick, shared by every subscriber

`DiagnosticBroadcastService` ticks once a second. For each category, it checks whether
`ILiveEventHub` currently has any subscriber to that category's stream; if not, the tick for that
category is a no-op -- no sampling, no serialization, no work beyond the check itself. If it does
have a subscriber, the category is sampled and serialized **once**, and the resulting bytes are
handed to `ILiveEventHub.Publish`, which broadcasts the same buffer to every current subscriber of
that stream.

This is the central design property of the live side of this API: the cost of watching a category
goes from "nothing" (zero subscribers) to "the cost of one sample and one serialize" (one or more
subscribers), and adding further subscribers to an already-watched category adds delivery cost
(one SSE frame per connection) rather than another round of sampling. `http` is the one category
where a plain `GET` and the live stream must not race over the same accumulator; the live stream is
the sole caller allowed to reset the request-duration window it reports, so checking the plain
reading never disturbs it. See [`sse.md`](sse.md) for how the underlying hub, subscriptions, and SSE
plumbing work in general; this page only covers what Diagnostics does with it.

### Measured overhead (Task 5.7)

The shared-collection claim above was measured, not just asserted. Driving the app in-process
through `WebApplicationFactory` and reading `Process.TotalProcessorTime` and
`GC.GetTotalAllocatedBytes` over 30-second windows on the development machine:

| Condition                | CPU (of one core) | Allocation rate |
| ------------------------ | ------------------ | ---------------- |
| Idle, no subscribers     | 0.36%               | 384 B/s           |
| 1 subscriber on `/diagnostic/live`  | 2.19%   | 11,653 B/s        |
| 10 subscribers on `/diagnostic/live` | 3.12%  | 43,527 B/s        |
| Idle again (drift check) | 0.16%               | 3,675 B/s         |

Going from 1 to 10 subscribers -- a 9x increase in subscriber count -- raised CPU by about 1.4x and
allocation by about 3.7x, not the roughly 9-10x a per-connection re-sample would produce. The
marginal cost per additional subscriber beyond the first works out to roughly 31 ms of CPU and
106 KB of allocation over the whole 30-second window (about 3.5 KB/s each), an order of magnitude
below the cost the *first* subscriber carries. The first subscriber's own premium (about 547 ms of
the 656 ms total) is not sampling -- the same one sample runs whether one or ten are connected --
it is one-time connection-establishment and first-traversal JIT cost for the SSE path. What
scales with subscriber count is delivery: a channel write, a UTF-8 decode, and a frame re-encode
per connection, not another sample. The harness ran in the same process as the client connections
it drove, so the raw numbers include client-side reading-loop cost alongside the server's; the
ratio between phases -- strongly sub-linear growth from 1 to 10 subscribers -- is what the design
claim is actually about, and it held. The harness itself was not committed (see "Testing" below)
-- these numbers are the durable record of that run.

### Admin-only, and no new slice edges to get there

Every route under `/diagnostic` requires the admin-key authorization policy
(`AdminKeyDefaults.Policy`); nothing here is reachable without it. This is the one adjacency edge
Diagnostics has onto another slice (`Diagnostics -> Auth`, for `AdminKeyDefaults`).

`ApplicationSample`'s match, channel, and IRC-session counts belong to Multiplayer, Chat, and Irc,
not to Diagnostics. Rather than referencing those slices' registries directly -- which would add
three more adjacency edges for a feature that only needs to read a count -- each of those slices
publishes its own count as an observable gauge, and `ApplicationSampler` reads it back through
`RuntimeMeterListener`. The owning slice is still the only one that decides what "active" means for
its own state; Diagnostics only reads the number it already publishes.

## Testing

`DiagnosticBroadcastServiceTests` pins the shared-collection contract directly: it opens several
subscriptions to one category, runs a single broadcast tick, and asserts every subscriber received
the identical published buffer (compared by the underlying `ReadOnlyMemory<byte>`'s buffer
reference, not just equal bytes) at the same version. That test fails if the implementation ever
starts serializing a fresh payload per subscriber, independent of what the actual sampled values
happen to be at the time.

The CPU/allocation numbers above came from a temporary `[Fact]` built for Task 5.7 and deleted
before committing, following the same pattern used elsewhere in this codebase for one-off
measurements: a three-phase, tens-of-seconds-per-phase timing run has no assertion that would stay
non-flaky on a loaded machine, so the numbers were recorded here instead of pinned as a test.
`DiagnosticEndpointTests` covers the HTTP-level contract (the admin-key gate on every route, a
live stream's self-seeded first event, the curated overview's fields, the one supported action).

## Related code

* [`src/Basil.Server/Features/Diagnostics/DiagnosticBroadcastService.cs`](../../src/Basil.Server/Features/Diagnostics/DiagnosticBroadcastService.cs)
* [`src/Basil.Server/Features/Diagnostics/RuntimeMeterListener.cs`](../../src/Basil.Server/Features/Diagnostics/RuntimeMeterListener.cs)
* [`src/Basil.Server/Features/Diagnostics/DiagnosticRoutes.cs`](../../src/Basil.Server/Features/Diagnostics/DiagnosticRoutes.cs)
* [`src/Basil.Server/Features/Diagnostics/DiagnosticsServiceCollectionExtensions.cs`](../../src/Basil.Server/Features/Diagnostics/DiagnosticsServiceCollectionExtensions.cs)
* [`tests/Basil.Server.Tests/Features/Diagnostics/DiagnosticBroadcastServiceTests.cs`](../../tests/Basil.Server.Tests/Features/Diagnostics/DiagnosticBroadcastServiceTests.cs)
* [`tests/Basil.IntegrationTests/DiagnosticEndpointTests.cs`](../../tests/Basil.IntegrationTests/DiagnosticEndpointTests.cs)

## See also

* [`sse.md`](sse.md): the live-updates pipeline Diagnostics builds on
* [`architecture.md`](architecture.md): slice boundaries and the `SliceAdjacency` allowlist
* [`for-technicians/configuration.md`](../for-technicians/configuration.md): the admin key
