---
name: docs-after-source
description: "osuBasil during the Application rework: ignore docs/ and XML docs in reviews and changes; they are written after the source stabilizes. Principles go into AGENTS.md, not only memory."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 9424f3a0-187c-4744-9c0c-1e47cd3acb93
  modified: 2026-09-30T09:08:02.045Z
---

While the Application/Domain source is being reworked, do not review, flag, or update `docs/` or
XML documentation. They may already be outdated, and the user writes them once the source is done.

Principles the user states about the design go into `AGENTS.md` (so every agent learns them), not
only into local memory.

**Why:** 2026-09-30, mid-review: "Chưa quan tâm tới docs hay xml docs nữa… docs trong đây có thể đang
outdate" and "bạn cần ghi lại trong AGENTS.md để các agent khác nhìn nhận, rút kinh nghiệm".

**How to apply:** Reviews of Application skip doc findings. When the user corrects a design
principle, update the matching AGENTS.md section as well as memory. See
[[passive-side-owns-interaction]].
