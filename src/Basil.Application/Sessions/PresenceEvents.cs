using Basil.Application.Common.Events;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>Something happened to who is online.</summary>
public abstract record PresenceEvent : Event;

/// <summary>A connection was opened.</summary>
/// <param name="Connection">The connection that was opened.</param>
/// <param name="CameOnline">Whether this is the user's first connection, so the user just came online.</param>
public sealed record ConnectionOpened(Connection Connection, bool CameOnline) : PresenceEvent;

/// <summary>A connection was closed.</summary>
/// <param name="Connection">The connection that was closed.</param>
/// <param name="Reason">Why the connection was closed.</param>
/// <param name="WentOffline">Whether this was the user's last connection, so the user went offline.</param>
public sealed record ConnectionClosed(Connection Connection, ConnectionCloseReason Reason, bool WentOffline)
	: PresenceEvent;

/// <summary>An osu! client reported a new presence status.</summary>
/// <param name="Connection">The client connection.</param>
/// <param name="Status">The new status.</param>
public sealed record StatusChanged(BanchoConnection Connection, PlayerStatus Status) : PresenceEvent;

/// <summary>A user was silenced.</summary>
/// <param name="User">The silenced user.</param>
/// <param name="EndsAt">When the silence ends.</param>
public sealed record UserSilenced(User User, DateTimeOffset EndsAt) : PresenceEvent;

/// <summary>A user's statistics changed.</summary>
/// <param name="User">The user whose statistics changed.</param>
public sealed record StatsChanged(User User) : PresenceEvent;

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
	Replaced
}