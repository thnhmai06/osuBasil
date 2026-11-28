# W9: reply catalogs, options binding, BasilBot (D43, D44, roadmap C3)

Source of truth: `plans/review/release-1.0.md` decisions **D43**, **D44**, D20, D4, D26, D32 and roadmap group **C3**
with its rows F-008, F-009, F-010, F-012, F-014, F-013, F-016 (BasilBot half). Read them first, and `AGENTS.md`
(rule 9 "User-visible reply strings", "Architecture", the module graph). W8 is committed (`f1b305c25`): every IRC text
is a constant in `Basil.Host.Irc/Support/IrcReplies.cs`, every Stable text in its reply class, BasilBot replies go
through `Basil.Infrastructure.Bot/Services/ReplyCatalog.cs` reading `Data/Localizations/bot.json`.

## 1. D43: one reply mechanism for every surface

- `Basil.Application.Shared` declares one generic contract: the text of a reply identified by a value of a reply enum,
  filled with named arguments (e.g. `IReplies<TReply> where TReply : struct, Enum` with
  `string Text(TReply reply, object? arguments = null)`). Each surface owns its enum and the default text and argument
  names of each member, declared beside the enum (an attribute or a defaults table; pick one, use it everywhere).
- `Basil.Infrastructure.Shared` implements it once: reads `Data/Localizations/<surface>.json` (keys = enum member
  names, named placeholders, MessageFormat as today), writes the defaults when the file is missing, reloads on change,
  falls back to the default text on a bad entry and logs it. Move what `ReplyCatalog` already does instead of
  rewriting it.
- Registered per surface through DI (an open-generic registration or one line per surface). The hosts take
  `IReplies<IrcReply>` / `IReplies<StableReply>` etc. from DI; BasilBot takes its own.
- Migrate: IRC (`IrcReplies` constants → an enum plus defaults; file `irc.json`), BasilBot (its current `Reply` keys →
  its enum; file `bot.json`; keep the same JSON keys if the enum names can match them, otherwise write a `Data/` layout
  step in `Basil/Configuration/DataLayout.cs` that renames the keys in an operator's existing file, layout 1 → 2).
  Stable texts may stay constants (the osu! client shows few) unless moving them is mechanical; say which you chose.
- No default text changes in this step. The sample files in `Basil/Data/Localizations/` become the default dumps, with
  their schemas.

## 2. D44: options bound by their owner, same keys

Each project binds the options types it defines from its section (`Add<Module>...(IConfiguration)`), keeping every key
path and environment variable name exactly as today; the composition root stops binding types it does not define.
Update `docs/for-developers/migration-status.md`.

## 3. C3: BasilBot

In order: F-008 (D20: an unknown command answers with a pointer to `!help` in a private message and in `#mp_<n>`, never
in a public channel; `!mp timer`, `!mp aborttimer`, `!mp close` answer where the typist is), F-009 (every reply says
what happened), F-010 (one situation, one wording; a `Denied` reply names the role that can act, D4), F-012 (the
creator is told to join `#mp_<n>`), F-014 (`!mp settings` shows what its help promises), F-013 (`!channel list` shows
privilege names and yes/no), F-016 BasilBot half (`!mp help` is one answer, not one message per command).
Where an item needs new or changed wording, write a plain first draft in the defaults; the reviewer runs every changed
text through the humanizer afterwards, so list each changed or new reply (enum member, old text, new text) in the
report.

## Rules for you

Split the wave yourself (1, then 2, then 3) and build after each. Rider MCP (`search_symbol`, `find_usages`,
`rename_refactoring`, `move_type_to_namespace`, `safe_delete`, `get_file_problems`, `build_solution_*`; if
`rename_refactoring` fails, say so and use a careful word-boundary edit). OpenCode Go at `http://localhost:4096` with
cheap flash models for mechanical sub-tasks (turning constants into enum members plus defaults). Build only
(`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no commit, no `git add`, do
not touch `Basil/Data/appsettings.json` (local skip-worktree copy; report any setting change instead). The module graph
of `AGENTS.md` holds: a host never references Infrastructure. A step needing an unwritten decision: skip it, finish the
rest, report.

## Report

Per step: the design chosen and files; the table of changed or new reply texts; skipped items and why; decisions you
took; build result.
