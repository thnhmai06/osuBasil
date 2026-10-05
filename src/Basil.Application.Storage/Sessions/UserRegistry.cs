using System.Collections.Concurrent;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Sessions;

/// <summary>The users who are online, each with their session.</summary>
public sealed class UserRegistry
{
	private readonly ConcurrentDictionary<User, UserSession> _sessions = new();
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <summary>Gets every online session.</summary>
	public IEnumerable<UserSession> Sessions => _sessions.Values;

	/// <summary>Finds the online session of a user.</summary>
	/// <param name="user">The user to look up.</param>
	/// <returns>The user's session, or <see langword="null" /> when the user is offline.</returns>
	public UserSession? Find(User user)
	{
		return _sessions.GetValueOrDefault(user);
	}

	/// <summary>Finds the open connection a token identifies.</summary>
	/// <param name="token">The token the client presented.</param>
	/// <returns>The connection, or <see langword="null" /> when no open connection has that token.</returns>
	public Connection? Find(string token)
	{
		return _connections.GetValueOrDefault(token);
	}

	/// <summary>Finds the spectator channel a connection is spectating.</summary>
	/// <param name="connection">The connection to look up.</param>
	/// <returns>The channel of the player being spectated, or <see langword="null" /> when the connection is not spectating.</returns>
	public SpectatorChannelSession? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _sessions.Values
			.Select(session => session.Bancho?.SpectatorChannel)
			.FirstOrDefault(channel => channel is not null && !ReferenceEquals(channel.Host, connection) &&
			                           channel.Members.Contains(connection));
	}

	/// <summary>Adds an online session.</summary>
	internal void Add(UserSession session)
	{
		_sessions[session.User] = session;
	}

	/// <summary>Removes an online session; a different session of the same user is left in place.</summary>
	internal bool Remove(UserSession session)
	{
		return _sessions.TryRemove(new KeyValuePair<User, UserSession>(session.User, session));
	}

	/// <summary>Makes a connection findable by its token.</summary>
	internal void Index(Connection connection)
	{
		_connections[connection.Token] = connection;
	}

	/// <summary>Stops a connection being findable by its token; another connection with the same token is left in place.</summary>
	internal void Unindex(Connection connection)
	{
		_connections.TryRemove(new KeyValuePair<string, Connection>(connection.Token, connection));
	}

	/// <summary>Enters the scope in which connections are opened and closed one at a time.</summary>
	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}
}