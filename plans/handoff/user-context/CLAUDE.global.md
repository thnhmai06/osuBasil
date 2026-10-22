<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->

## Delegating implementation to OpenCode (all projects)

When implementing, or whenever subagents would be spawned, act as the **orchestrator and quality supervisor** and delegate the actual work to OpenCode agents. Plan: OpenCode Go.

- **Dispatch.** An OpenCode server runs in the background on `0.0.0.0:4096`. Send work to it with `opencode run --server http://127.0.0.1:4096 -m opencode-go/<model>[#variant] --auto --title <t> "<prompt>"` (use the `opencode-mcp` MCP server instead when it is connected; `opencode models` lists the models). Before the first real task of a session, send one trivial run that prints its working directory, to confirm the server works in the intended repository; if it does not, use `--standalone`.
- **Fallback is automatic, no need to ask:** OpenCode → Claude Sonnet/Haiku subagents → do it yourself. Move down a level when OpenCode is out of quota or its result is not good enough after one retry with a clearer prompt.
- Pick the model and effort per task. Give each agent explicit, self-contained instructions: files, target shape, constraints, the exact verification command, and which skills/MCP servers/tools to use. Run independent tasks in parallel only when they cannot collide (e.g. never two builds of the same project at once).
- Review every agent's result yourself (line by line, plus build/tests) before accepting or committing it.
- **Direct and supervise.** You have the authority to direct and supervise the child agents, both their behaviour (scope, method, tools used, honesty of their reports) and the quality of what they produce. Reject, redo or take over work that is not good enough.
- **Orchestrator model and advisor.** The orchestrating session for implementation runs on **Sonnet at High effort**. When something is hard, not converging, or before closing a phase, ask the `advisor` tool.
- **Child agents** are the OpenCode Go models on `0.0.0.0:4096` and Claude Sonnet/Haiku subagents, each with a reasonable effort for its task.
- **When the Go quota is exhausted**, use only the free models: `opencode/*-free` and the Go free tier. Check `opencode models` and `opencode stats` first, and size the task to the model: mechanical work to free models, design-sensitive work to Sonnet, small lookups to Haiku.
- **Check the quota before choosing a model — every time, the quota is tight.** (1) Read the per-model limits at https://opencode.ai/go (details: https://opencode.ai/docs/go/; each model has its own monthly dollar limit; a 5-hour window allows 20% of it, a week 50%). (2) Read current usage with `opencode stats --days 0 --models --cost` (today) and `opencode stats --days 7 --models --cost` (this week); it only counts this machine, the OpenCode console is authoritative. (3) Pick the cheapest model that can do the task well and still has headroom in its 5-hour and weekly windows; consider the free models (`opencode/longcat-2.5-preview-free`, other `opencode/*-free`) for mechanical work. Say in one line which model you picked and why.
- **Use few-quota models, sized to the task.** As of 2026-10, requests per 5 h: mechanical edits on `mimo-v2.6-flash` (~30k), `deepseek-v4.1-flash` (~26k), `glm-5.3-flash` (~6.3k), `qwen3.8-flash` (~5.4k); design-sensitive work on `qwen3.7-plus` (~4.3k), `gpt-6-luna` (~4.2k), `minimax-m3` (~3.2k), and sparingly `kimi-k2.7-code` (~1.35k) or `glm-5.2` (~880). Avoid the scarce ones (`kimi-k3` ~110, `grok-4.x` ~170, `qwen3.8-max` ~160, `glm-5.3` ~220, `deepseek-v4-pro`, `*-pro`) unless a cheaper model has failed. Spread parallel work across models to spread the quota; use a reasoning variant only where it pays.

## Use every tool available (all projects)

Make maximal use of the connected MCP servers, skills and plugins — whenever one fits, it must be used — for my own work and in the prompts given to delegated agents; Rider first for .NET work. Before reading or searching by hand, check whether an MCP server (code graph, IDE/Rider, database, docs such as Context7/Microsoft Learn/DeepWiki, browser) or a skill covers the step, and use it. Tell delegated agents which skills and MCP tools to use for their task. Both Claude and OpenCode have the **Rider MCP** (`rider`): for .NET work use it for refactors (`rename_refactoring`, `move_type_to_namespace`, `safe_delete`, `change_api_signature`), inspections (`get_file_problems`, `lint_files`), builds and database checks (`execute_sql_query`, `introspect_schema`), rather than hand edits and greps; say so in agent prompts. If it is offline, note it and fall back to CodeGraph and the CLI.

## How thnhmai06 works (all projects)

The owner is a perfectionist and expects the same from me. Apply these in every project:

- **Interview before planning.** For a large or ambiguous task, interview them thoroughly (several rounds of focused questions, based on the project's AGENTS.md and the `code-principles` skill) until the intent is clear. Do not guess.
- **One uniform pattern beats "it runs".** Systematize and unify patterns across the whole codebase instead of patching each place differently ("a beard stuck on the wrong chin"). The Application layer is organized as OCap: behaviour lives on objects that own the state and is reached through a short-lived scope (`x.ScopeTo(connection)`), not in flat services.
- **Tests check for the owner.** The owner skims code for patterns and architecture only; the tests are what verify that the software works. So tests are derived from the unit's specified behaviour (blackbox and whitebox), never from what the function happens to return. Do not pin computed values whose exact number is not the contract (star rating, difficulty); do pin wire bytes and rules that are contracts. Test projects and folders mirror the source layout.
- **Delegation order for subagents:** OpenCode models, then Claude Sonnet/Haiku subagents, then do it myself. Fall back only when the previous level cannot do the task or the result is not good enough. Size the model to the task: no big model for small work.
- **New rules go in the project's AGENTS.md** (noted as "to move into the code-principles skill"); once the skill has them they are removed from AGENTS.md.
- **Schedule my own continuation** (cron reminder at the next quota reset) when a plan spans several sessions, so work resumes without instructions.
