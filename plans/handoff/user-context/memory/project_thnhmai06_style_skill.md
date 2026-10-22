---
name: thnhmai06-style-skill
description: "Cross-project skill thnhmai06-style drafted 2026-10-12 at skills/thnhmai06-style (skills.sh layout, not installed yet); interview decisions that shaped it"
metadata:
  node_type: memory
  type: project
  originSessionId: 1fd5d42e-6268-4a38-a752-170e1f11b138
  modified: 2026-10-12T16:11:14.676Z
---

The skill is now named `code-principles` and lives in its own repo `V:\Code\md\code-principles`
(GitHub thnhmai06/code-principles): `skills/code-principles/SKILL.md` + references (organization, architecture,
design, memory, csharp-style, docs, testing, workflow, microsoft-docs), and `evals/` (evals.json + inputs). It
was moved out of osuBasil on 2026-10-12. skills.sh layout, so `npx skills add thnhmai06/code-principles`.

Interview decisions (2026-10-12):
- One skill with references, not several skills. English. Principles language-neutral, syntax detail C# only.
- `?:` puts the normal case in the `?` branch (older code that puts the null case first is legacy).
- Errors follow Microsoft's Framework Design Guidelines: exceptions for execution failures, Try pattern or an
  outcome type (RoomResult-style) for common expected failures. The user first said "exceptions first", then
  reverted to this.
- For design questions, Microsoft's official guidance comes first among external sources.
- `ConfigureAwait(false)` on every await in library (non-host) code.
- Formatting is done by the IDE (Rider cleanup); repos without .editorconfig follow Microsoft defaults.
- Commit conventions are deliberately not in the skill.
- Records for passed data, mutable classes with validating setters for live entities, sealed services.
- Small types in their owner's file, class names at most three words, `#region` for sections, plural folder
  when the singular collides with a type, tests named Method_Scenario_Expected.
- Evals run through OpenCode (the Go server on :4096 was down; `--standalone` with the run dir as cwd works).
  Run them one at a time: parallel runs and the viewer were killed once for low RAM.

Round-2 decisions (2026-10-12): `IXService` contract only across a module boundary; a value computed from an
object's own data is its derived property (`Order.IsBig`, `Order.Discount`) and a stateless service becomes a
`static class`; the last branch of an `if` chain becomes an `if` only when it is the same kind of decision;
`ConfigureAwait(false)` on every await in library code, `await using`/`await foreach` included (user chose this
after learning CA2007 reports both; verified with the .NET 10 SDK); lock per
object; text storages expose StreamReader/StreamWriter. The user asked to cite Microsoft docs:
`references/microsoft-docs.md` indexes them and each reference file cites its sources (44 links, all checked 200).
Text rule: a fixed business format (`{name} {age} tuổi`) may be a model property; natural-language messages
("An error occurred…") never live on models, they belong to the surface/localization. The user also asked for
Basil's layer + event pattern, now `references/architecture.md` (layer map, event pipeline, skeleton);
`data-and-resources.md` was renamed `memory.md` at the user's request.

Round-4 decisions: a milestone the operation reaches (a move that ends the game) may be a second event right
after the first (`GameMovePlayed`, then `GameEnded`); inside the system operations take objects, but members
facing the outside (HTTP, remote API) may take ids and resolve them first.

**Standalone rule (user, 2026-10-12):** the skill must not reference any of the user's projects (no Basil, osu!,
SlideGenerator, beatmaps, MpReplies, `~/.claude` paths…). All examples use one neutral domain, an online meeting
platform (Meeting, Room, Participant, Connection, ChatChannel, Recording, Billing) plus Orders/Payment/Shipment
for organization examples. Keep it that way when editing.

**Why:** user asked to build a reusable style skill from AGENTS.md, commits and an interview.
**How to apply:** when the user reports a new convention, propose adding it to the skill; related
[[code-style-user-edits]], [[storage-vs-services]], [[setters-and-naming]].
