---
name: check-quota-before-agents
description: "Before every batch of agent runs (OpenCode or any metered agent), read the granted quota (opencode.ai/go) and estimate the batch's spend against the remaining 5-hour and weekly windows"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 1fd5d42e-6268-4a38-a752-170e1f11b138
  modified: 2026-10-12T16:11:00.192Z
---

Before dispatching agents, and again before each later batch, check the quota the provider grants (OpenCode Go
publishes per-model limits at https://opencode.ai/go: monthly dollars, 20% per 5 hours, 50% per week), read
current usage (`opencode stats --days 0 --models --cost`), and estimate what the planned batch will cost. Only
start the batch if it fits; otherwise pick another model with headroom, shrink the batch, or fall back.

**Why:** 2026-10-12, while evaluating the code-principles skill, I checked the Go page once at the start, then
ran batch after batch on `claude-haiku-5-5` ($15/month, so ~$3 per 5 h) until the whole Go 5-hour window was
exhausted mid-run ("Go usage limit exceeded"). User: "Khi gọi agent... phải xem xem quota được cấp là bao
nhiêu trước khi dùng".

**How to apply:** per batch, write one line: model, granted 5 h / week budget, used so far, estimated cost of
this batch. Same care for `claude -p` loops (description optimization): estimate the number of calls first and
start with the smallest run that answers the question.
