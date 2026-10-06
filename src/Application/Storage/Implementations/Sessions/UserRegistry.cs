using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Implementations.Sessions;

/// <summary>The users who are online, each with their session.</summary>
internal sealed class UserRegistry : IUserRegistry
{
	private readonly ConcurrentDictionary<User, UserSession> _sessions = new();
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <inheritdoc />
	public IEnumerable<UserSession> Sessions => _sessions.Values;

	/// <inheritdoc />
	public UserSession? Find(User user)
	{
		return _sessions.GetValueOrDefault(user);
	}

	/// <inheritdoc />
	public Connection? Find(string token)
	{
		return _connections.GetValueOrDefault(token);
	}

	/// <inheritdoc />
	public SpectatorChannelSession? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _sessions.Values
			.Select(session => session.Bancho?.SpectatorChannel)
			.FirstOrDefault(channel => channel is not null && !ReferenceEquals(channel.Host, connection) &&
			                           channel.Members.Contains(connection));
	}

	/// <inheritdoc />
	void IUserRegistry.Add(UserSession session)
	{
		_sessions[session.User] = session;
	}

	/// <inheritdoc />
	bool IUserRegistry.Remove(UserSession session)
	{
		return _sessions.TryRemove(new KeyValuePair<User, UserSession>(session.User, session));
	}

	/// <inheritdoc />
	void IUserRegistry.Index(Connection connection)
	{
		_connections[connection.Token] = connection;
	}

	/// <inheritdoc />
	void IUserRegistry.Unindex(Connection connection)
	{
		_connections.TryRemove(new KeyValuePair<string, Connection>(connection.Token, connection));
	}

	/// <inheritdoc />
	Lock.Scope IUserRegistry.Enter()
	{
		return _sync.EnterScope();
	}
}
