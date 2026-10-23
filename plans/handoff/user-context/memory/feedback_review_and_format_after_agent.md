---
name: feedback-review-and-format-after-agent
description: "After every subagent finishes: line-by-line quality review (not just green build), then Rider reformat + cleanup of the touched files"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 8cd03193-4520-4c7f-bd41-bf52f68a6113
  modified: 2026-10-19T16:10:18.320Z
---

After each subagent (OpenCode, Sonnet, Haiku) finishes, review its whole diff by hand against AGENTS.md and the old behaviour (guards, invariants, events kept), and run Rider `reformat_file` and cleanup (`lint_files`/`post_edit_quality_check`) on every touched file. A green build is only the gate. Look at the working tree first: the user edits files between turns (2026-10-19 they renamed `Rights` to `Permissions`, `RoomPermissionsGroups.Of` to the extension `GetPermissions`, `All` to a `const`).

**Why:** user wants "mọi thứ hoàn hảo"; agents left unused locals, noisy comments, dropped guards and block-scoped namespaces.
**How to apply:** put the Rider reformat/cleanup step in the finish checklist of every delegated task, and diff against `git diff -w --ignore-cr-at-eol` to ignore Rider's CRLF rewrites. Related: [[feedback-review-quality-not-build]].
