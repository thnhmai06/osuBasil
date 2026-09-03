# ADR-007 — BasilBot reply format

> Status: **Proposed 2026-09-03, recommended against a broad rewrite. User overrode that
> recommendation 2026-09-04 and clarified the actual ask — implemented, see "What was actually
> built" below.**
>
> The Decision section below is kept as written: it answers Issue #4's literal wording ("structured,
> machine-parsable formats") and the reasoning for declining that broad reading still stands. When
> asked to clarify, the user's real want turned out to be different from the issue's literal
> wording — reply text moved out of C# source into an editable locale file, wording kept as natural
> language. That is a smaller, well-scoped, non-breaking change and was implemented directly rather
> than needing its own ADR.

## What was actually built (2026-09-04)

The user's clarification, verbatim intent: *"Vẫn tuân theo ngôn ngữ tự nhiên, chỉ là phản hồi cần tự
nhiên và nhanh chóng nắm bắt được thông tin. Đề nghị tạo file ghi hết cách trả lời vào một chỗ (kiểu
locale) thay vì cố định trong code"* — keep natural language, but move every reply's wording out of
hardcoded C# into one locale-style file, editable without a rebuild.

This is **not** Issue #4's "structured, machine-parsable formats" ask — the wire shape (plain
natural-language strings, `string.Format` placeholders) is unchanged, so this ADR's Decision
against a broad *format* rewrite still holds and Issue #4's literal item is **still open**. What
changed is where the wording lives, not its shape.

Implementation:

- `src/Basil.Application/Data/Locale/replies.json` — single file, two top-level objects (`Mp`,
  `Irc`), each mapping a reply's C# member name to its text. All 122 `MpReplies` members and all 32
  `IrcReplies` members now live here; none are hardcoded string literals in the `.cs` files anymore.
- `ReplyLocale` (`src/Basil.Application/Services/ReplyLocale.cs`) — loads the file once via
  `AppContext.BaseDirectory` (not the working directory: this needs to resolve identically whether
  the entry point is `Basil.Web`, `dotnet test` on a project that references `Basil.Application`, or
  a published, self-contained executable — matching the existing `MenuIconService` precedent for
  that exact resolution strategy). Exposes `ReplyLocale.Mp(key)`/`ReplyLocale.Irc(key)`, throwing a
  clear exception naming the missing key and file path if a lookup fails.
- `MpReplies`/`IrcReplies` — every `public const string X = "literal";` became
  `public static readonly string X = ReplyLocale.Mp(nameof(X));` (or `.Irc(...)`). The public shape
  is unchanged: same member names, same type, same call sites — none of the ~3,900 existing
  references to these two classes across the codebase (production or test) needed to change. Using
  `nameof(X)` as the lookup key means a member's name and the file's key can never drift apart by a
  typo; a genuine mismatch (a renamed member, a deleted file entry) surfaces as a startup exception
  naming exactly what's missing.
- `Program.cs` touches one member of each class during `InitializeDataAsync`, before the app starts
  accepting connections, specifically so a missing/malformed locale file is a boot-time failure
  (loud, in the startup log) rather than one discovered whenever a live chat command first happens
  to touch the affected reply.
- `Basil.Application.csproj` ships the file via `CopyToOutputDirectory`, which — because MSBuild
  propagates `Content` items with that metadata to every project that references it — is what makes
  it appear next to every consumer's own build output (`Basil.Web`, and every test project), not
  just `Basil.Web`'s.
- Regression tests (`ReplyLocaleTests`): every `MpReplies`/`IrcReplies` member resolves to non-empty
  text, and the file carries no key that no member reads (drift in either direction). Verified by
  deleting one key from the file and re-running: `MpReplies`'s static constructor throws
  `Reply locale file is missing 'Mp.CreateFailed' (expected at ...)`, cascading to every test that
  touches the class (40/49 in that project failed) — confirming the failure is loud and points
  straight at the cause, not a silent empty string.

One caller needed a real code change beyond the mechanical rewrite: `MpCommandService.Set` declared
`const string usage = MpReplies.SetUsage;` — a local `const` requires a compile-time constant, which
`MpReplies.SetUsage` no longer is now that it's a `static readonly` field resolved at runtime.
Changed to `var`; behavior identical.

Not built (matches the "no i18n system" scope call from this same conversation): no culture
selection, no fallback chain, no hot-reload, no pluralization. One file, one language, loaded once
per process lifetime.

Full suite: 1553 tests pass (was 1549, +4). Release build clean; verified the file reaches the
`win-x64` publish output alongside the DLLs (`Data/Locale/replies.json`), and that ASP.NET Core's
build-time OpenAPI doc generation — which runs the app — resolves it correctly, the same trap
`ccc804f` (moving `appsettings.json`) hit earlier in this session.

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
