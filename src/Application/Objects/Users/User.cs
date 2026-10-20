using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Objects.Chat;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Common;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Objects.Users;

/// <summary>Owns the state changes of one account.</summary>
public sealed class User
{
	private readonly ConcurrentDictionary<ConnectionType, ConcurrentSet<Connection>> _connections = new();
	private ImmutableList<Restriction> _restrictions = [];

	/// <summary>Gets the account this object represents.</summary>
	public UserRecord Record { get; }

	/// <summary>Gets the channel that receives the user's private messages.</summary>
	public PmChannel PmChannel { get; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; internal set; }

	/// <summary>Gets a value that indicates whether the user accepts private messages only from their friends.</summary>
	public bool PmPrivate { get; internal set; }

	/// <summary>Gets a value that indicates whether this is the user's current online instance.</summary>
	public bool IsOnline =>
		Context.Registry.Online.TryGetValue(Record, out var online) && ReferenceEquals(online, this);

	/// <summary>Gets the user's restrictions that had not ended when they were last loaded or changed.</summary>
	public IReadOnlyList<Restriction> Restrictions => _restrictions;

	/// <summary>The runtime context for account behaviour.</summary>
	internal UserContext Context { get; }

	/// <summary>Creates the account object for a stored user.</summary>
	/// <param name="record">The account to represent.</param>
	/// <param name="context">The runtime context for account behaviour.</param>
	internal User(UserRecord record, UserContext context)
	{
		Record = record;
		Context = context;
		PmChannel = new PmChannel(this, context.Chat);
	}

	/// <summary>Scopes this account's behaviour to a connection and its authority.</summary>
	/// <param name="by">The connection acting on the account.</param>
	/// <returns>A short-lived scope for this account and connection.</returns>
	public ScopedUser ScopeTo(Connection by)
	{
		return new ScopedUser(this, by);
	}

	/// <summary>Gets the user's permissions that are in effect at a moment.</summary>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	/// <returns>The granted permissions not suspended by an active restriction.</returns>
	public Permissions EffectivePermissions(DateTimeOffset now)
	{
		return Record.Value.Permissions.Effective(Restrictions, now);
	}

	/// <summary>Checks whether the user's granted permissions are in effect for an action.</summary>
	/// <param name="required">The permissions the action needs.</param>
	/// <param name="now">The moment to evaluate restrictions at.</param>
	/// <returns>Whether the permissions are in effect, not granted, or suspended by a restriction.</returns>
	public Access Check(Permissions required, DateTimeOffset now)
	{
		if (!Record.Value.Permissions.Allows(required)) return Access.NotGranted;
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

	/// <summary>Gets a value that indicates whether this user blocks another.</summary>
	/// <param name="who">The user to check.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when this user blocks <paramref name="who" />.</returns>
	public async ValueTask<bool> HasBlockedAsync(UserRecord who, CancellationToken ct = default)
	{
		if (BasilBot.Is(Record)) return false;

		var relationships = await Context.Relationships.ListAsync(Record, ct);
		return relationships.Any(relationship => relationship.Target.Equals(who) &&
		                                         relationship.Type == RelationshipType.Block);
	}

	/// <summary>Changes the account data and keeps its online user in sync.</summary>
	/// <param name="change">The change to apply to the account.</param>
	internal void Apply(Action<UserData> change)
	{
		change(Record.Value);
		var online = Context.Registry.Online.GetValueOrDefault(Record);
		if (online is not null && !ReferenceEquals(online.Record, Record))
			change(online.Record.Value);
	}

	/// <summary>Replaces the user's restrictions in one step, so concurrent changes never lose each other.</summary>
	/// <param name="change">Computes the new restrictions from the current ones.</param>
	internal void ChangeRestrictions(Func<ImmutableList<Restriction>, ImmutableList<Restriction>> change)
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