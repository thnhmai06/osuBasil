---
name: project-package-advisories
description: "Decisions on NuGet advisories (ImageSharp, SignalR, MessagePack) and the room/user redesign choices confirmed by the user"
metadata:
  node_type: memory
  type: project
  originSessionId: 8cd03193-4520-4c7f-bd41-bf52f68a6113
  modified: 2026-10-20T00:59:04.159Z
---

ImageSharp stays on v3: v4 (the only line that fixes the advisories) needs a paid license. Ignore the SixLabors.ImageSharp advisories.

SignalR.Common and MessagePack advisories: no code or csproj uses them (only the Host csproj lists their DLLs as unused runtime files to strip from publish), so they are ignored.

**Why:** user decision 2026-10-20.

**How to apply:** do not try to upgrade or pin these packages; do not report their NU190x warnings as work. Re-check only if SignalR/MessagePack is ever used.

Also confirmed by the user (2026-10-20): the Player room role holds `MembersInvite`, and the host may abort a round (`RoundsAbort`). See [[feedback-review-and-format-after-agent]].
