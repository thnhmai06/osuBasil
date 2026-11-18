using System.Collections.Concurrent;
using Basil.Application.Users.Objects.Connections;

namespace Basil.Host.Irc.Session;

internal sealed class IrcClients
{
	private readonly ConcurrentDictionary<string, IrcSession> _sessions = new(StringComparer.Ordinal);

	public IEnumerable<IrcSession> All => _sessions.Select(pair => pair.Value);

	public bool Reserve(string key, IrcSession session)
	{
		return _sessions.TryAdd(key, session);
	}

	public void Release(string key, IrcSession session)
	{
		_sessions.TryRemove(new KeyValuePair<string, IrcSession>(key, session));
	}

	public IrcSession? Find(Connection connection)
	{
		// The socket claims the key before its login returns, so the connection may not be set yet; then the open
		// connection is the one that login is opening. A closed one belongs to an earlier socket with the same key.
		return connection is IrcConnection && _sessions.TryGetValue(connection.Token, out var session) &&
		       (ReferenceEquals(session.Connection, connection) || (session.Connection is null && connection.IsOpen))
			? session
			: null;
	}

	public IEnumerable<IrcSession> Of(IEnumerable<Connection> connections)
	{
		return connections.Select(Find).OfType<IrcSession>();
	}
}