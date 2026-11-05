using System.Collections.Concurrent;
using Basil.Application.Users.Objects.Connections;

namespace Basil.Host.Irc;

/// <summary>Holds the open IRC sockets, found by the key their connection was opened with.</summary>
internal sealed class IrcClients
{
	private readonly ConcurrentDictionary<string, IrcSession> _sessions = new(StringComparer.Ordinal);

	/// <summary>Gets the open sockets.</summary>
	public IEnumerable<IrcSession> All => _sessions.Values;

	/// <summary>Claims a key for a socket before its login, so no event for the new connection is lost.</summary>
	/// <param name="key">The IRC key.</param>
	/// <param name="session">The socket.</param>
	/// <returns><see langword="false" /> when another socket holds the key.</returns>
	public bool Reserve(string key, IrcSession session)
	{
		return _sessions.TryAdd(key, session);
	}

	/// <summary>Frees a key.</summary>
	/// <param name="key">The IRC key.</param>
	/// <param name="session">The socket that holds it.</param>
	public void Release(string key, IrcSession session)
	{
		_sessions.TryRemove(new KeyValuePair<string, IrcSession>(key, session));
	}

	/// <summary>Finds the socket of a connection.</summary>
	/// <param name="connection">The connection.</param>
	/// <returns>The socket, or <see langword="null" /> when the connection is not an IRC client's.</returns>
	public IrcSession? Find(Connection connection)
	{
		// The socket claims the key before its login returns, so the connection may not be set yet; then the open
		// connection is the one that login is opening. A closed one belongs to an earlier socket with the same key.
		return connection is IrcConnection && _sessions.TryGetValue(connection.Token, out var session) &&
		       (ReferenceEquals(session.Connection, connection) || (session.Connection is null && connection.IsOpen))
			? session
			: null;
	}

	/// <summary>Finds the sockets of the IRC clients among some connections.</summary>
	/// <param name="connections">The connections, for example the members of a channel.</param>
	/// <returns>The sockets of those that are IRC clients.</returns>
	public IEnumerable<IrcSession> Of(IEnumerable<Connection> connections)
	{
		return connections.Select(Find).OfType<IrcSession>();
	}
}