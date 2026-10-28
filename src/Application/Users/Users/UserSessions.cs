using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Common;

namespace Basil.Application.Users.Users;

/// <summary>Owns the live connections of one online account.</summary>
public sealed class UserSessions
{
	private readonly User _user;
	private readonly ConcurrentDictionary<ConnectionType, ConcurrentSet<Connection>> _connections = new();

	/// <summary>Creates the live connection state for an account.</summary>
	/// <param name="user">The account whose connections are held.</param>
	internal UserSessions(User user)
	{
		_user = user;
	}

	/// <summary>Gets the open connections of one kind.</summary>
	/// <param name="type">The kind of connection.</param>
	public IReadOnlySet<Connection> this[ConnectionType type] =>
		_connections.TryGetValue(type, out var set) ? set : ImmutableHashSet<Connection>.Empty;

	/// <summary>Gets every open connection of the user.</summary>
	public IEnumerable<Connection> Connections => _connections.Values.SelectMany(set => set);

	/// <summary>Gets the user's osu! game client connection, if any.</summary>
	public BanchoConnection? Osu => (BanchoConnection?)this[ConnectionType.Osu].SingleOrDefault();

	/// <summary>Gets one of the user's IRC connections, if any.</summary>
	public IrcConnection? Irc => Ircs.FirstOrDefault();

	/// <summary>Gets the user's IRC connections.</summary>
	public IEnumerable<IrcConnection> Ircs => this[ConnectionType.Irc].Cast<IrcConnection>();

	/// <summary>Gets the server's connection for BasilBot, if any.</summary>
	public BotConnection? Bot => (BotConnection?)this[ConnectionType.Bot].SingleOrDefault();

	/// <summary>Gets the user's osu!tourney connections.</summary>
	public IEnumerable<TourneyConnection> Tourneys => this[ConnectionType.Tourney].Cast<TourneyConnection>();

	/// <summary>Gets the user's HTTP API connections.</summary>
	public IEnumerable<ApiConnection> Apis => this[ConnectionType.Api].Cast<ApiConnection>();

	/// <summary>Adds a connection to the account.</summary>
	/// <param name="connection">The connection to add.</param>
	internal void Add(Connection connection)
	{
		using var scope = _user.Enter();
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		set.Add(connection);
	}

	/// <summary>Removes a connection from the account.</summary>
	/// <param name="connection">The connection to remove.</param>
	/// <returns><see langword="true" /> if the connection was held and has been removed.</returns>
	internal bool Remove(Connection connection)
	{
		using var scope = _user.Enter();
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}
}
