# W3: the `Data/` layout, its migrations, logs

Source of truth: `plans/review/release-1.0.md` decisions D21 (log part only: none), D24, D26 (the parts D29 keeps),
D29, D32, D36, D38 and findings F-052, F-096, F-112, F-118, F-119, F-036, F-037. Read those rows first, and
`AGENTS.md`. W1 (Basil behind nginx) is already in the tree: build on it.

## Goal

One volume, `Data/`, holds only what a backup needs besides the database. Everything rebuilt on demand or transient sits
beside it. Shipped files are written only when missing. `Data/metadata.json` versions the layout; the program refuses
to start on a layout or a database schema newer than it knows. Logs reach `docker logs`.

## Target layout (install root = `AppContext.BaseDirectory`; in the container `/app`)

```text
/app/Basil ...            the program
/app/Data/                the one volume (path frozen at 1.0, D38)
    appsettings.json, appsettings.schema.json, tuisettings.json, metadata.json
    Localizations/        bot.json (+ schema) ...
    Beatmapsets/ Replays/ Avatars/ Menu/ Faqs/ Screenshots/ Rulesets/
/app/Cache/               Covers/, Attributes/, audio previews, anything rebuilt on demand
/app/Logs/                system log, Sessions/
/app/Temp/                basil.sock
```

`Spool/` and `Quarantine/` stay where they are for now (W4 removes them); move them under `Temp/` only if that is a
one-line change, otherwise leave them.

## Out of scope

Uploads (W4), the calculation stack and `RulesetStore` beyond the attributes path (W5), IRC reply texts (W8), any
reply wording change, tests.

## Steps

1. **`Configurations/` merged into `Data/` flat (F-052).** `ConfigurationSetup.Directory` becomes `Data/`
   (`appsettings.json`, `appsettings.{Environment}.json`); the schema and `tuisettings.json` move with it (find every
   path to `Configurations`: `HostApplication.AddCli`, `ShellRunner`, `ConfigurationSetup`, the csproj `Content`
   items, docs). The source folder in the repository becomes `Basil/Data/` (move `Basil/Configurations/*` there with
   `git mv`). Remove `ConfigurationSetup.Templates`.
2. **Shipped files written only when missing (D26).** At start, before configuration is read, every shipped file of
   `Data/` (appsettings and schema, `Localizations/*`, the default menu images) is copied from a read-only shipped copy
   in the image (e.g. `/app/Defaults/Data/`, produced by the publish) into `Data/` only if the target does not exist.
   Never overwrite an operator's file. One small class in the composition root.
3. **`Data/` persistent only (D26, D32, F-112).** `DataPaths` in `Basil.Infrastructure.Shared/Storage/DataPaths.cs`
   gets the install root and exposes `Data` storages plus `Cache`, `Logs`, `Temp` roots beside it (not inside). Covers:
   `Basil.Infrastructure.Beatmaps/Storage/BeatmapsetCovers.cs` writes to `Cache/Covers/<set id>/`; deleting a set
   deletes its covers folder too; the cover route still works (and its `X-Accel` path from W1 points at the cache
   location: nginx needs a second `internal` location over a read-only `Cache/` mount, add it). Attributes:
   `Basil.Infrastructure.Rulesets/Storage/AttributeStorage.cs` moves from `Rulesets/Attributes` to `Cache/Attributes`.
   Control socket (`ControlSocket.PathIn`) to `Temp/`. Logs default `Location` to the `Logs/` root.
4. **`Data/metadata.json` (F-096).** Format `{ "layout": 1 }`. At start, first thing after step 2: read it; missing on a
   fresh `Data/` → write `1`; missing on a `Data/` that already holds files from before → treat as layout 0 and run the
   step 0→1 (moves this wave makes: covers out of set folders into `Cache/Covers` may simply be deleted since they are
   rebuilt; `Rulesets/Attributes` deleted; old `Configurations/` content is not in the volume, nothing to do). A newer
   layout than the program knows → refuse to start with one plain sentence. A failed step → stop with the step's name,
   leave `metadata.json` at the old version. Steps are a small ordered list in code, each idempotent.
5. **Database newer than the program (F-119).** In `Basil.Infrastructure.Shared/Persistence/DatabaseMigrator.cs`,
   before migrating, for each module ask EF for applied migrations (`GetAppliedMigrationsAsync`) and known ones
   (`GetMigrations`); any applied one unknown → refuse to start naming the module and the migration. Through EF API
   only, no SQL.
6. **BasilBot replies from `Data/Localizations/` (D26, D32).** `ReplyCatalog` (`Basil.Infrastructure.Bot/Services/`)
   reads `Data/Localizations/bot.json`; `RepliesOptions.Path` and the `Basil:Bot:Replies` binding go; the watcher
   watches the new file. The file's shape is what `ReplyCatalog` already reads and writes (keys = `Reply.Key`, named
   placeholders matching `Reply.Arguments`, MessageFormat); the existing `Basil/Data/Localizations/{bot,mp}.json`
   samples use `{0}` and a nested shape that nothing reads: replace `bot.json` with the catalog's own default dump
   (what `WriteDefaults` writes) and its schema; fold `mp.json` into it if `!mp` replies are part of the same catalog,
   otherwise leave `mp.json`; leave `irc.json` for W8. Do not change any default wording.
7. **Logs (F-036, F-037, F-118, D24).** Shipped levels: `Default` `Information`, `Microsoft` and `System` `Warning`,
   `Microsoft.Hosting.Lifetime` `Information`. The system log also goes to standard output (Serilog console sink, plain
   text, no colour when output is redirected) when the console dashboard is not drawing on it (check `CliModes`).
   Session logs: one file per connection session (osu!, IRC, API) under `Logs/Sessions/`, holding that session's
   lifecycle and refusals only (not routine traffic), kept 7 days; find where `ServerLogging` and the session
   lifecycle log today and route by a session property (Serilog `Map` sink or equivalent). Settle the smallest design;
   report it.
8. **Compose and Dockerfile.** One `Data` volume at `/app/Data`; `Cache/`, `Logs/` may be mounted (Logs and Cache
   read-only into nginx where it serves files); remove the `Templates` and `appsettings.json` mounts. Data and Cache
   group-readable for nginx.
9. **Left over from W1.** `Basil.Host.Shared/Http/StoredFile.cs` hard-codes `AppContext.BaseDirectory/Data` as the
   X-Accel root: make it hand off any file under `Data/` or `Cache/` with a prefix per root (`/_files/` for `Data/`,
   `/_cache/` for `Cache/`; one setting each or one prefix with the root name, your choice, keep it minimal) and add the
   matching `internal` location and read-only `Cache` mount to `docker/nginx/nginx.conf` and compose. Remove
   `Basil__Bot__Replies__Path` from `.github/workflows/ci.yml`. `Basil/Configurations/appsettings.json` has the git
   skip-worktree flag (the owner keeps local log levels): `git mv` it as part of step 1, and report it so the reviewer
   restores the flag on the new path.
10. **Docs.** `docs/for-technicians/configuration.md` (layout table, metadata, refuse-to-start rules, backup before
   `docker compose pull`: `pg_dump` and the `Data/` volume), `docker.md`, `logging.md` (both), `AGENTS.md` lines that
   name `Configurations/Templates/replies.json` or `Configurations/`.

## Rules for you

Same as W1: Rider MCP (`search_symbol`, `find_usages`, `rename_refactoring`, `safe_delete`, `get_file_problems`,
`build_solution_*`), OpenCode Go at `http://localhost:4096` with cheap flash models for mechanical edits only, build
only (`dotnet build Basil.slnx -c Release --nologo -v q`), no tests, no commit, no `git add` except `git mv`. AGENTS.md
code rules apply. A step needing an unwritten decision: skip it, finish the rest, report.

## Report

Per step the files changed; skipped steps and why; decisions you took; build result.
