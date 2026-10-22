---
name: opencode-use-rider-mcp
description: "OpenCode agent prompts must tell agents to use the rider MCP, codegraph and .NET skills instead of hand edits"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 9424f3a0-187c-4744-9c0c-1e47cd3acb93
  modified: 2026-10-13T15:09:13.072Z
---

Every prompt sent to an OpenCode agent tells it to use its tools: `codegraph_explore` to locate code,
`rider` MCP for refactors (`rename_refactoring`, `safe_delete`, `change_api_signature`,
`get_file_problems`, `reformat_file`/`post_edit_quality_check`), and relevant .NET skills; hand
edits only where Rider cannot do it.

**Why:** user asked (2026-10-01) "Bảo nó dùng Rider mcp skills các thứ" — OpenCode has `rider` and
`codegraph` MCP connected; semantic refactors are safer than text edits.

**How to apply:** include the tools section (plan "Điều phối OpenCode", item 6) in every prompt.
Word it as a requirement, never "when available": callers via `analyze_calls`/`search_symbol` (not Grep),
`extract_interface` for new contracts, `get_file_problems` after each file; fallback only if Rider does not
answer, reported. (2026-10-13: a soft "when it is available" let an agent use Grep; user asked "Không bảo nó
dùng Rider à?".)
Related: [[opencode-implements-only]].

**Fallback (2026-10-01):** if OpenCode Go quota runs out and free/GitHub Copilot/other OpenCode
providers cannot replace it, switch on my own to Claude Code Sonnet/Haiku subagents with the same
prompt and review — no need to ask first.
