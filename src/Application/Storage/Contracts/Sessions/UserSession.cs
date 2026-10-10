using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Chat;
using Basil.Domain.Users;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Contracts.Sessions;

/// <summary>One period during which a user is online, across all the clients they are connected with.</summary>
public sealed class UserSession
{
	private readonly ConcurrentDictionary<ConnectionType, ConcurrentSet<Connection>> _connections = new();

	private ImmutableList<Restriction> _restrictions = [];

	internal UserSession(User user)
	{
		User = user;
		PmChannel = new PmChannelSession(this);
	}

	/// <summary>Gets the user who is online.</summary>
	public User User { get; }

	/// <summary>Gets the channel that receives the user's private messages.</summary>
	public PmChannelSession PmChannel { get; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; internal set; }

	/// <summary>Gets a value that indicates whether the user accepts private messages only from their friends.</summary>
	public bool PmPrivate { get; internal set; }

	/// <summary>Gets the user's restrictions that had not ended when they were last loaded or changed.</summary>
	public IReadOnlyList<Restriction> Restrictions => _restrictions;

	/// <summary>Replaces the user's restrictions in one step, so concurrent changes never lose each other.</summary>
	/// <param name="change">Computes the new restrictions from the current ones.</param>
	internal void ChangeRestrictions(Func<ImmutableList<Restriction>, ImmutableList<Restriction>> change)
	{
		ImmutableInterlocked.Update(ref _restrictions, change);
	}

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

	/// <summary>Gets the user's osu!tourney connections.</summary>
	public IEnumerable<TourneyConnection> Tourneys => this[ConnectionType.Tourney].Cast<TourneyConnection>();

	/// <summary>Gets the user's HTTP API connections.</summary>
	public IEnumerable<ApiConnection> Apis => this[ConnectionType.Api].Cast<ApiConnection>();

	/// <summary>Adds a connection.</summary>
	internal void Add(Connection connection)
	{
		var set = _connections.GetOrAdd(connection.Type, _ => []);
		set.Add(connection);
	}

	/// <summary>Removes a connection.</summary>
	/// <returns><see langword="true" /> if the connection was held and has been removed.</returns>
	internal bool Remove(Connection connection)
	{
		return _connections.TryGetValue(connection.Type, out var set) && set.Remove(connection);
	}
}