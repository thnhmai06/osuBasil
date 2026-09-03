# ADR-007 — BasilBot reply format

> Status: **Proposed 2026-09-03. Recommendation: do not implement broadly.** Written unattended
> (user asleep, blanket "consider it all approved" authorization given beforehand) — this ADR
> itself is the checkpoint: it recommends *against* the literal broad reading of the Issue #4 item
> it responds to, so implementation was deliberately not started pending the user reading this and
> either confirming the recommendation or overriding it with a narrower concrete scope.

## Problem

Issue #4 (BasilBot): *"✨ Replace natural language bot responses with structured, easily
machine-parsable formats."* No specific replies, commands, or target format are named.

## Evidence

- `docs/for-client/basil-bot.md` (existing, authored earlier in this project's life, not by this
  session) already states the project's position on this exact question: *"Clients, tournament
  tooling, and automated integrations should not assume that arbitrary human-readable text
  represents a stable command result unless that behaviour is explicitly documented."* Reply text
  is documented as a stable *string* contract (`MpReplies`/`IrcReplies`, CLAUDE.md rule 9 — every
  reply is a named constant, pinned by tests), but the project has already, deliberately, declined
  to also promise it as a *data* contract.
- Basil's REST API already covers essentially every piece of state a `!mp` command touches:
  settings, slots, refs, bans, timer, chat, live SSE streams — all JSON, all documented via
  OpenAPI, all exercised by this session's own multi-round naming/DTO/response-example audit.
  Tournament tooling that wants machine-readable match data already has a first-class path that
  does not involve scraping chat text.
- `CLAUDE.md`'s own scope discipline: *"Basil deliberately has a smaller feature surface... Do not
  implement a bancho.py feature simply because it exists upstream"* and rule 2, *"do not add...
  unused abstractions... future-proofing without a concrete requirement."* The issue item names no
  concrete consumer that is blocked today by natural-language replies.
- `MpReplies.cs`/`MpCommandService.cs` are 426/1424 lines respectively; converting every reply to
  a dual human+machine format (or replacing human phrasing outright with something like
  `key=value` pairs) touches essentially the entire bot command surface — this is not a scoped
  patch under any reading.

## Constraints

- No named consumer, no named target format — implementing the literal broad reading is
  implementing a guess, which CLAUDE.md rule 1 ("do not silently invent requirements") and rule 2
  ("smallest solution... no speculative features") both weigh against.
- Rewriting every `!mp` reply is itself a user-visible contract change (CLAUDE.md rule 9): every
  existing tournament caster/overlay/script that currently reads BasilBot's chat text (however
  unsupported that is per `basil-bot.md`) would break, for a benefit that already has a supported
  channel (the API).

## Alternatives

**A. Broad rewrite**: convert every (or most) `!mp`/general reply to a structured wire-ish format
(e.g. `key=value` tokens, or a JSON blob posted as chat text). Satisfies the issue literally.
Rejected as the default action: no concrete consumer named, duplicates data the API already
serves, breaks the existing documented "chat text is not a stable data contract" position without
a stated reason to reverse it, and is a large surface (see Evidence) to commit to on a guess.

**B. Do nothing; close as already addressed by the existing API + `basil-bot.md`'s documented
position.** Defensible given the evidence above, but silently dropping a user-filed issue item
without surfacing the reasoning is worse than writing this ADR — hence writing it instead of
simply skipping the item.

**C. Narrow, evidence-driven scope: leave general/most `!mp` replies as natural language (matching
`basil-bot.md`'s existing position and real precedent), but identify the *specific* replies that
are already borderline-structured today and would cost little to make consistently parseable,
without inventing a new format wholesale.** Looking at the current reply surface, `!mp settings`
is the one candidate that stands out: it already prints multiple `Label: value` lines
(`SettingsRoomName`, `SettingsBeatmap`, `SettingsTeamMode`, `SettingsActiveMods`,
`SettingsCreator`) in a fixed, already-line-oriented shape — closer to "accidentally
semi-structured" than genuine prose. Even this is not adopted below (see Decision) for lack of a
named consumer, but it is the one place a narrower future ask could reasonably land.

## Decision

**Recommend against implementing this broadly right now.** No code changes made. Reasoning:

1. The project has already made and documented this exact call (`basil-bot.md`), in the direction
   opposite to the issue's literal wording, before this session started — reversing it needs a
   reason beyond "the issue asks for it," and none is given in the issue text itself.
2. The concrete need this issue item is presumably protecting against — tournament tooling unable
   to get machine-readable match state — is already met by the API, which this exact session spent
   multiple rounds auditing and hardening for consistency (naming, DTOs, response examples).
3. Implementing Alternative A on no concrete spec risks building the wrong shape and then having
   to break it again once a real consumer's actual requirements surface — worse than not building
   it yet.

**This is a recommendation, not a refusal — it's the one action item from this session's queue
that gets a documented "don't build this yet" instead of a build.** If the user's actual intent
is narrower than the issue's literal wording (e.g., "make `!mp settings`'s output easier for a
casting overlay to scrape" specifically), that is a small, well-scoped follow-up (Alternative C)
worth doing on its own — but needs that concrete target stated first, not inferred.

## Trade-offs

- Leaves the issue item open/unaddressed in code. Documented here rather than silently dropped, so
  the decision and its reasoning are visible and reversible.
- If a real consumer does exist and simply wasn't named in the issue text, this ADR under-delivers
  until that's clarified.

## Measurements

Not applicable — no implementation, no behavior change.
