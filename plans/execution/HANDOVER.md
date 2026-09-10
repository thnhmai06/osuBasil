# Handover — osuBasil architecture migration

**Written 2026-09-11.** For a successor agent with no prior context. Read this first, then
`plans/basil-plan-20260909.md`. Everything here is verifiable from the repository; where it is not,
it says so.

---

## 1. Where things stand

Branch **`feat/vsa-migration`**, HEAD `b42967c0`, pushed. Working tree clean. A second worktree sits
at `V:\Code\cs\osuBasil-diagnostics` on `fix/integration-test-order-dependence` at `7b0ea3bd`,
already merged into the main branch — it is a spare lane, not pending work.

**The suite is fully green with no known failures**, which has not been true for most of this
migration. Run it as five separate calls, never as one (§5):

| Project | Count |
|---|---:|
| `Basil.ArchitectureTests` | 7 |
| `Basil.Domain.Tests` | 114 |
| `Basil.Protocol.Tests` | 158 |
| `Basil.Server.Tests` | 1057 |
| `Basil.IntegrationTests` | 363 |
| **Total** | **1699** |

Route table: **140** literal patterns from
`grep -rhoE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' src/Basil.Server --include=*.cs | sort -u | wc -l`.
Treat that number with suspicion — see §4.

### Stages

| Stage | State |
|---|---|
| **A** — make constant-mediated coupling visible | Done. ADR-008. |
| **B** — untangle before anything moves | Done, all six tasks. |
| **F** — the Diagnostic API | Done and merged. |
| **C** — extract the business layer | C4 done, C3 done, C2 **off the path**, C6 done. **C1 and C5 remain.** |
| **D** — split the transports | Not started. 15 plan items. |
| **E** — declare what survives, enforce it | Not started. 6 items. |
| **G** — the load harness | Not started. Unblocked now that F is merged. |
| **H** — documentation and final verification | Not started. |

**Stage C runs C4 → C3 → C6 → C1 → C5.** Not numbered order.
`plans/execution/stage-c-order-decision.md` says why: a half-finished restructuring is a compiling
tree with a measurable edge count, and a half-finished ninety-six-file project move is not.

---

## 2. The next task: C1

**Move the business layer into `Basil.Domain`.** About ninety-six files. It is the largest single step
left and the one with the least margin for error.

Its blockers are cleared. C6 built the rule that watches the internal graph of `Basil.Domain`, and it
had to exist *before* C1 rather than after — the reasoning is in `plans/basil-plan-20260909.md` Task
C6, and it is the single most important thing to understand before starting C1.

Two things are known about C1 that the original plan text does not say:

* **`MatchSession` is still one 584-line class** holding both the business state of a match and its
  SSE projection machinery — nine `StateStream<T>` fields, `SseSubscriberRegistry`, `SequenceGate`,
  sixteen references in all. C1 splits it: business state to `Basil.Domain`, projection stays behind.
* **C1 absorbs what C2 was going to do to `GameSession.Match`.** Once the business half is a Domain
  type, `Shared/Sessions/GameSession.cs` stops naming `Features.Multiplayer` and that
  `Shared → Features` edge goes with it, for free. Do not rewrite forty-five read sites separately.
  See `plans/execution/c2-deferred-decision.md`.

Move **one feature per commit**. A type moved and not yet rewired across nine slices is a broken tree.

---

## 3. Decisions already taken — do not re-litigate these

Each was decided with evidence and is recorded. A successor re-opening one costs a session and
usually reaches the same answer.

| Document | Settles |
|---|---|
| `docs/adr/ADR-008-dependency-enforcement.md` | values crossing a boundary are `static readonly`, not `const`, because NetArchTest reads IL and C# inlines constants |
| `stage-c-order-decision.md` | the order of Stage C, and the measured payoff of C4 |
| `c2-deferred-decision.md` | why C2 is off the path — its own proof is unreachable by it |
| `hub-adoption-decision.md` | the event hub carries deltas only; the seed handshake was deleted because `SeedIfNotSuperseded` had no callers |
| `logout-as-event-decision.md` | logout uses an ordered handler list, **not** an event bus — there is no domain-event bus in this codebase, and `Shared/Eventing` is entirely SSE machinery |
| `diagnostics-boundary-decision.md` | `Diagnostics → Auth` authorised; four other edges refused in favour of published gauges |
| `architecture-progress.md` | the instrument map in §4, and what the C5 gate is allowed to gate on |

**Two plan documents are stale in specific places and say so inline.**
`phase-1-multiplayer.md`'s "Task 1.3's design question" weighs two candidates that
`hub-adoption-decision.md` rejects in favour of a third, and `architecture-target-20260908.md` §2
carries a visible correction block about a measurement error. Prefer the decision documents.

---

## 4. The instrument map — the most expensive knowledge here

Four things measure coupling on this project. **They cover different populations and they fail in
different directions.** Most of a day went into learning this, and a successor who assumes one number
means "the coupling" will draw a false conclusion.

| Instrument | Population | Enforced | Blind to |
|---|---|---|---|
| `SliceAdjacency` + `SliceBoundaryTests` | `Features/<Slice>` → `Features/<Slice>` | every build | `const` values, until ADR-008 |
| `Shared_Should_Not_Reference_Features` pinned list | `Shared/` → `Features/` | every build, exact set equality | nothing known |
| `DomainAdjacency` + `DomainBoundaryTests` | inside `Basil.Domain`, population read from the assembly at run time | every build | nothing known |
| `plans/execution/measure-slice-graph.py` | files owned by a slice-named directory, in `Features/` or `Domain/` | nothing — it is a report | three things, below |

The script has **three proven blind spots**:

1. **Inferred types.** `sender.IrcConnection` is typed `IIrcConnection`, a `Features.Irc` type, but
   that name is never written in the file. The script reads source text, so it cannot see it. Task C4
   hit exactly this: it predicted `Bot → Irc` was gone, the script agreed, and deleting the allowlist
   row failed the build.
2. **`Shared/` as a source.** It counts edges *sourced from* slice-named directories, so
   `Shared/Sessions/PlayerLogoutService.cs` importing five slices was invisible. Task C3 removed that
   coupling and **every script number stayed identical**.
3. **Namespaces that are not slices.** It takes slice names from the directory listing under
   `Features/`, so `Channels`, `Login` and `Social` in `Basil.Domain` were never examined. C6 measured
   the Domain graph from the assembly instead and found **six edges where the record said three**.

### The rules that follow

* **An edge counts as removed only when its `SliceAdjacency` row can be deleted and the architecture
  suite stays green.** The script says where to look. The allowlist says whether it worked.
* **Name the currency before claiming a win.** C3's was the pinned list; C4's was allowlist rows;
  C1's will be the pinned list and `DomainAdjacency`. Without this, a task that moved nothing reads as
  progress on whichever number happened to drift.
* **The route grep is weak.** It records only the string inside the `Map*` call, so for a route mapped
  inside a `MapGroup` it captures the *suffix* and cannot see a changed group prefix; it deduplicates;
  and it cannot resolve an interpolated pattern. The Roslyn endpoint map reports 151 endpoints with
  full patterns. **Stage D moves routes into three host projects — exactly when group prefixes move —
  so verify Stage D with the endpoint map, not the grep.** Task H4 has this written in.

---

## 5. Operating rules learned the expensive way

Every one of these cost a worker session or a wrong answer.

* **Never run `dotnet test` over the whole solution in one call.** It exceeds the 600-second tool
  ceiling, the tool backgrounds it, and the worker stalls. Build once with
  `dotnet build --configuration Debug`, then one **foreground** call per test project with
  `--no-build`, `Basil.IntegrationTests` last (about six minutes alone), `timeout: 600000` on each.
  Following the earlier version of this rule — "foreground with a 600-second timeout" — still failed,
  because the whole suite does not fit inside it.
* **Never `git add -A` or `git add .`** Stage explicit paths. A blanket add swept another worker's
  unverified files into a documentation commit twice.
* **A resumed tree must be built before its contents are treated as progress.** `git status` cannot
  tell a finished task from a half-rewired one. Two trees once looked identical in `git status`; one
  was green and one did not compile at all.
* **Use the Rider MCP refactorings** — `rename_refactoring`, `move_type_to_namespace`,
  `change_api_signature`, `safe_delete`, `find_references`. They work on the reference index, so they
  also fix `nameof` and `<see cref>` that a text edit leaves silently wrong. They are bound to the
  solution open in the **main tree only**; pass `rootFolder: "V:/Code/cs/osuBasil"` when asked. The
  one behavioural bug found in all of Stage C was in hand-applied work.
* **The worker keeps its own checkpoint, in the same commit as each green step** — not the
  orchestrator afterwards. A worker here dies mid-task roughly every session, and when it does its
  report dies with it. State what is **applied but uncommitted** separately from what is next: a
  checkpoint once listed five already-applied steps as "next", and the successor reviewed a diff it
  believed it was about to write, with a real bug sitting in it.
* **One worker per tree.** Never coordinate manually around overlapping edits — serialise, assign
  ownership, or move the boundary.
* **Model tiers.** Opus for architectural judgement only; Sonnet for implementation; Haiku for pure
  verification. The account is Claude Pro, not Max — budget is a real constraint, and a fixed model
  for every task wastes it.

---

## 6. Tooling worth reaching for, and one that lies

Audited by running the tools, not by reading their descriptions.

**Use:** the Rider refactorings; `cwm-roslyn-navigator get_endpoint_map` (151 endpoints with full
patterns, constraints, file and line) and `find_references` (found an `<inheritdoc cref>` that grep
reads as a comment); `codegraph_explore` instead of a grep-then-read loop; `context7` for NetArchTest
and xunit v3 API questions, which Stage E2 will need; `deepwiki` against `osuAkatsuki/bancho.py` for
scope questions.

**Do not trust:** `cwm-roslyn-navigator get_dependency_graph` **errors** on this solution at both
project and namespace scope, and `detect_circular_dependencies` returns zero cycles where the feature
graph is demonstrably one strongly connected component of ten slices. On a migration whose central
gate is a cycle count, that is the most expensive possible false comfort.

---

## 7. Where I was wrong, so you do not trust this record blindly

* I claimed four services touched the database, from an unanchored grep for `ExecuteAsync` that
  matched every `BackgroundService` override. A worker disproved it; Task B3 closed with no code
  change. The correction is a visible block in `architecture-target-20260908.md` §2.
* I wrote a checkpoint saying B5 still owed an invariant test. It already existed, bounded with a
  five-second `CancellationTokenSource`, committed two commits earlier. I had not checked.
* I reported the `Shared → Features` pinned list as "12 down to 11" twice. The array held 13 and then
  12; I was quoting a comment that had drifted before I arrived. The comment no longer states a count.
* My prediction table for C4 listed `Bot → Scores` as an edge that would disappear. It was
  `using Basil.Domain.Scores` — a Domain namespace, never a slice-boundary violation, never an
  allowlist row.
* I recorded the Domain graph as three edges. It is six; see §4.

The pattern: **every one of these was a number or a claim written without measuring, and every one was
caught by someone measuring it.** Prefer a probe to the record — this document included.

---

## 8. Open items

* **C1, then C5.** C5 gates Stage D: if the graph did not move as predicted, stop and report before
  Stage D, whose project split assumes it did.
* **Task G5** — `BeatmapsetManagementEndpointTests.PutBeatmapset_Valid_ReplacesTheBeatmapsetsFilesAndReturns202`
  passes because a background migration sweep usually finishes in time, not because anything makes it.
  Recorded in `docs/for-developers/testing.md`.
* **Task H2** — the localization rule set the user supplied becomes developer and agent documentation,
  and `CLAUDE.md` splits into `docs/for-agents/`. Deferred by the user to the documentation phase; the
  source is at `C:\Users\haith\Desktop\osuBasil-docs.md`.
* **Five documents under `plans/`** still link to `../docs/for-developers/known-limitations.md`. That
  file was deliberately moved to `plans/known-limitations.md` in `8d3e9060` — "It's just local
  document for implement PR" — so the links are stale, not the file. They are historical records, left
  for the documentation pass.
* **The user owes a force-push** restoring PR #7 to `97e7d56`. Blocked by a hook; the command was
  handed over and only the user can run it.

---

## 9. Conventions that are contracts, not preferences

From `CLAUDE.md`, and each has bitten: bancho packet layouts are wire contracts; user-visible chat
strings live in named production constants (`MpReplies`, `IrcReplies`, `BotReplies`) and changing the
wording is a contract change; `Privilege`, never `Priv`; no pp in gameplay; the response envelope on
the `api.` host; and the multiplayer concurrency model — hold the match lock across the whole
state-transition-and-broadcast sequence, and never introduce a second synchronisation mechanism for
the same state.

Commit messages, code comments and documentation are written in normal English prose, and comments
explain the reason without citing a document or an ADR number as the reason.
