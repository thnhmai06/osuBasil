---
name: plan-means-no-code
description: "When the user asks for a plan (viết kế hoạch / chưa triển khai), write only the plan; never start code until they say go, and implementation goes through OpenCode first"
metadata:
  node_type: memory
  type: feedback
  originSessionId: b978cba2-cc34-44cd-be1a-bcf5af19573d
  modified: 2026-10-07T17:09:52.499Z
---

A request to "viết kế hoạch" (write a plan) or anything marked "chưa triển khai" means plan only: no code edits, even
after the user answers the plan's questions — answering policy questions is approval of policies, not a go-ahead to
code. When implementation is approved, delegate to OpenCode agents first, then Sonnet/Haiku subagents, and only then
write code myself ([[delegation-chain]]).

**Why:** on 2026-10-08 I started writing the PostgreSQL storage code myself right after the user answered the plan's
questions; the user stopped me ("VUI LÒNG CHỈ VIẾT PLAN, CHƯA TRIỂN KHAI CODE") and reminded me OpenCode comes first.
**How to apply:** after a plan's questions are answered, update the plan and stop; wait for an explicit "triển khai".
Even small scaffolding (schema, packages) counts as code. Environment setup the user explicitly asked for (creating a
database role) is fine.
