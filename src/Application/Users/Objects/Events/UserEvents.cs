using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects.Events;

/// <summary>Something happened to who is online.</summary>
public abstract record UserEvent : Event;

/// <summary>A connection was opened.</summary>
/// <param name="Connection">The connection that was opened.</param>
/// <param name="CameOnline">Whether this is the user's first connection, so the user just came online.</param>
public sealed record UserConnectionOpened(Connection Connection, bool CameOnline) : UserEvent;

/// <summary>A connection was closed.</summary>
/// <param name="Connection">The connection that was closed.</param>
/// <param name="Reason">Why the connection was closed.</param>
/// <param name="WentOffline">Whether this was the user's last connection, so the user went offline.</param>
public sealed record UserConnectionClosed(Connection Connection, SessionEndReason Reason, bool WentOffline)
	: UserEvent;

/// <summary>An osu! client reported a new presence status.</summary>
/// <param name="Connection">The client connection.</param>
/// <param name="Status">The new status.</param>
public sealed record UserConnectionStatusChanged(StableConnection Connection, PlayerStatus Status) : UserEvent;

/// <summary>A user was silenced.</summary>
/// <param name="User">The silenced user.</param>
/// <param name="Until">The moment the silence ends.</param>
public sealed record UserSilenced(UserRecord User, DateTimeOffset Until) : UserEvent;

/// <summary>The silence of a user was lifted before its end.</summary>
/// <param name="User">The user.</param>
public sealed record UserUnsilenced(UserRecord User) : UserEvent;

/// <summary>The privileges granted to a user were replaced.</summary>
/// <param name="User">The user.</param>
/// <param name="Privileges">The privileges now granted.</param>
public sealed record UserPrivilegesChanged(UserRecord User, UserPrivileges Privileges) : UserEvent;

/// <summary>A notification was shown to online users.</summary>
/// <param name="Recipients">The osu! clients that were sent the notification.</param>
/// <param name="Text">The text of the notification.</param>
public sealed record UserNotificationSent(IReadOnlyList<StableConnection> Recipients, string Text) : UserEvent;

/// <summary>Something happened between an osu! client and a connection spectating it.</summary>
/// <param name="Host">The osu! client being spectated.</param>
/// <param name="Spectator">The spectating connection.</param>
public abstract record UserSpectatorEvent(StableConnection Host, Connection Spectator) : UserEvent;

/// <summary>A connection started spectating an osu! client.</summary>
/// <param name="Host">The osu! client being spectated.</param>
/// <param name="Spectator">The connection that started spectating.</param>
public sealed record UserSpectatorJoined(StableConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);

/// <summary>A connection stopped spectating an osu! client.</summary>
/// <param name="Host">The osu! client that was spectated.</param>
/// <param name="Spectator">The connection that stopped spectating.</param>
public sealed record UserSpectatorLeft(StableConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);

/// <summary>An osu! client sent the frames of its play to the connections spectating it.</summary>
/// <param name="Host">The client being spectated.</param>
/// <param name="Bundle">The frames.</param>
public sealed record UserSpectatorFramesReceived(StableConnection Host, SpectatorBundle Bundle) : UserEvent;

public sealed record UserSpectatorFailed(StableConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);