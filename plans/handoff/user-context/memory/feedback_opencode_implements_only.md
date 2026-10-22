---
name: opencode-implements-only
description: "When delegating to OpenCode, Claude owns all design; OpenCode only types in the code"
metadata:
  node_type: memory
  type: feedback
  originSessionId: aa847801-527a-476c-b37e-b4372479a1fc
  modified: 2026-09-30T01:57:54.903Z
---

When delegating to OpenCode, Claude does the design: exact type and member signatures, file
locations, XML doc text, and control flow. OpenCode only turns that spec into code and fixes call
sites mechanically. Prompts must not leave choices open ("same file or X.cs", "update the summary to
describe…").

**Why:** the user said "Bạn thiết kế, còn opencode chỉ code triển khai" while the identity-wrapper plan
was being executed (2026-09-30).

**How to apply:** put the full designed code (signatures and docs) in each OpenCode prompt. Review
the diff yourself afterwards and fix any design drift. Related: [[runtime-models-derive-from-basis]]
