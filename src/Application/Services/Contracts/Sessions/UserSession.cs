using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Services.Contracts.Chat;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>One period during which a user is online, across all the clients they are connected with.</summary>
public sealed class UserSession
{
	private readonly ConcurrentDictionary<ConnectionType, ConcurrentSet<Connection>> _connections = new();

	private ImmutableList<RestrictionRecord> _restrictions = [];

	internal UserSession(UserRecord user, SessionContext context)
	{
		User = user;
		Context = context;
		PmChannel = new PmChannelSession(this, context.Chat);
	}

	/// <summary>The runtime dependencies used by this session.</summary>
	internal SessionContext Context { get; }

	/// <summary>Gets the user who is online.</summary>
	public UserRecord User { get; }

	/// <summary>Gets the channel that receives the user's private messages.</summary>
	public PmChannelSession PmChannel { get; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; internal set; }

	/// <summary>Gets a value that indicates whether the user accepts private messages only from their friends.</summary>
	public bool PmPrivate { get; internal set; }

	/// <summary>Scopes this session's behaviour to a connection.</summary>
	/// <param name="by">The connection acting on this session.</param>
	/// <returns>A short-lived scope for this session and connection.</returns>
	public ScopedUserSession ScopeTo(Connection by)
	{
		return new ScopedUserSession(this, by);
	}

	/// <summary>Gets the user's restrictions that had not ended when they were last loaded or changed.</summary>
	public IReadOnlyList<RestrictionRecord> Restrictions => _restrictions;

	/// <summary>Gets the user's permissions that are in effect at a moment.</summary>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	/// <returns>The granted permissions not suspended by an active restriction.</returns>
	public Permissions EffectivePermissions(DateTimeOffset now)
	{
		return User.Value.Permissions.Effective(Restrictions, now);
	}

	/// <summary>Checks whether the user's granted permissions are in effect for an action.</summary>
	/// <param name="required">The permissions the action needs.</param>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	/// <returns>Whether the permissions are in effect, not granted, or suspended by a restriction.</returns>
	public Access Check(Permissions required, DateTimeOffset now)
	{
		if (!User.Value.Permissions.Allows(required)) return Access.NotGranted;
		return EffectivePermissions(now).Allows(required) ? Access.Allowed : Access.Suspended;
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

	/// <summary>Gets the server's connection for BasilBot, if any.</summary>
	public BotConnection? Bot => (BotConnection?)this[ConnectionType.Bot].SingleOrDefault();

	/// <summary>Gets the user's osu!tourney connections.</summary>
	public IEnumerable<TourneyConnection> Tourneys => this[ConnectionType.Tourney].Cast<TourneyConnection>();

	/// <summary>Gets the user's HTTP API connections.</summary>
	public IEnumerable<ApiConnection> Apis => this[ConnectionType.Api].Cast<ApiConnection>();

	/// <summary>Replaces the user's restrictions in one step, so concurrent changes never lose each other.</summary>
	/// <param name="change">Computes the new restrictions from the current ones.</param>
	internal void ChangeRestrictions(Func<ImmutableList<RestrictionRecord>, ImmutableList<RestrictionRecord>> change)
	{
		ImmutableInterlocked.Update(ref _restrictions, change);
	}

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