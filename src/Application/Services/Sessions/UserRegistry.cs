using System.Collections.Concurrent;
using Basil.Application.Objects.Sessions;
using Basil.Application.Objects.Users;
using Basil.Domain.Users;

namespace Basil.Application.Services.Sessions;

/// <summary>The users who are online.</summary>
internal sealed class UserRegistry : IUserRegistry
{
	private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<UserRecord, User> _online = new();
	private readonly Lock _sync = new(); // ponytail: one lock for all logins; per-user locks if login rate ever matters

	/// <inheritdoc />
	public IReadOnlyDictionary<UserRecord, User> Online => _online;

	/// <inheritdoc />
	public Connection? Find(string token)
	{
		return _connections.GetValueOrDefault(token);
	}

	/// <inheritdoc />
	public BanchoConnection? FindSpectating(Connection connection)
	{
		// ponytail: scans every online osu! client; add an index if the online count grows large.
		return _online.Values
			.Select(user => user.Bancho)
			.FirstOrDefault(host => host is not null && host.Spectators.Contains(connection));
	}

	/// <inheritdoc />
	void IUserRegistry.Add(User user)
	{
		_online[user.Record] = user;
	}

	/// <inheritdoc />
	bool IUserRegistry.Remove(User user)
	{
		return _online.TryRemove(new KeyValuePair<UserRecord, User>(user.Record, user));
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
