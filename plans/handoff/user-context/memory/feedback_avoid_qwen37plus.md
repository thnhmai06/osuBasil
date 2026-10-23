---
name: feedback-avoid-qwen37plus
description: User finds opencode-go/qwen3.7-plus too costly; default to cheaper models for delegated work
metadata:
  type: feedback
---

On 2026-10-19 the user said qwen3.7-plus is too expensive ("chát"). It cost $1.39 of $1.62 spent in one day.

**Why:** OpenCode Go quota is tight and shared with the user's other work.
**Also (user, same day):** "Chú ý chọn model phù hợp với độ phức tạp công việc" - match the model to the task: cheap flash models for mechanical renames and exact-spec ports, a mid model (luna, minimax) for ports that move rules between objects, a strong model only for design-sensitive work; never one model for everything.
**How to apply:** use `opencode-go/mimo-v2.6-flash` or `deepseek-v4.1-flash` for spec-driven ports (specs here carry exact shapes), `gpt-6-luna` or `minimax-m3` when a task needs judgement; avoid qwen3.7-plus unless a cheaper model failed. Related: [[feedback-check-quota-before-agents]].
