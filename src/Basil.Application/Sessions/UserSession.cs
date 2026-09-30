using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Sessions;

/// <summary>One period during which a user is online, across all the clients they are connected with.</summary>
public sealed class UserSession
{
	private readonly ConcurrentDictionary<ConnectionType, ConcurrentSet<Connection>> _connections = new();

	internal UserSession(User user)
	{
		User = user;
	}

	/// <summary>Gets the user who is online.</summary>
	public User User { get; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; set; }

	/// <summary>Gets the open connections of one kind.</summary>
	/// <param name="type">The kind of connection.</param>
	public IReadOnlySet<Connection> this[ConnectionType type] =>
		_connections.TryGetValue(type, out var set) ? set : ImmutableHashSet<Connection>.Empty;

	/// <summary>Gets every open connection of the user.</summary>
	public IEnumerable<Connection> Connections => _connections.Values.SelectMany(set => set);

	/// <summary>Gets the user's osu! game client connection, if any.</summary>
	public BanchoConnection? Bancho => (BanchoConnection?)this[ConnectionType.Bancho].SingleOrDefault();

	/// <summary>Gets the user's IRC connection, if any.</summary>
	public IrcConnection? Irc => (IrcConnection?)this[ConnectionType.Irc].SingleOrDefault();

	/// <summary>Gets the user's bot connection, if any.</summary>
	public BotConnection? Bot => (BotConnection?)this[ConnectionType.Bot].SingleOrDefault();

	/// <summary>Gets the user's osu!tourney connections.</summary>
	public IEnumerable<TourneyConnection> Tourneys => this[ConnectionType.Tourney].Cast<TourneyConnection>();

	/// <summary>Adds a connection; a kind that allows only one connection accepts it only when none is held.</summary>
	/// <returns><see langword="true" /> if the connection was added.</returns>
	internal bool Add(Connection connection)
	{
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		if (!connection.Type.AllowsMany() && set.Count > 0) return false;
		return set.Add(connection);
	}

	/// <summary>Removes a connection.</summary>
	/// <returns><see langword="true" /> if the connection was held and has been removed.</returns>
	internal bool Remove(Connection connection)
	{
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}
}