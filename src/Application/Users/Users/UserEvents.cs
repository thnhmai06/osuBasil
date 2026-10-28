using Basil.Application.Users.Connections;
using Basil.Application.Common.Events;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

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
public sealed record UserConnectionStatusChanged(BanchoConnection Connection, PlayerStatus Status) : UserEvent;

/// <summary>Some of a user's permissions were suspended; a silence is one such restriction.</summary>
/// <param name="User">The restricted user.</param>
/// <param name="Restriction">The new restriction.</param>
public sealed record UserRestricted(UserRecord User, UserRestriction Restriction) : UserEvent;

/// <summary>A restriction of a user was ended before its time.</summary>
/// <param name="User">The user the restriction applied to.</param>
/// <param name="Restriction">The lifted restriction, now ending at the moment it was lifted.</param>
public sealed record UserRestrictionLifted(UserRecord User, UserRestriction Restriction) : UserEvent;

/// <summary>A restriction was removed from a user's history.</summary>
/// <param name="User">The user whose restriction was removed.</param>
/// <param name="Restriction">The removed restriction.</param>
public sealed record UserRestrictionDeleted(UserRecord User, UserRestriction Restriction) : UserEvent;

/// <summary>The permissions granted to a user were replaced.</summary>
/// <param name="User">The user.</param>
/// <param name="Permissions">The permissions now granted.</param>
public sealed record UserPermissionsChanged(UserRecord User, UserPermissions Permissions) : UserEvent;

/// <summary>A notification was shown to online users.</summary>
/// <param name="Recipients">The osu! clients that were sent the notification.</param>
/// <param name="Text">The text of the notification.</param>
public sealed record UserNotificationSent(IReadOnlyList<BanchoConnection> Recipients, string Text) : UserEvent;

/// <summary>Something happened between an osu! client and a connection spectating it.</summary>
/// <param name="Host">The osu! client being spectated.</param>
/// <param name="Spectator">The spectating connection.</param>
public abstract record UserSpectatorEvent(BanchoConnection Host, Connection Spectator) : UserEvent;

/// <summary>A connection started spectating an osu! client.</summary>
/// <param name="Host">The osu! client being spectated.</param>
/// <param name="Spectator">The connection that started spectating.</param>
public sealed record UserSpectatorJoined(BanchoConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);

/// <summary>A connection stopped spectating an osu! client.</summary>
/// <param name="Host">The osu! client that was spectated.</param>
/// <param name="Spectator">The connection that stopped spectating.</param>
public sealed record UserSpectatorLeft(BanchoConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);

/// <summary>A spectator reported that it failed to spectate, usually because it lacks the beatmap.</summary>
/// <param name="Host">The osu! client being spectated.</param>
/// <param name="Spectator">The spectator that failed to spectate.</param>
/// <summary>An osu! client sent the frames of its play to the connections spectating it.</summary>
/// <param name="Host">The client being spectated.</param>
/// <param name="Bundle">The frames.</param>
public sealed record UserSpectatorFramesReceived(BanchoConnection Host, SpectatorBundle Bundle) : UserEvent;

public sealed record UserSpectatorFailed(BanchoConnection Host, Connection Spectator)
	: UserSpectatorEvent(Host, Spectator);
