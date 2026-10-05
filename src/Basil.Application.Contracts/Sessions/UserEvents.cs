using Basil.Application.Contracts.Events;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Contracts.Sessions;

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
public sealed record UserConnectionClosed(Connection Connection, ConnectionCloseReason Reason, bool WentOffline)
	: UserEvent;

/// <summary>An osu! client reported a new presence status.</summary>
/// <param name="Connection">The client connection.</param>
/// <param name="Status">The new status.</param>
public sealed record UserConnectionStatusChanged(BanchoConnection Connection, PlayerStatus Status) : UserEvent;

/// <summary>Some of a user's permissions were suspended; a silence is one such restriction.</summary>
/// <param name="User">The restricted user.</param>
/// <param name="Restriction">The new restriction.</param>
public sealed record UserRestricted(User User, Restriction Restriction) : UserEvent;

/// <summary>A restriction of a user was ended before its time.</summary>
/// <param name="User">The user the restriction applied to.</param>
/// <param name="Restriction">The lifted restriction, now ending at the moment it was lifted.</param>
public sealed record UserRestrictionLifted(User User, Restriction Restriction) : UserEvent;

/// <summary>The permissions granted to a user were replaced.</summary>
/// <param name="User">The user.</param>
/// <param name="Permissions">The permissions now granted.</param>
public sealed record UserPermissionsChanged(User User, Permissions Permissions) : UserEvent;

/// <summary>A notification was shown to online users.</summary>
/// <param name="Recipients">The osu! clients that were sent the notification.</param>
/// <param name="Text">The text of the notification.</param>
public sealed record UserNotificationSent(IReadOnlyList<BanchoConnection> Recipients, string Text) : UserEvent;

/// <summary>The reasons a connection is closed.</summary>
public enum ConnectionCloseReason : byte
{
	/// <summary>The client logged out or quit.</summary>
	LoggedOut,

	/// <summary>The transport connection was lost.</summary>
	ConnectionLost,

	/// <summary>The client sent nothing for too long.</summary>
	TimedOut,

	/// <summary>A new login of the same kind replaced this idle connection.</summary>
	Replaced,

	/// <summary>The user's account was deleted.</summary>
	Deleted,

	/// <summary>The user's password was changed.</summary>
	CredentialsChanged,

	/// <summary>The user's sessions were ended, or the user may no longer use this kind of client.</summary>
	Revoked
}