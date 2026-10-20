# Chat and commands

## Overview

Chat uses one channel model for general, room, spectator and private-message conversations. A `Channel` owns its members and events; the channel's kind and owner determine who may read and write. A connection acts on a channel through a short-lived scope.

This page describes the application model and its authorization rules. It does not document transport routes or packet formats.

## Channel kinds and owners

| Kind | Runtime channel | Owner | Who can read |
|---|---|---|---|
| General | `GeneralChannel` | `GeneralChannelRegistry` | Connections whose effective privileges satisfy the channel's read requirement |
| Room | `RoomChannel` | The `Room` | Room participants and users allowed to observe the room |
| Spectator | `SpectatorChannel` | The spectated `BanchoConnection` | The host and its spectators |
| Private message | `PmChannel` | The recipient's `User` | The recipient's connections, except osu!tourney connections |

General channels use configured names such as `#osu`; room and spectator channels use `#mp_{room id}` and `#spec_{user id}`. A private-message channel uses its owner's name without `#`. The room channel opens and closes with its room. A user session owns the away message and private-message settings shared across its connections.

## Read and write rules

`ChannelRules` defines access by channel kind. General-channel requirements are masks of derived `Privileges`; every requested bit must be present, and an empty requirement allows access.

| Kind | Read | Write |
|---|---|---|
| General | Effective privileges satisfy `ReadPrivileges` | Effective privileges satisfy `WritePrivileges` |
| Room | A room participant or an account with effective `RoomObserve` | A room participant or an account with effective `ChatPostAnyRoom` |
| Spectator | The spectated player or one of that player's spectators | The spectated player or one of that player's spectators |
| Private message | The recipient's connection, except osu!tourney | An open connection with effective `ChatPostPrivate` |

For a room channel, participants include seated players, registered observers, and the room's creator or referees when their room permissions are effective. `RoomManageAny` also grants access across rooms. These are account permissions, not authority inferred from the connection type.

## Acting on a channel

Create a scope from the channel and the real caller connection. The scope keeps the caller with each operation, so callers do not pass an actor or permission mask repeatedly:

```csharp
var scopedChannel = channel.ScopeTo(by);
var result = scopedChannel.Join();
```

`ScopedChannel` provides `Join`, `Part` and `PostAsync`. For a general channel, `scopedChannel.General` provides `ConfigureAsync` and `DeleteAsync`; both require effective `ServerConfigure` and return `ChannelManageResult`. A configuration returns `Ok`, `NotAuthorized` or `Closed`. Configuring a channel removes members who can no longer read it.

### Membership results

| Operation | Result | Meaning |
|---|---|---|
| `Join` | `Joined` | The connection joined |
| | `NoPermission` | The caller may not read the channel |
| | `AlreadyMember` | The connection is already a member or an open same-kind connection is already present |
| | `Closed` | The channel is closed |
| `Part` | `Parted` | The connection left |
| | `NotMember` | The connection was not a member |

A closed same-kind connection may be replaced during a join; an open one cannot. osu!tourney and API connections may have multiple concurrent memberships of their kind. `Join` and `Part` return `ChannelJoinResult` and `ChannelPartResult` respectively.

### Posting results

`PostAsync` returns `ChannelPostResult` rather than throwing for ordinary authorization and message outcomes:

| Result | Meaning |
|---|---|
| `Posted` | The message was accepted |
| `NoWritePermission` | The sender lacks a required permission or channel write right |
| `Silenced` | An active restriction suspends the sender's chat permission |
| `Blocked` | The recipient blocks the sender or accepts private messages only from friends |
| `TargetSilenced` | An active restriction suspends the recipient's private-message permission |
| `NotMember` | The sender is not a member of a channel that requires membership |
| `Empty` | The text is empty or whitespace |
| `Closed` | The channel is closed |

Text over the channel's maximum length is truncated. The message event reports whether truncation occurred.

## Private messages

A private message is a post into the recipient's `PmChannel`. It reaches the recipient's open connections except osu!tourney connections. The sender needs effective `ChatPostPrivate`; the recipient's active restriction can refuse the message with `TargetSilenced`.

The recipient's block and friends-only setting both apply. A sender with effective `ChatMessageAnyone` bypasses those limits. If the recipient has an away message, the same posted-message event carries the automatic reply; a notice does not trigger an away reply.

## Events and session behavior

Membership and message events belong to the channel that changed, not to the sender's `User`. A private-message event can carry both the posted message and an away reply. Spectating is owned by the spectated osu! connection: its first spectator brings it into the spectator channel, and its last departing spectator removes it. Closing that connection closes its spectator channel.

These ownership rules keep channel membership on the object being joined and avoid a second, competing list of channels on the user.

## BasilBot and commands

BasilBot is the server's voice, not an authority that users borrow.

BasilBot is online with a permanent `User` and a `BotConnection` that the server opens at startup; a private message to it is a command. Commands act with the typist's connection and permissions, a room command calls the room through `room.ScopeTo(by)`, and countdown announcements follow room-owned countdown events. Server-originated bot messages will not go through an ordinary caller's permission check. BasilBot itself will not own countdown timing or room authority.

## Invariants

* Each channel belongs to its owner and is opened and closed with that owner.
* A channel's read and write rules depend on its kind; a transport does not supply authority.
* Every required general-channel privilege bit must be present; an empty requirement is satisfied.
* Private-message posting requires `ChatPostPrivate`; recipient blocks and friends-only settings yield to effective `ChatMessageAnyone`.
* Restrictions apply to effective permissions for both sender and recipient.
* Channel events describe the channel that changed, not a relation stored on the sender.
* BasilBot commands use the typist's authority; the transports must deliver channel messages only to `Channel.RecipientsAsync`.

## Related code

* [`Channel.cs`](../../src/Application/Objects/Chat/Channel.cs): channel state and `ScopeTo`
* [`ScopedChannel.cs`](../../src/Application/Objects/Chat/ScopedChannel.cs): join, part and post operations
* [`ScopedGeneralChannel.cs`](../../src/Application/Objects/Chat/ScopedGeneralChannel.cs): general-channel configuration and deletion
* [`ChannelRules.cs`](../../src/Application/Objects/Chat/ChannelRules.cs): read and write rules by kind
* [`ChannelResults.cs`](../../src/Application/Objects/Chat/ChannelResults.cs): operation outcomes
* [`GeneralChannelRegistry.cs`](../../src/Application/Objects/Chat/GeneralChannelRegistry.cs): general-channel ownership and the automatic joins and parts
* [`GeneralChannel.cs`](../../src/Application/Objects/Chat/GeneralChannel.cs), [`PmChannel.cs`](../../src/Application/Objects/Chat/PmChannel.cs): general and private-message channels; [`GeneralChannelRecord.cs`](../../src/Domain/Chat/GeneralChannelRecord.cs): the stored general channel
* [`RoomChannel.cs`](../../src/Application/Objects/Multiplayer/Rooms/RoomChannel.cs), [`SpectatorChannel.cs`](../../src/Application/Objects/Sessions/SpectatorChannel.cs): room and spectator channels
* [`User.cs`](../../src/Application/Objects/Users/User.cs), [`ScopedUser.cs`](../../src/Application/Objects/Users/ScopedUser.cs): the user's online state (PM channel, away message, connections)
* [`ScopedBanchoConnection.cs`](../../src/Application/Objects/Sessions/ScopedBanchoConnection.cs): spectator operations
* [`Room.cs`](../../src/Application/Objects/Multiplayer/Rooms/Room.cs), [`RoomCountdown.cs`](../../src/Application/Objects/Multiplayer/Rooms/RoomCountdown.cs): room ownership and countdown

## See also

* [`multiplayer.md`](multiplayer.md): room authority and room channels
* [`privileges.md`](privileges.md): permissions, restrictions and `ChatMessageAnyone`
* [`irc.md`](irc.md): IRC integration
* [`working-scopes.md`](working-scopes.md): supported and excluded chat features
