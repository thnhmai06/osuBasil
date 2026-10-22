---
name: memory-first-terms
description: "osuBasil — \"cache\" means only the on-disk Cache/ folder; RAM data is \"memory\" (source of truth); retry only environment DB errors; unique creates check memory then DB"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 31570c86-b298-4502-a9b0-77ebc24d49ab
  modified: 2026-10-13T03:45:52.691Z
---

- "Cache" only ever means the file-system `Cache/` directory of derived, rebuildable files (.osz repacks, previews). Never call runtime RAM data a cache (no `CachedRepository`, no `*Cache` types for live data); it is "memory", the source of truth: everything checks memory first, and only on a miss goes to the DB to verify or fetch into memory. A miss means "not loaded yet", never "does not exist".
- User's inviolable rule (2026-10-13): "RAM là nơi làm việc chính, DB chỉ là nơi cất giữ những gì đã làm" — no operation waits for the DB unless its result comes from the DB. Appends (logins, match events) carry no Domain id: DB assigns it (identity column), fire-and-forget, AppendAsync stores exactly once. No IdAllocator.
- Resident data = only DB records every client uses (settings, general channels, menu banners), loaded at startup, never released. File storages are NOT resident and keep no bytes in memory (FS is the index); on-demand data is released after idle; owned collections (match records, round scores, …) live exactly as long as their owner instance.
- DB writes are single statements per root queue, not batches; transactions only for a real multi-row business operation. Retry only environment errors; anything else is returned/thrown.
- Creating a record with a unique key: if memory already has it, reject without touching the DB; otherwise send to the DB and, if the DB reports it exists, reject without loading the existing record into memory.

**Why:** user corrected the plan on 2026-10-08 (plans/infrastructure-memory-first-plan-20261008.md); "cache" made memory sound like an intermediary.
**How to apply:** naming, XML docs, plans and agent specs for Storage/Infrastructure. Related: [[storage-vs-services]], [[project-infrastructure-rewrite]].
