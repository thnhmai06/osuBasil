# The chat seam: `IrcMessage` is a leaked wire type doing an internal model's job

> Status: **Decided 2026-09-15 at `95715349`.** Written by a read-only design agent from the brief
> in `plans/execution/HANDOVER.md` §2; the three claims it rests on (the bridge drops everything but
> PRIVMSG/NOTICE at `Irc/BanchoIrcBridgeConnection.cs:32`; `ChannelMembershipService.cs:372` parses
> the server's own prefix back; 44 `new ChannelMembershipService(` sites in 24 files) were
> re-checked by the orchestrator before adoption. Nothing is applied yet; §4 is the order for C1a
> step 3. Paths are under `src/Basil.Server/Features/` unless stated; counts are grepped.
>
> **The three open questions in §6 are settled as follows.** `ChannelNotifier` lives in
> `Features/Chat/Packets/`, like steps 1 and 2. `ChatLine` is born in `Features/Chat/`, not in
> `Basil.Domain` — no move without a requirement, and C1b is undecided. The join echo is produced by
> `ChannelNotifier` calling `IrcQueryService.BuildNamesReply`, so RPL_NAMREPLY is formatted in one
> file; the `Chat -> Irc` edge is already declared.

## 1. Inventory — every protocol use in the five types, by meaning

Per file, XML comments excluded: `IrcMessageWriter.` 32 sites, `ServerPacketWriter.` 6,
`IrcNumeric.` 5, `IrcMessage` as a type 5. Every one ends in one of two delivery primitives:
`GameSession.Enqueue(byte[])` (`Shared/Sessions/GameSession.cs:146`) for a bancho packet, or
`UserSession.IrcConnection.Send(IrcMessage)` (`Shared/Sessions/UserSession.cs:126`,
`Irc/IIrcConnection.cs:27`), which for a `GameSession` is `Irc/BanchoIrcBridgeConnection.cs:28-37`,
re-encoding PRIVMSG/NOTICE into a `SendMessage` packet and dropping everything else (`:32`).

| # | Meaning | Sites (file:line) | Transport today |
|---|---|---|---|
| A | **Chat line into a channel** (PRIVMSG/NOTICE from a user or the bot, to every session of every member, then mirrored to the match SSE stream) | `Chat/ChannelMembershipService.cs:324-336` (`BroadcastPrivmsg`, the single fan-out); callers build the `IrcMessage`: `Chat/ChatDispatchService.cs:98`, `:204-208`, `:307`, `:356-357`; `Multiplayer/MatchBroadcast.cs:60-61` (`EnqueueChat`); `Multiplayer/MpCommandService.cs:1754-1755` | Both, via `IrcConnection.Send` (`:330`, `:332`) |
| B | **Chat line to one user** (DM; away-message echo; command reply to sender) | `Chat/ChatDispatchService.cs:273-275`, `:280`, `:315`, `:350`, `:366`; `Multiplayer/MatchBroadcast.cs:174`, `:176`; `Multiplayer/MpCommandService.cs:1749`, `:1764`; `Auth/ClientIntegrityService.cs:103`, `:105` | Both, via `IrcConnection.Send` |
| C | **DM refused** (blocked / PM-private → `UserDmBlocked`; target silenced → `TargetSilenced`) | `Chat/ChatDispatchService.cs:251`, `:261`, `:269` | Bancho only, guarded by `sender is GameSession`; an IRC sender gets nothing |
| D | **You joined a channel** | `Chat/ChannelMembershipService.cs:97-103`: bancho `ChannelJoin(DisplayName)` `:98`; IRC `JOIN` `:101` plus the NAMES numerics `:102` | Branches on `GameSession`/`IrcSession` inline |
| E | **You left a channel** (kick) | `:135-143`: bancho `ChannelKick(DisplayName)` when `kick` `:138`; IRC `PART` `:141` | Branches inline |
| F | **A member joined / parted** (to the other members) | `:109-110` (JOIN), `:148-149`, `:180-181` (PART) via `BroadcastToOtherIrcMembers` `:382-390` | IRC only; bancho learns presence through G |
| G | **Roster or topic changed** (`ChannelInfo` to members of an instance channel, to every reader of a public one) | `:392-408` (`BroadcastChannelInfo`), called from `:108`, `:147`, `:176`, `:355` | Bancho only |
| H | **A member quit** (one QUIT per remaining member, deduplicated across channels) | `:167` (built once), `:183-188` | IRC only |
| I | **Topic changed** (TOPIC attributed to BasilBot) | `:357-361` (`SyncTopic`) | IRC only; bancho via G |
| J | **NAMES reply** (RPL_NAMREPLY + RPL_ENDOFNAMES, prefixes from `MemberPrefix` `:264-273`) | `:199-210`, *returns* `IEnumerable<IrcMessage>`; consumed by `:102`, `Irc/IrcAuthenticationService.cs:107`, `Irc/IrcQueryService.cs:79`, `:91` | IRC only |
| K | **LIST reply** (RPL_LISTSTART/RPL_LIST/RPL_LISTEND with the match-room gate) | `:223-249`, *returns* `IEnumerable<IrcMessage>`; consumed by `Irc/TcpIrcConnection.cs:216` and `tests/Basil.Server.Tests/Features/Chat/ChannelMembershipServiceTests.cs:326-327`, `:391`, `:409-410` | IRC only |
| L | **Read-back: parse a chat line to publish it** | `Chat/ChannelMembershipService.cs:368-380` (`PublishMatchChat`: `Params.Count`, `TryParseUserPrefix(Prefix)`, `Params[1]`) | Neither; it un-encodes the line to recover sender id, name and text |
| M | **Raw bytes to game members** | `:306-314` (`BroadcastToMembers(channel, byte[])`); callers `Multiplayer/MatchBroadcast.cs:45`, `:189`, `Multiplayer/Packets/BanchoMatchNotifier.cs:56`, `:69`, `:83`, `:94` | Bancho; `byte[]` is not a protocol type, the rule does not see it |

`MpCommandService.cs:18` also imports `Basil.Protocol.Multiplayer` for `new MatchState(...)` at
`:474`. That is step 5's read-model problem, not chat; its outer row waits (§4).

Who else touches `IIrcConnection`, and what the tests read back from it (`tests/Basil.Server.Tests/Features/`):

| Consumer | Where | Reads the recorded `IrcMessage` at |
|---|---|---|
| `TcpIrcConnection` (real client) | `Irc/TcpIrcConnection.cs:34-44` | n/a, formats to wire |
| `BanchoIrcBridgeConnection` | `Irc/BanchoIrcBridgeConnection.cs:14` | n/a, re-encodes |
| `RecordingIrcConnection` | `Bot/CommandDispatcherTests.cs:858` | `:549`, `:574`, `:606`, `:647`, `:667` |
| `RecordingIrcConnection` | `Chat/ChannelDisconnectSemanticsTests.cs:153` | `:72`, `:121-122`, `:150` (`.Command is "PART" or "QUIT"`) |
| `RecordingIrcConnection` | `Chat/ChannelMembershipServiceTests.cs:433` | `:161-163` (TOPIC, `.Prefix`), `:245` (JOIN) |
| `RecordingIrcConnection` | `Chat/ChatDispatchNoticeTests.cs:128` | `:70-71` (`.Command == "NOTICE"`, `.Params[1]`), `:124-125` |
| `FakeIrcConnection` | `Irc/IrcSessionRegistryTests.cs:131` | none |
| `RecordingIrcConnection` | `Multiplayer/MpCommandServiceTests.cs:1864` | none directly |

Constructor sites a new dependency touches (src plus tests):

| Type | Sites | Files |
|---|---|---|
| `new ChannelMembershipService(` | 44 | 24 |
| `new MatchBroadcast(` | 9 | 8 |
| `new ChatDispatchService(` | 7 | 4 |
| `new MpCommandService(` | 2 | 2 |
| `new ClientIntegrityService(` | 1 | 1 |

## 2. The central call: a leaked wire type

`IrcMessage` calls itself "a pure wire-format representation with no server or session semantics"
(`src/Basil.Protocol/Irc/IrcMessage.cs:5`), and the code agrees three times:

1. **Half its vocabulary never reaches bancho.** `BanchoIrcBridgeConnection.cs:32` keeps PRIVMSG
   and NOTICE and drops JOIN, PART, QUIT, TOPIC and every numeric. A shared model is consumed
   whole; this one is filtered.
2. **The server parses its own output.** Sender identity goes in as a `nick!id@...` prefix and
   comes back through `TryParseUserPrefix` at `BanchoIrcBridgeConnection.cs:33` and
   `ChannelMembershipService.cs:372` (row L), text as `Params[1]`. Business code serialises to a
   string and deserialises to get its own fields back.
3. **The other branch costs the same mapper.** Protocol references nothing, so a Domain-owned
   `IrcMessage` could not be consumed by `IrcMessageWriter.Format` (`IrcMessageWriter.cs:11`); the
   Irc slice maps internal record to wire record on either branch. Promoting it buys nothing.

What both transports share is exactly what rows A/B and L recover by parsing: sender id, sender
name, target, text, notice flag. Five fields. Everything else is one transport's business.

**If wrong:** a future transport needing JOIN/PART from the shared record would extend `ChatLine`
with a `Kind`; nothing below is undone. Keeping `IrcMessage` as the model means every business type
that says anything imports `Basil.Protocol.Irc` forever, and the list never reaches zero.

## 3. Contract shape and where each piece lives

| Piece | Lives in | Shape | Implementation |
|---|---|---|---|
| `ChatLine` | `Features/Chat/ChatLine.cs` (to `Basil.Domain.Channels` with the slice if C1b happens) | `sealed record ChatLine(int SenderId, string SenderName, string Target, string Text, bool Notice = false)` | none |
| `IChatNotifier` | `Features/Chat/IChatNotifier.cs` | `void Deliver(UserSession recipient, ChatLine line)` (A/B); `void DmRefused(UserSession sender, string recipientName, DmRefusal reason)` (C; `DmRefusal { Blocked, Silenced }`) | `Features/Chat/Packets/ChatNotifier.cs`: `Deliver` calls `recipient.IrcConnection.Send(Privmsg/Notice)`; `DmRefused` enqueues the packet for a `GameSession`, nothing otherwise, as at `ChatDispatchService.cs:251-269` |
| `IChannelNotifier` | `Features/Chat/IChannelNotifier.cs` | `Joined(UserSession self, ChannelSession ch)` (D); `Left(self, ch, bool kick)` (E); `MemberJoined(ch, UserSession m)` / `MemberLeft(ch, m)` (F); `Quit(UserSession m, IReadOnlyCollection<int> tell, string reason)` (H); `TopicChanged(ch, UserSession by, string topic)` (I); `RosterChanged(ch)` (G) | `Features/Chat/Packets/ChannelNotifier.cs`, one class; the `GameSession`/`IrcSession` switch at `ChannelMembershipService.cs:95-104`, `:135-143` moves here |

`.Packets` is the slice's transport side by the instrument's own definition
(`TransportSeamTests.cs:14-17`); `BanchoIrcBridgeConnection.cs:2-3` already has one adapter
importing both `Basil.Protocol.Irc` and `.Packets`. Registration mirrors step 2, in
`ChatServiceCollectionExtensions.cs:19-20`.

**Routing without branching.** For A/B the mechanism exists: `UserSession.IrcConnection`
(`UserSession.cs:126`) is one abstract property per session with two implementations, and
`Deliver` calls it. `IIrcConnection.Send(IrcMessage)` keeps its signature — the rule does not see
the Irc slice, and every fake and read-back assertion in §1 keeps passing through the real adapter,
steps 1–2's "same bytes, same path". Narrowing its payload was rejected: the same `Send` carries
JOIN/PART/QUIT/TOPIC and is `ChannelDisconnectSemanticsTests.cs:72-150`'s only observation point.
For C–I the branch moves into the adapter, once.

**NAMES and LIST become IRC-side.** `BuildNamesReply`/`BuildListReply` move verbatim into
`Irc/IrcQueryService.cs`, which already wraps both (`:74-91`), has the numeric helper (`:320`), and
is exempt (`TransportSeamTests.cs:23`); the `IrcReplies.EndOfNames`/`ListChannel`/`EndOfList`
strings they use (`:209`, `:230`, `:248`) already live in `Features/Irc/`. Chat keeps the data as
two plain methods on `ChannelMembershipService`: `IReadOnlyList<string> Roster(ChannelSession)`
(`:201-204`, via the public `MemberPrefix`) and
`IEnumerable<ChannelSession> Listable(UserSession, string? filter)` (`:232-241`). The join
self-echo (`:102`) is produced by `ChannelNotifier` calling `IrcQueryService.BuildNamesReply` —
the declared `("Chat", "Irc")` edge (`SliceAdjacency.cs:76`) — so RPL_NAMREPLY is formatted in
one file. `IrcQueryService` depends on `ChannelMembershipService`, not the reverse, so there is no
DI cycle. `IrcAuthenticationService.cs:107` and `TcpIrcConnection.cs:216` re-point; the LIST tests
at `ChannelMembershipServiceTests.cs:313-413` move to `IrcQueryServiceTests` with assertions
unchanged (numerics `321/322/323` are IRC wire contracts).

**Lock discipline.** Every method is `void` and synchronous, like `IIrcConnection.Send`
(`IIrcConnection.cs:22-27`, "must never block on I/O"), because chat is delivered under
`MatchSession.Lock` (§5). A `Task`-returning notifier invites an await under the lock.
`PublishMatchChat` (L) reads `line.SenderId`/`line.Text`; `TryParseUserPrefix` leaves Chat.

## 4. Commit order — one row per commit, tree green after each

Eight chat rows; NetArchTest pins nested types separately (`TransportSeamTests.cs:39-54`:
`+ChannelReplySink`, `+DmReplySink`, `+ScopedDmReplySink`). Seven delete by chat work;
`MpCommandService` itself keeps its row for `MatchState` (`:474`) until step 5.

| # | Commit | Adds | Sites changed | Ctor sites | Row deleted | List |
|---|---|---|---|---|---|---|
| 1 | `ClientIntegrityService` says through `IChatNotifier` | `ChatLine`, `IChatNotifier`, `ChatNotifier`, DI | `:103`, `:105` | 1 | `Auth.ClientIntegrityService` | 17 → 16 |
| 2 | `MatchBroadcast` says through `IChatNotifier` | a `BroadcastPrivmsg(channel, ChatLine, skip)` overload **beside** the `IrcMessage` one (six src callers; replacing it would drag commits 3–4's types in); `PublishMatchChat` reads fields | `:60-61`, `:174`, `:176` | 9 | `Multiplayer.MatchBroadcast` | 16 → 15 |
| 3 | `ScopedDmReplySink` | nothing | `:1749`, `:1754-1755`, `:1764` | 2 | `MpCommandService+ScopedDmReplySink` | 15 → 14 |
| 4 | `ChatDispatchService` and its two sinks | `DmRefusal` | `:98`, `:204-208`, `:251`, `:261`, `:269`, `:273-275`, `:280`, `:307`, `:315`, `:350`, `:356`, `:366` | 7 | `ChatDispatchService`, `+ChannelReplySink`, `+DmReplySink` | 14 → 11 |
| 5 | `ChannelMembershipService` | `IChannelNotifier`, `ChannelNotifier`, `Roster`, `Listable`; J/K to `IrcQueryService`; old `BroadcastPrivmsg` overload deleted (tests `:372-373` build a `ChatLine`) | all of D–K | 44 | `Chat.ChannelMembershipService` | 11 → 10 |

Commit 5 cannot be sliced thinner while row deletion is the currency: D–I are spread over the same
three methods and the row goes only when the last `IrcMessageWriter` call leaves. Step 2's deferred
"seven packet handlers move to `BanchoMatchNotifier.Broadcast`" is not needed for commit 2 —
`Enqueue(match, byte[])` is invisible to the rule — and stays out.

Each commit: five test calls, row deleted, `phase-stage-c.md` checkpoint in the same commit.

## 5. Risks

* **Lock sites that call into chat.** `MatchLifecycle.cs:368`, `:377` (`AnnounceToRoomAndReferees`)
  are inside `await using (await match.BeginMutationAsync(token))` (`:363-380`); `:262`, `:276`,
  `:288` (`EnqueueChat`) are in `StartAsync(match, mutation, ...)`; `AbortHandler.cs:58` sits
  between `notifier.RoundAborted` and `mutation.PublishState()` (`:57-59`);
  `MatchChangeSettingsHandler.cs:118` is inside the mutation writing `match.MapName` (`:100-119`).
  `TimerHandler.cs:246`, `MatchLifecycle.cs:238` (`CancelQueuedAutoStart`, four
  `MatchControlService` callers) and `MatchMembership.cs:304` (`SyncTopic`) are reached from
  mutating services and were not traced individually. The notifier replaces the encoder on the
  same line, so the locked sequence is unchanged — provided every new method stays sync/void.
* **IRC wire-text tests.** `TcpIrcConnectionTests.cs:45-137` reads real lines through a real
  `TcpIrcConnection`, whose `Send` this design does not touch. NAMES/LIST numerics move files, not
  bytes.
* **Sinks that read back.** The six fakes keep compiling and their `.Command`/`.Params` assertions
  keep passing because `ChatNotifier` produces the same `IrcMessage` the services did.
* **Behaviour to preserve.** An IRC sender whose DM is refused gets nothing today
  (`ChatDispatchService.cs:251`, `:260`, `:268`); `DmRefused`'s IRC branch must stay a no-op.
  `IrcAuthenticationService.cs:101-107` sends NAMES twice at login (`Join`'s echo at `:102`, then
  `:107`); moving J must not "fix" that in the same commit.
* **Ctor fan-out.** 44 `ChannelMembershipService` sites in 24 files — the mechanical change step 2
  made with Rider's `change_api_signature`, defaulting to the real adapter.

## 6. What you are not sure about

* `Features/Chat/Packets/` versus the Irc slice for `ChannelNotifier`, which writes more IRC lines
  than packets. Both satisfy the rule; `.Packets` follows steps 1–2, the Irc slice follows
  `BanchoIrcBridgeConnection`. I chose `.Packets`.
* Whether `ChatLine` should be born in `Basil.Domain.Channels`. The brief's option A says
  Domain-owned; rule 2 says no move without a requirement, and C1b is undecided. Deferred.
* Whether `ChannelNotifier` taking `IrcQueryService` for the join echo is acceptable, or whether
  the orchestrator prefers the roster passed as a parameter (`Joined(self, ch, roster)`) so the
  adapter has no Irc-slice dependency. Either compiles.
* Nothing was built or run; counts are grep results and "compiles after each commit" is by
  inspection of the dependency edges.
* `MpCommandService`'s outer row is gated on `MatchState` (`:474`); if step 5 moves that record
  to Domain, the row falls out then.
