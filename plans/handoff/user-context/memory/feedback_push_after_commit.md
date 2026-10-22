---
name: push-after-commit
description: "osuBasil: push to origin right after every commit (user, 2026-10-03)"
metadata:
  node_type: memory
  type: feedback
  originSessionId: a5c6af71-b5b0-44e3-bdd8-0b8defbe36c1
  modified: 2026-10-03T12:14:20.669Z
---

After every commit in osuBasil, push the branch to `origin` immediately; no need to ask.

**Why:** user said "Push lên mỗi khi commit" (2026-10-03) after being asked about pushing plan commits.

**How to apply:** `git push origin <branch>` right after each `git commit`; report the pushed range.
Force-push and pushes to `main` still need explicit confirmation.
