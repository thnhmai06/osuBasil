# ADR-007 — BasilBot reply format

> Status: **Accepted 2026-09-03 (structured replies declined); amended 2026-10-22 (wording moved to an
> editable file, mechanism replaced).**

## Summary

BasilBot's replies stay natural-language chat text. The wording is not in code: it lives in the
operator-editable file `Data/Bot/replies.json`. A broad rewrite into machine-parsable replies was
considered and declined; machine-readable state is served by the HTTP API.

## Current mechanism

The earlier implementation (a `ReplyLocale` loader, category-nested `BasilBot.json`/`Irc.json` locale files, and
`static readonly` reply fields in `Basil.Application`) no longer exists. The redesigned bot replaced it:

* **One file.** `Data/Bot/replies.json` maps a reply key to an ICU MessageFormat string with named placeholders
  (`{room}`, plural selectors). There is one language and no fallback chain. The path is `RepliesOptions.Path`.
* **Defaults live in the bot.** Each reply is a typed key owned by `Basil.Bot` that carries its default wording and
  the arguments it declares. The file is written with every default when it does not exist.
* **Hot reload.** The file is watched; a save is applied while the server runs.
* **Failure keeps the bot talking.** A file that cannot be read, or whose JSON is broken, leaves the last good
  wording in use. A single text that is invalid or uses an argument its reply does not declare is skipped and its
  default is used. A key in the file that is no reply is ignored with a warning. Saying a reply never fails because of
  the file.
* **Tests** reference reply keys, never copy default text (AGENTS.md rule 9).

Related code: `src/Bot/Replies/` (`ReplyCatalog`, `RepliesWatcher`, `RepliesOptions`, `Reply`), and the bot
section of [`../for-agents/application-design.md`](../for-agents/application-design.md). Operator-facing behaviour is in
[`../for-client/basil-bot.md`](../for-client/basil-bot.md).

## History

* 2026-09-04: the user clarified the actual request behind Issue #4. The aim was not structured replies but reply
  text kept in natural language and moved out of C# source into one editable place. This was implemented and then
  split into per-surface locale files; both layouts were superseded by `replies.json`.
* Issue #4's literal wording (structured, machine-parsable replies) stays open and is still declined by the Decision
  below.

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
