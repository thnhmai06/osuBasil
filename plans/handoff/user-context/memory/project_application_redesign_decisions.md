---
name: application-redesign-decisions
description: "Current decisions for the Basil.Application redesign (plans/application-environment-plan-20260930.md): session/connection model, chat channel naming and ownership, repositories, R28 method API, all decided 2026-09-30/10-01."
metadata:
  node_type: memory
  type: project
  originSessionId: 9424f3a0-187c-4744-9c0c-1e47cd3acb93
  modified: 2026-09-30T17:54:26.174Z
---

The authoritative record is `plans/application-environment-plan-20260930.md` (part A = decisions,
part C = phases 0–10 with call sites). Summary of what the user decided (2026-09-30 / 10-01):

- **Principles:** acted-on object owns relations and emits events; Application only emits events
  (handlers/dispatcher in Infrastructure); objects react to other objects only via events; a
  manager holding an old connection of the same user asks it (`IsOpen`) — open → refuse, closed →
  evict and admit. Cleanup must be idempotent; dispatcher processes one source in order.
- **Sessions:** sealed `UserSession` (one online period of a user, reference identity, looked up by
  `User` via `Presence`) holds `AwayMessage`, `PmChannel`, and connections keyed by
  `ConnectionType` (`Bancho`, `Tourney`, `Irc`, `Bot`); `ConnectionType.AllowsMany()` extension
  (only `Tourney`). Abstract `Connection` (`Session`, `Login`, `IsOpen`, `Type`; reference identity)
  with `BanchoConnection` (`Status`, `LastActiveAt`, `UtcOffset`, `SpectatorChannel`),
  `TourneyConnection`, `IrcConnection`, `BotConnection`. Events `ConnectionOpened(cameOnline)`,
  `ConnectionClosed(wentOffline)`. Re-login R2 applies to single-kind types.
- **Chat naming (Domain `X`, runtime `XSession`, no "Definition"):** Domain abstract `ChatChannel`
  (`Name`, `Topic`) + `GeneralChatChannel` (privileges, AutoJoin, Visible; equality by Name),
  `RoomChatChannel`, `SpectatorChatChannel`, `PmChatChannel`; record `ChatMessage(From, Content,
  Timestamp)`. Runtime `ChatChannelSession` + `General/Room/Spectator/PmChatChannelSession`.
  Directory `ChatChannels` manages only general channels; Room owns its room channel; UserSession
  its PM channel; BanchoConnection its spectator channel. Events `ChatChannelOpened/Closed`.
- **Rooms:** R28 = methods taking `by` with authority inside Room, settings via one
  `Configure(by, change)` → one `RoomSettingsChanged`; setters only assign+validate (R31, see
  [[setters-and-naming]]). Creator/referee/host rules (R6), tournament room closes after 15 min empty.
- **Other:** `IXxxRepository` per model; registries are Application objects; channel names without
  `#`; privileges need every bit; private = history privacy; rooms may pick maps the server lacks.

- **Naming (2026-10-01):** Domain `X` → runtime `XSession` → `XRegistry` (`Channel`/`ChannelSession`/`GeneralChannelRegistry`, `User`/`UserSession`/`UserRegistry`; osu! terms win: `Match`/`Room`/`Lobby`); `ChatMessage`→`Message`. Events `{Group}{Subject}{PastTense}` (`RoomRoundAllLoaded`, `ChannelSpectatorCantSpectate`, `LobbyRoomClosingAnnounced`); category records likewise (`UserEvent`, `RoomRoundEvent`). The `lobby` chat channel is an ordinary general channel. Older lines above use pre-rename names.

**Why:** user decisions during the 2026-09-30 review sessions.

- **Execution (2026-10-01): phases 0–10 DONE and committed on develop (one commit per phase, snapshot refs deleted); leftovers and later fixes in plan appendix F.** no test project is built ("Test cần có" lists are notes only). Phases
  run through OpenCode on the user's server `--server http://127.0.0.1:4096`; orchestration rules
  (model per phase, prompt template, review checklist, snapshot refs `refs/basil/pre-phase<N>`)
  live in the plan section "Điều phối OpenCode". Base = uncommitted identity-wrapper work.

**How to apply:** read the plan before touching Application; delegate phases to OpenCode with the
full spec from the phase; check each change against AGENTS.md "Domain and Application". Related:
[[passive-side-owns-interaction]], [[docs-after-source]].
