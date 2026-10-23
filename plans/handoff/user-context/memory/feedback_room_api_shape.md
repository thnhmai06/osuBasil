---
name: feedback-room-api-shape
description: "osuBasil Room/Application.Models mutation methods must be direct field/property access, not flatten wrapper methods — a dedicated method is only earned by needing to inject an event or invariant at that exact mutation point."
metadata:
  node_type: memory
  type: feedback
  originSessionId: 422abc45-3e7f-4576-a247-b7878807b67d
  modified: 2026-09-29T00:59:22.473Z
---

Rename/info-change operations (name, password, beatmap, mods, team type, size, lock state...) on `Room` and similar Application.Models runtime objects must be plain public getter/setters or direct field access (`room.Name = x`, `room.Slots.Resize(n)`, `slot.Team = x`), never a `Change*`/`Rename`/`TransferHost`-style wrapper method created just to have an API surface.

**Why:** User corrected this directly after I proposed porting ~20 missing `Room` methods (`ChangeMods`, `ChangeTeamType`, `Rename`, `ChangePassword`, `Resize`, `Lock`/`Unlock`, `TransferHost`, `MoveSlot`...) 1:1 from the pre-split `Room`. Their words: "Các thao tác như rename, đổi thông tin các thứ, tôi đã quy về thành method bản chất hoặc getter/setter trực tiếp tại field rồi. Nếu cần cấy event vào, đó là nơi cần thêm, không phải tạo hàm flatten mới rồi yapping đủ thể loại method." Most of the "missing" surface was never meant to be re-created as methods — it was meant to collapse into the property/field itself, matching what `Host`, `Name`, `Password` already do in the committed `Room.cs`.

**How to apply:** When a compile error names a missing `Room`/domain-model method:
1. First check whether a plain property/field with the same semantics already exists (`Room.Host`, `RoomSlots.Resize`, `RoomSlot.Team`, `MatchSettings.Mods`) — use it directly at the call site.
2. Only write a dedicated method when the operation needs to inject a `RoomEvent` write (`_events.Writer.TryWrite(...)`) or an invariant check at that exact mutation point — mirror the existing `Kick`/`Ban`/`Unban` shape in `Room.cs` (validate, mutate the specific field, done — no generic abstraction layer).
3. `MatchSettings` (Basil.Domain) properties that `Room` (Basil.Application.Models, a different assembly) needs to set must be `public`, not `internal` — this project's Domain/Application split makes `internal set` on a Domain settings object inaccessible from the Application layer that now owns the mutating call sites.

This is not Room-specific: same correction landed a third time on `UserStats.WithSubmittedScore` — I added a method returning a new immutable copy (properties were `init`-only) instead of just changing `TotalScore`/`RankedScore`/`PlayCount` to plain `set` and mutating the loaded instance in place at the call site. Default assumption for this codebase: if a value needs to change after construction, make the property `set`-able and set it directly; don't reach for an immutable-copy/`with`-style method first.

See also [[feedback-eventlog-redundant]] for the related correction on the event-storage side of this same refactor.
