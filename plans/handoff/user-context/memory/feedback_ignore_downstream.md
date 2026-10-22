---
name: ignore-downstream
description: During the Application rework, ignore tests and downstream projects (Infrastructure, hosts) entirely — treat them as deleted
metadata:
  type: feedback
---

User (2026-10-04): "Ko quan tâm tới test, downstream — cứ kệ nó, như là đã bị xóa rồi." Do not edit, fix, add or
run anything under tests/, Infrastructure or the hosts while reworking Application/Domain/Protocol; verification
is building the projects being reworked (Domain, Application.*, Protocol.*). Record what downstream will need in
the plan instead.

**Why:** downstream will be rewritten against the new contracts later; effort on it now is wasted.

**How to apply:** strip test/downstream items from agent specs; never "fix" stale downstream code. See [[storage-services-split]].
