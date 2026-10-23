---
name: feedback-room-slots-conventions
description: "osuBasil RoomSlots: slots are numbered 1..16 (indexer and RoomSlot.Index), and Resize is count-based because players may sit in any slot — don't 'fix' these toward 0-based / positional."
metadata:
  type: feedback
---

`RoomSlots` is accessed through `[1..16]`, not a 0-based index, and `RoomSlot.Index` follows the same numbering (wire slot ids are converted at the protocol boundary). `Resize(size)` treats the size as a *count* of usable slots: players may sit in any slot, so keep (size - players) empty slots open and lock the rest of the empty ones; never lock/unlock by slot range.

**Why:** On 2026-09-29 my plan called the 1-based indexer and the count-based Resize "bugs" and an OpenCode agent rewrote them to 0-based + positional. The user rejected both: "_slots tôi đã quy ước là truy cập thông qua [1..16]", and the positional Resize "chưa tính trường hợp người chơi có thể ở bất cứ slot nào".

**How to apply:** Before labelling the user's code a bug, check whether it encodes a deliberate convention or a domain rule; ask if unsure. When delegating, pass these conventions explicitly to the agent. See [[feedback-runtime-references]].
