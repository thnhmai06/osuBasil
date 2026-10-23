---
name: storage-vs-services
description: "User principle (2026-10-01): models only represent; Registry/Repository/Storage only store; all behaviour lives in XService; outer layers may use storage directly."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 9424f3a0-187c-4744-9c0c-1e47cd3acb93
  modified: 2026-10-01T14:14:43.498Z
---

Models (Domain and runtime) are representations only. A model guarantees that **it is valid** (field
values in range, its own text format, pure derived values); a service guarantees that **it is meaningful**
(not tampered, authority, timing, osu! rules, consequences of a change). Registries, repositories and
storages are only places to store: no rules, no events, no timers. Behaviour lives in `XService`
classes ("nơi tập trung xử lý các hành động liên quan đến X", depending on storage). A class named like a
model (`Gateway`) must not hold logic either. Services are only for actions; Infrastructure, hosts and
the bot read and do plain CRUD on storage directly (user correction: "Services chỉ là nơi thực hiện
hành động khác độc lập với việc lưu trữ, còn bên ngoài vẫn có thể truy cập vào lớp lưu trữ bình
thường").

Repository verbs: `Create`, `CreateOrUpdate`, `Get`, `List` (with search/paging), `Delete`; `Save` only for
file storages. Auth concerns belong entirely to `ICredentialRepository` (incl. admin key as `Md5`), user
info to `IUserRepository`; no separate `Registration` service.

**Why:** user found behaviour executed directly on models/registries "gần như ở mọi mặt" and called it a
violation of the philosophy they had set.

**How to apply:** never put operations on runtime models or registries; give them data, lookups and
concurrency scopes only. Plan: `plans/storage-services-split-plan-20261001.md`. Supersedes the
"object emits its own events / acts on itself" parts of [[passive-side-owns-interaction]] and
[[runtime-models-derive-from-their-basis]].
