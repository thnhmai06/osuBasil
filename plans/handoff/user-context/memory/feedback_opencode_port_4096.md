---
name: opencode-port-4096
description: "Dispatch OpenCode agents through the server on port 4096, not --standalone"
metadata:
  node_type: memory
  type: feedback
  originSessionId: a535ad81-bf36-4f41-8ab0-79ee8fc3a560
  modified: 2026-10-21T10:33:59.323Z
---

Run OpenCode with `opencode run --server http://127.0.0.1:4096 -m opencode/<model>-free --auto --title <t> "<prompt>"`. The server on 0.0.0.0:4096 was verified on 2026-10-21 to run in V:\Code\cs\osuBasil.

**Why:** the user said "OpenCode ở cổng 4096" when I started agents with `--standalone`.

**How to apply:** use `--server http://127.0.0.1:4096` for every dispatch; `--standalone` only if the server is down. Write long task prompts to a file with the Write tool (heredocs with apostrophes break the Bash tool) and pass `"$(cat file)"`. See [[delegation-chain]].

**2026-10-21 update:** the user moved OpenCode Go to another Go account. Paid `opencode-go/*` models are limit-blocked, but `opencode/*-free` models work again; the free tier still returns "Rate limit exceeded" in bursts, so retry another free model or fall back to a Sonnet subagent.
