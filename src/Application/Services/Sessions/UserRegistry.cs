using System.Collections.Concurrent;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Sessions;

/// <summary>The users who are online, each with their session.</summary>
internal sealed class UserRegistry : IUserRegistry
{
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<UserRecord, UserSession> _sessions = new();
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <inheritdoc />
	public IReadOnlyDictionary<UserRecord, UserSession> Sessions => _sessions;

	/// <inheritdoc />
	public Connection? Find(string token)
	{
		return _connections.GetValueOrDefault(token);
	}

	/// <inheritdoc />
	public BanchoConnection? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _sessions.Values
			.Select(session => session.Bancho)
			.FirstOrDefault(host => host is not null && host.Spectators.Contains(connection));
	}

	/// <inheritdoc />
	void IUserRegistry.Add(UserSession session)
	{
		_sessions[session.User] = session;
	}

	/// <inheritdoc />
	bool IUserRegistry.Remove(UserSession session)
	{
		return _sessions.TryRemove(new KeyValuePair<UserRecord, UserSession>(session.User, session));
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
