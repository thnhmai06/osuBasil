---
name: delegation-chain
description: "Implementation goes OpenCode first, then Claude Sonnet/Haiku subagents, then myself — automatic, no asking; rules live in global ~/.claude/CLAUDE.md"
metadata:
  node_type: memory
  type: feedback
  originSessionId: fdae2673-0224-44ed-93fc-5a0d73896330
  modified: 2026-10-07T18:00:53.413Z
---

The delegation rules (OpenCode Go server on `0.0.0.0:4096`, few-quota models sized to the task, automatic
fallback OpenCode → Sonnet/Haiku subagents → myself, maximal use of MCP/Skills/Plugins incl. Rider MCP) now
live in the global `~/.claude/CLAUDE.md`, because the user wants them in every project, not only osuBasil.
This memory only records the osuBasil specifics.

**Why:** user, 2026-10-08: "điều phối và giám sát chất lượng qua OpenCode Go 0.0.0.0:4096 (dùng model ít
quota và chọn vừa phải thôi) và fallback sang Claude subagents -> bạn làm… nhớ điều này xuyên suốt các
phiên của tôi sau này, không chỉ cho osuBasil" and "Tận dụng tối đa, triệt để MCP, Skills, Plugins".

**How to apply in osuBasil:** write the model/effort per task into the plan's phase table; prompts carry the
full spec per [[opencode-implements-only]] and the tool rules of [[opencode-use-rider-mcp]]; review every
result per [[review-quality-not-build]]; run agents that build the same project one after another.

**Free-model practice (2026-10-13, Go quota exhausted for the month):** use `opencode/*-free` only. Prefix prompts
with "START EDITING IMMEDIATELY… do not explore beyond the files named, do not check line endings" (otherwise
`mimo-v2.6-flash-free` spends 10 min exploring). `longcat-2.5-preview-free` follows specs well but hits
"Rate limit exceeded" mid-run; then switch to mimo or finish the last fixes myself. Launch runs detached
(`nohup sh -c "opencode run … ; echo $? > x.done" &` + Monitor on the done file) — Claude Code kills tracked
background shells when RAM is low.
