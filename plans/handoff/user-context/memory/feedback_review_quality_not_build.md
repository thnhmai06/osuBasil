---
name: review-quality-not-build
description: "Accepting agent work needs a line-by-line quality review of behaviour, races, edge cases and rule compliance — build + grep passing is not \"đạt\""
metadata:
  node_type: memory
  type: feedback
  originSessionId: 9424f3a0-187c-4744-9c0c-1e47cd3acb93
  modified: 2026-09-30T23:47:26.296Z
---

Before calling any delegated (or own) change "đạt", read every changed line: behaviour against the
spec and osu!/bancho semantics, concurrency (locks, check-then-act), edge cases (empty, closed,
re-entry, idempotency), event payloads, AGENTS.md rules, naming, docs. Build and grep are only the
entry gate. Report concrete findings, not "build xanh, grep rỗng".

**Why:** user (2026-10-01): "Các mục đạt hay không, bạn phải kiểm tra kĩ các chi tiết, tránh việc lấy
kết quả là 'chạy được' mà ko quan tâm đến chất lượng." Earlier phases were accepted mostly on
build/grep plus a skim.

**How to apply:** for each review, list what was checked and what was found; re-review already
accepted work when quality was not checked in depth. Related: [[opencode-implements-only]].
