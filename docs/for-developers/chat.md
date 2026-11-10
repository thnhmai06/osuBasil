# Chat and commands

## Overview

Chat uses one channel model for general, room, spectator and private-message conversations. A `Channel` owns its members and events; the channel's kind and owner determine who may read and write. A connection acts on a channel through a short-lived scope.

The osu! client chats through the osu! (stable) host (chat is pushed in game packets) and IRC clients through the IRC host. The HTTP API neither sends nor receives chat: it manages general channels and sends announcements. The server stores no chat history on any surface.

This page describes the application model and its authorization rules. It does not document transport routes or packet formats.

## Channel kinds and owners

| Kind | Runtime channel | Owner | Who can read |
|---|---|---|---|
| General | `GeneralChannel` | `GeneralChannelRegistry` | Connections whose effective privileges satisfy the channel's read requirement |
| Room | `RoomChannel` | `RoomChannels`, one per open room | Room participants and users allowed to observe the room |
| Spectator | `SpectatorChannel` | `SpectatorChannels`, one per watched `StableConnection` | The host and its spectators |
| Private message | `PmChannel` | `PmChannels`, one per online user | The recipient's connections, except osu!tourney connections |

General channels use configured names such as `#osu`; room and spectator channels use `#mp_{room id}` and `#spec_{user id}`. A private-message channel uses its owner's name without `#`. The room channel opens and closes with its room. A spectator channel is opened for a watched client when its first spectator joins and closed when that client closes. Chat opens a private-message channel when the online user is first needed and closes it when the user goes offline. The `User` owns the away message and private-message settings shared across its connections.

## Read and write rules

Each kind of channel decides who may read and write it (`CanRead` and `CanWrite` on the channel). General-channel requirements are masks of derived `UserPrivileges`; every requested bit must be present, and an empty requirement allows access.

| Kind | Read | Write |
|---|---|---|
| General | Effective privileges satisfy `ReadPrivileges` | Effective privileges satisfy `WritePrivileges` |
| Room | A room participant or an account with effective `Tournament` | A room participant or an account with effective `Tournament` |
| Spectator | The spectated player or one of that player's spectators | The spectated player or one of that player's spectators |
| Private message | The recipient's connection, except osu!tourney | An open connection with effective `Player` |

For a room channel, participants include seated players, registered observers, and the room's creator or referees when their room permissions are effective. `Tournament` also grants access across rooms. These are account permissions, not authority inferred from the connection type.

## Acting on a channel

Create a scope from the channel and the real caller connection. The scope keeps the caller with each operation, so callers do not pass an actor or privilege mask repeatedly:

```csharp
var scopedChannel = channel.ScopeTo(by);
var result = scopedChannel.Join();
```

`ScopedChannel` provides `Join`, `Part` and `PostAsync`. For a general channel, `scopedChannel.General` provides `ConfigureAsync` and `DeleteAsync`; both require effective `Moderator`. A missing privilege comes back as a denial naming the missing privileges; otherwise `ConfigureAsync` returns `Ok` or `Closed`. Configuring a channel removes members who can no longer read it.

The channel to manage is found through `server.ScopeTo(by).Channels.Find(name)`. It requires effective `Moderator` and answers `NotFound` only once that right is met, so a connection without it is refused the same way whether or not the channel exists.

### Results

Every operation returns `Result<ChannelOutcome>` rather than throwing for ordinary refusals. A refusal for lack of a privilege carries the missing privileges.

| Operation | Outcome | Meaning |
|---|---|---|
| `Find` | `Ok` | The connection may manage channels and the channel has that name |
| | `NotFound` | No open general channel has that name |
| `Join` | `Ok` | The connection joined |
| | `AccessDenied` | The kind of channel does not let the caller read it |
| | `AlreadyMember` | The connection is already a member or an open same-kind connection is already present |
| | `Closed` | The channel is closed |
| `Part` | `Ok` | The connection left |
| | `NotMember` | The connection was not a member |
| `PostAsync` | `Ok` | The message was accepted |
| | `Empty` | The text is empty; text of only whitespace is a message |
| | `InvalidText` | The text holds a control character chat does not carry (tab, CR, LF and U+0001 are carried) |
| | `NotMember` | The sender is not a member of a channel that requires membership |
| | `AccessDenied` | The kind of channel does not let the sender write |
| | `Blocked` | The recipient blocks the sender or accepts private messages only from friends |
| | | `Silenced` | The sender is silenced and may not post |
| | `TargetSilenced` | The recipient is silenced, so private messages to them are refused |
| | `Closed` | The channel is closed |

A sender without the `Player` privilege is refused with a denial that names it; a silenced sender is refused with `Silenced`. A closed same-kind connection may be replaced during a join; an open one cannot. osu!tourney and API connections may have multiple concurrent memberships of their kind.

Text over the channel's maximum length is truncated. The message event reports whether truncation occurred.

## Private messages

A private message is a post into the recipient's `PmChannel`. It reaches the recipient's open connections except osu!tourney connections. The sender needs effective `Player`; the recipient's active restriction can refuse the message with `TargetSilenced`.

The recipient's block and friends-only setting both apply. A sender with effective `Moderator` bypasses those limits. If the recipient has an away message, the same posted-message event carries the automatic reply; a notice does not trigger an away reply.

## Events and session behavior

Membership and message events belong to the channel that changed, not to the sender's `User`. A private-message event can carry both the posted message and an away reply. Spectating is a relation on the spectated osu! connection, and Chat keeps that client's spectator channel in step by reacting to the spectator events: the first spectator brings the host into the channel, the last departing spectator removes it, and closing the connection closes the channel. Channel membership therefore follows the events and is eventually consistent with them.

These ownership rules keep channel membership on the object being joined and avoid a second, competing list of channels on the user.

## BasilBot and commands

BasilBot is the server's voice, not an authority that users borrow.

BasilBot is online with a permanent `User` and one Stable and one Irc connection that the server opens at startup with no transport behind them; a private message to it is a command. Commands act with the typist's connection and privileges, a room command calls the room through `room.ScopeTo(by)`, and BasilBot owns the room countdowns: it keeps their timers, announces their marks and starts the round with the typist's connection when one elapses. Server-originated bot messages do not go through an ordinary caller's privilege check, and the bot never owns room authority.

## Invariants

* Each channel belongs to its owner and is opened and closed with that owner.
* A channel's read and write rules depend on its kind; a transport does not supply authority.
* Every required general-channel privilege bit must be present; an empty requirement is satisfied.
* Private-message posting requires `Player`; recipient blocks and friends-only settings yield to effective `Moderator`.
* A silence stops a sender from posting and a recipient from receiving private messages.
* Channel events describe the channel that changed, not a relation stored on the sender.
* BasilBot commands use the typist's authority; the transports must deliver channel messages only to `Channel.RecipientsAsync`.

## Related code

* [`Channel.cs`](../../src/Application/Chat/Objects/Channel.cs): channel state, `ScopeTo` and the read and write rules each kind overrides
* [`ScopedChannel.cs`](../../src/Application/Chat/Capabilities/ScopedChannel.cs): join, part and post operations
* [`ScopedGeneralChannel.cs`](../../src/Application/Chat/Capabilities/ScopedGeneralChannel.cs): general-channel configuration and deletion
* [`ScopedServerChannels.cs`](../../src/Application/Servers/Capabilities/ScopedServerChannels.cs): general-channel creation, and the lookup of the channel to manage
* [`ChannelOutcome.cs`](../../src/Application/Chat/Objects/ChannelOutcome.cs): operation outcomes
* [`GeneralChannelRegistry.cs`](../../src/Application/Chat/Registry/GeneralChannelRegistry.cs): general-channel ownership and the automatic joins and parts
* [`GeneralChannel.cs`](../../src/Application/Chat/Objects/GeneralChannel.cs), [`PmChannel.cs`](../../src/Application/Chat/Objects/PmChannel.cs), [`PmChannels.cs`](../../src/Application/Chat/Registry/PmChannels.cs): general and private-message channels, and the private-message channel of each online user; [`GeneralChannelRecord.cs`](../../src/Domain/Chat/GeneralChannelRecord.cs): the stored general channel
* [`RoomChannel.cs`](../../src/Application/Chat/Objects/RoomChannel.cs), [`SpectatorChannel.cs`](../../src/Application/Chat/Objects/SpectatorChannel.cs), [`SpectatorChannels.cs`](../../src/Application/Chat/Registry/SpectatorChannels.cs): room and spectator channels, and the spectator channel of each watched client
* [`User.cs`](../../src/Application/Users/Objects/User.cs), [`ScopedUser.cs`](../../src/Application/Users/Capabilities/ScopedUser.cs): the user's online state (away message, connections)
* [`ScopedStableConnection.cs`](../../src/Application/Users/Capabilities/ScopedStableConnection.cs): spectator operations
* [`Room.cs`](../../src/Application/Multiplayer/Objects/Rooms/Room.cs), [`RoomCountdowns.cs`](../../src/Infrastructure/Bot/Workers/RoomCountdowns.cs): room ownership and the bot's countdowns

## See also

* [`multiplayer.md`](multiplayer.md): room authority and room channels
* [`privileges.md`](privileges.md): privileges, silences and `Moderator`
* [`irc.md`](irc.md): IRC integration
* [`working-scopes.md`](working-scopes.md): supported and excluded chat features
