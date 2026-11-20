# Release 1.0: handoff of the working method

This page hands the release work to a new main agent. The previous main agent stays as an advisor. Read it after the
task itself and before doing anything. It is working material, kept in `plans/review/` with the goals
([`release-1.0.md`](release-1.0.md)) and one evaluation file per day (`2026-11-17.md`); none of it belongs in `docs/`.

## What to read, in this order

1. The task: `/tmp/claude-1000/-mnt-shared-Development-osuBasil/b381bf36-dcba-46f6-93f4-dc497a991c59/scratchpad/release-review-prompt.md`.
   It is in a scratchpad that a reboot can clear; if it is gone, ask the owner for it.
2. [`plans/review/release-1.0.md`](release-1.0.md) (goals) and the dated evaluation files next to it: the owner's decisions (D1 to D10), every finding so far, the open questions.
   It is the one record of the work. This page only says how the work is done.
3. `AGENTS.md`, then what the task's "Giai đoạn 0" lists. The project memory
   (`~/.claude/projects/-mnt-shared-Development-osuBasil/memory/`) holds two standing orders about delegated agents.

## Where the work stands

The reading and the interview are finished. The owner changed the plan during the interview, and these changes
override the task text where they differ:

* **Evaluation comes before criteria.** The owner rejected a criteria table written on paper. Each client is tried
  first; the release criteria are written per client from what the trial showed (D10).
* **The order is from the client inwards** (D9): IRC, the HTTP API with Postman, the Cli (tmux or kwin-mcp), a real
  osu! client (kwin-mcp), the documentation (the generated API reference, then `docs/`), the codebase last.
* **The interface is a category of its own**, and Basil must ship what a client needs to start (D10).
* **The release goes straight to `v1.0.0`** with no intermediate tag (D7), and all four open areas are blockers (D5).

| Step | State |
|---|---|
| IRC and BasilBot, raw lines | Done: F-006 to F-017 |
| IRC with Irssi | Started: F-018, F-019 (F-020 withdrawn). WeeChat and Halloy (the owner's graphical client, drive it with kwin-mcp) are not tried |
| HTTP API | Part 1 done: F-021 to F-031, role matrix in `<scratch>/matrix.py`, room commands in `<scratch>/roomcmds.py`. The Postman collection is not run |
| Cli and dashboard, real osu! client, documentation, codebase | Not started |
| Release criteria, roadmap, approval, fixes, rebuilt `docs/` | Not started |

Nothing in `src/` or `tests/` was changed, and nothing was committed. The only new files are the three files of
`plans/review/`, all uncommitted. Findings go into the evaluation file of the day; goals, decisions and progress into
`release-1.0.md`.

## What the owner wants, in the owner's words

The owner wrote, in capitals, that the goal is a user experience that is perfect and smooth, and later that the
agent must pay special attention to the user experience and to **what the server answers**. Judge every action a
user takes on three points and record all three:

1. Did the server answer at all?
2. Does the answer say what really happened?
3. Does the user know what to do next?

A finding quotes the exact text the server sent. "The reply is unclear" is not a finding; the typed line and the
answer are.

The owner confirmed that **every** finding of the IRC category is a blocker, down to inconsistent punctuation. Grade
the other surfaces the same way: propose blocker by default and let the owner lower it.

## How to work with the owner

* Vietnamese in chat, technical terms unchanged. Documentation, code and commits stay in English.
* The owner answers in a few words and often chooses every option. A question must therefore separate real
  alternatives; "select all that apply" teaches little.
* Explain a term the first time it is used. The owner stopped the interview to ask what "Blocker" meant.
* One question at a time, with a guess and its reason, through the question tool. When the owner answers something
  that fits no option ("Như cũ", "Cả 3", "hỏi lại"), restate what was understood before going on.
* Keep chat short and put the detail in the evaluation file of the day. After a silence of a few tool calls, say in one line what is
  being done.
* After each category: write the findings to the file, show the three to five heaviest ones in chat with the server's
  own words, and ask the owner whether the severity is right. Record the answer in the file.
* Changing code, committing or starting an agent that writes code needs the owner's approval of that group of work.
  Evaluation needs none. An OpenCode dispatch needs a question every time.

## The environment of the evaluation

The server under test is the Release build of the working tree, started from the repository root, with its own
database and data directory so that the owner's development database (`basil` on port 5432) is never touched.

| Thing | Where |
|---|---|
| PostgreSQL | container `basil-review-pg`, `127.0.0.1:55432`, database `basil_review`, user and password `basil` |
| Settings | `<scratch>/env.sh`, sourced before the server or a console command |
| Server | tmux session `basil` (`--console 0`), log in `<scratch>/run/server.log` |
| HTTP | `http://127.0.0.1:18443`, with the header `Host: api.basil.local` |
| IRC | `127.0.0.1:16667` |
| Accounts | `alice` (id 2), `bob` (3), `root` (4, administrator), `carol two` (5); password `<name>-pass-1234` with the space removed for carol (`carol-pass-1234`) |
| Tokens and IRC keys | `<scratch>/run/<name>.token`, `<scratch>/run/<name>.key` (tokens last seven days) |
| Raw IRC probe | `<scratch>/irc.py <port> <script>`; a script line is a raw IRC line, `~ 2` waits two seconds while reading |

The review environment moved on 2026-11-18, after a reboot wiped the scratchpad: settings, start script, tokens,
data and logs are in `/home/haith/.cache/basil-review/` (`env.sh`, `run-server.sh`, `run/`), and the server listens on
`https://0.0.0.0:443` (`--console 0`, tmux session `basil`), IRC on 16667. The probe scripts of the first day were in
the scratchpad and are gone; rewrite them there when needed. Docker Desktop is a user service
(`systemctl --user start docker-desktop`) that does not start with the machine.

`<scratch>` is `/tmp/claude-1000/-mnt-shared-Development-osuBasil/bc8fa537-d5aa-4b45-af40-8916e41e2731/scratchpad`.
A reboot clears it and stops the tmux session; the container survives. To rebuild: write `env.sh` again (connection
strings for port 55432, `Basil__Storage__Location`, `Basil__Server__Irc__Port=16667`,
`Basil__Server__Multicast__Enabled=false`, `Basil__Bot__Replies__Path`,
`Kestrel__Endpoints__Https__Url=http://127.0.0.1:18443`, `Logging__File__Location`), create the accounts with
`users create`, start the server, sign in and create the IRC keys with `POST /v1/users/{id}/sessions/irc`.

Stop the container and the tmux session when the evaluation ends, and say so to the owner.

## Lessons of this session

* **Try a real client as well as raw lines.** Raw lines pin the exact wire text. A real client sends commands of its
  own (Irssi sends `MODE <nick> +i` and `MODE #channel b`), and the server's answers to those are what a user sees
  first; three findings came only from Irssi.
* **Pace the commands.** The chat rate limit drops messages after about ten in a burst. Space BasilBot commands by two
  seconds, or a missing reply is the limit and not a defect. A first fast run produced four false "no reply" results.
* **Read to the end before judging silence.** A reply can arrive after the next command was sent. Re-run a silent
  command alone before recording it.
* **Check a suspicion in the code before stating its cause**, and mark the finding "likely" until then. F-011 is
  recorded with an unverified cause on purpose.
* **Never judge from a truncated answer.** F-019 and F-020 were first written from a response cut at 1200 characters;
  F-020 was wrong and withdrawn, F-019 rewritten. Parse the whole answer.
* **Read the client documents of a surface before grading it** (`docs/for-client/`): they settled F-025 and F-029
  and showed that an option put to the owner for F-011 rested on a false premise.
* **Send a valid body when testing a refusal.** An invalid body stops at 422 before the privilege check and proves
  nothing about authorization. A room left empty closes after 15 minutes; create a fresh one before a room pass.
* **Several accounts with different roles.** The private-room leak (F-006) appeared only with a user who held no role
  in the room. Use a creator, a referee, an outsider and an administrator on every surface.
* **Test data left by a failed script changes later results.** A half-run script created a room, and a matrix that
  called `POST /v1/auth/logout` signed its own outsider out; exclude routes that end the caller's session.
* The shell is zsh: an unquoted variable is not split into words (build `curl` headers explicitly), and a bare
  `=====` is expanded. A foreground `sleep` is refused by the harness; `python3 -c 'import time; time.sleep(n)'` works.
* Build and the whole test suite run **once**, as base evidence. The Release build is done (0 errors, 145 warnings,
  not looked at yet). The test suite has not been run.
* Browse with the `/browse` skill of gstack, never the Chrome tools. gstack's artifact sync is off by the owner's
  choice; do not turn it on.

## Delegating the costly trials

The owner asked (2026-11-17) that expensive hands-on work (driving the osu! client through kwin-mcp, long scripted
runs) go to Sonnet or Haiku subagents, while the main agent only collects their raw or recognised results and does the
analysis. Write the recipe to a file in the scratchpad (environment, exact steps, what to record, a time box, "change
no file of the repository"), start the agent with a short prompt that points to it, and read its report file. The
first such recipe is `<scratch>/osu-client-recipe.md`; its report is `<scratch>/osu-client-report.md`. A finding still
needs the main agent's own check of the evidence before it goes into the evaluation file.

## The advisor

The previous main agent holds the context of the interview and of the IRC trial. Ask it before:

* a checkpoint with the owner, to check that each finding is supported by its evidence and graded like the earlier ones;
* a proposal that touches a frozen surface (D2) or a rule of `AGENTS.md`;
* writing a recipe for an implementer, and before accepting an implementer's diff.

The advisor gives an opinion and the owner decides. Do not wait for the advisor on evaluation work that needs no
decision.
