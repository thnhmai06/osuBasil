# W11: console and dashboard (roadmap E7, plus F-034, F-035)

Source of truth: `plans/review/release-1.0.md`, roadmap group **E7** and its rows F-071, F-072, F-049, F-050, F-074,
F-075, F-053, F-076, F-073, F-046, F-047, F-051, F-048, F-038, F-103, plus F-034 (E1.4) and F-035 (E2.9). Read every row
first, `docs/for-technicians/console.md` and the dashboard code in `Basil.Host.Cli/`. Owner decisions that bind:
the dashboard runs in the container through `tty` and `docker compose attach` (D24, owner 2026-11-24); `restart` is
gone (W1); one-off commands run through `docker compose exec basil ./Basil <command>`. W10 is committed (`8f2885182`);
it removed `DashboardModel.AddOutput` as unused, so the output list in `DashboardModel` is never filled: either wire it
to what it was meant to show or remove it with its view, and say which.

## Items, in order

1. F-071: a dialog left open never blocks the dashboard (data refresh, shutdown, other keys).
2. F-072: the `shutdown` confirmation is shown and answered.
3. F-049, F-050: Ctrl+C does what `console.md` says; the terminal is restored on any exit (normal, error, signal).
4. F-103: leaving an attached dashboard without stopping the server (Ctrl+P Ctrl+Q) is shown on screen (status or
   help line) and in `docs/for-technicians/docker.md`; Ctrl+C in an attached dashboard must not stop the server by
   surprise: follow what F-103 says and ask for confirmation if the row allows it.
5. F-074: keyboard use as documented (every documented key works; undocumented keys documented or removed).
6. F-075: toasts name the account and the change; file errors in plain words; password rules enforced in the dialog
   the same way the server enforces them.
7. F-053: names instead of numbers for enums and flag sets in commands and the dashboard (W10 made the API speak
   names; the console's `--privileges` and `--types` take and show names, with completion).
8. F-076: lists fill their frame and count right (the "show 1–6 in 9 with seven rows" footer).
9. F-073: `--ascii` draws ASCII only.
10. F-046, F-047: the effective configuration with its source (file, environment, default); sizes and durations in the
    console's units.
11. F-051: the dashboard preferences in Settings, saved in `Data/tuisettings.json` (W3 moved it to `Data/`).
12. F-048: empty tabs say how to fill them.
13. F-038: the smaller operator findings listed in the row (modes text, the IRC listening line at start, `users <name>`,
    width, the Sessions tab group names, error prefix).
14. F-034: an unknown shell argument is refused before anything starts; `--help` prints help and exits 0.
15. F-035: when a one-off command cannot reach the control socket, it says so in one plain sentence (where the server
    should be, how to start it).

## Rules for you

Read the `tui-design` skill (Skill tool) before item 1 and follow it for layout and key handling. Rider MCP
(`search_symbol`, `find_usages`, `rename_refactoring`, `safe_delete`, `get_file_problems`, `build_solution_*`); beware
the Rider post-edit hook that may reformat whole files: if a file shows hundreds of unrelated changed lines, restore it
from HEAD and reapply only your change. OpenCode Go at `http://localhost:4096` with cheap flash models for mechanical
sub-tasks. Build only (`dotnet build Basil.slnx -c Release --nologo -v q`), no tests (deferred; ignore `tests/`), no
commit, no `git add`, do not touch `Basil/Data/appsettings.json`. New user-visible texts: plain first drafts in the
console's reply class, listed in the report. An item needing an unwritten decision: skip it, finish the rest, report.

## Report

Per item: what changed and the files; new or changed texts; skipped items and why; decisions you took; build result.
