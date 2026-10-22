---
name: assets-and-oop
description: "osuBasil — assets referred to by Domain AssetReference (record struct, IParsable/IFormattable); storages keep CRUD, CDN only distributes; code must be truly object-oriented"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 52cf2e94-6f3f-4a93-9a3a-f7d0f4cb46b2
  modified: 2026-10-13T03:45:58.959Z
---

- Asset references are `Basil.Domain.Content.AssetReference` (readonly record struct, only state `Value`, IParsable/IFormattable, explicit conversions), never `string`/`Uri`. Local = relative path under `Data/` (portable across machines); external = http/https kept as-is, never cloned. No `file://`. Normalization is Domain business.
- File storages keep basic CRUD incl. List (caller must know what exists); `OpenAsync` is internal use only. The host CDN (ImageSharp.Web PhysicalFileSystemProvider, fixed URL pattern per asset folder, cache invalidated by LastWriteTime) only distributes, never manages or lists. `DataStorage` is `internal` in Infrastructure; hosts go through the storage interfaces.
- Replay file name = osu! export name `{ScoreId} {Player} - {Artist} - {Title} [{Version}] ({yyyy-MM-dd}) {Mode}.osr`; screenshot = UUID.
- "Hướng đối tượng thực sự": operations go through the object owning the state, not static procedural helpers; collection types implement BCL interfaces; plural folder when it would collide with a type name.

**Why:** user corrections during the 2026-10-13 storage.todo plan (several rounds: file:// rejected, UriExtensions belongs to Infrastructure then normalization to Domain, AssetFiles/AssetLinks removed).
**How to apply:** any storage/asset/host-serving work and agent specs. Related: [[memory-first-terms]], [[contract-naming-neutral]], [[user-code-style]].
