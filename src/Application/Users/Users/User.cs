using System.Collections.Concurrent;
using System.Collections.Immutable;
using Basil.Application.Users.Connections;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>Owns the state changes of one account.</summary>
public sealed class User
{
	private readonly ConcurrentDictionary<ConnectionType, ConcurrentSet<Connection>> _connections = new();
	private ImmutableList<UserRestriction> _restrictions = [];

	/// <summary>Creates the account object for a stored user.</summary>
	/// <param name="record">The account to represent.</param>
	/// <param name="context">The runtime context for account behaviour.</param>
	internal User(UserRecord record, UserContext context)
	{
		Record = record;
		Context = context;
	}

	/// <summary>Gets the account this object represents.</summary>
	public UserRecord Record { get; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage { get; internal set; }

	/// <summary>Gets a value that indicates whether the user accepts private messages only from their friends.</summary>
	public bool PmPrivate { get; internal set; }

	/// <summary>Gets a value that indicates whether this is the user's current online instance.</summary>
	public bool IsOnline =>
		Context.Registry.Online.TryGetValue(Record, out var online) && ReferenceEquals(online, this);

	/// <summary>Gets the user's restrictions that had not ended when they were last loaded or changed.</summary>
	public IReadOnlyList<UserRestriction> Restrictions => _restrictions;

	/// <summary>The runtime context for account behaviour.</summary>
	internal UserContext Context { get; }

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
	public UserPermissions EffectivePermissions(DateTimeOffset now)
	{
		return Record.Value.Permissions.Effective(Restrictions, now);
	}

	/// <summary>Replaces the permissions granted to the account and keeps its online user in sync.</summary>
	/// <param name="permissions">The permissions to grant.</param>
	internal void ReplacePermissions(UserPermissions permissions)
	{
		Change(data => data.Permissions = permissions);
	}

	/// <summary>Marks the account as deleted at a moment, taking all its permissions away.</summary>
	/// <param name="at">When the account was deleted.</param>
	internal void MarkDeleted(DateTimeOffset at)
	{
		Change(data =>
		{
			data.DeletedAt = at;
			data.Permissions = UserPermissions.None;
		});
	}

	/// <summary>Adds a restriction that has started to the running ones.</summary>
	/// <param name="restriction">The restriction.</param>
	internal void AddRestriction(UserRestriction restriction)
	{
		ImmutableInterlocked.Update(ref _restrictions, running => running.Add(restriction));
	}

	/// <summary>Removes a restriction from the running ones.</summary>
	/// <param name="restriction">The restriction.</param>
	internal void RemoveRestriction(UserRestriction restriction)
	{
		ImmutableInterlocked.Update(ref _restrictions, running => running.RemoveAll(restriction.Equals));
	}

	/// <summary>Replaces the running restrictions with the ones the user has now, as at login.</summary>
	/// <param name="restrictions">The restrictions that have not ended.</param>
	internal void ReplaceRestrictions(IEnumerable<UserRestriction> restrictions)
	{
		Interlocked.Exchange(ref _restrictions, [.. restrictions]);
	}

	private void Change(Action<UserData> change)
	{
		change(Record.Value);
		var online = Context.Registry.Online.GetValueOrDefault(Record);
		if (online is not null && !ReferenceEquals(online.Record, Record))
			change(online.Record.Value);
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