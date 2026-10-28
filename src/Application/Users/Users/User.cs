using Basil.Application.Users.Connections;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Users.Users;

/// <summary>Owns the state changes of one account.</summary>
public sealed class User
{
	private readonly Lock _sync = new();

	/// <summary>Creates the account object for a stored user.</summary>
	/// <param name="record">The account to represent.</param>
	/// <param name="context">The runtime context for account behaviour.</param>
	internal User(UserRecord record, UserContext context)
	{
		Record = record;
		Context = context;
		Profile = new UserProfile(this);
		Credentials = new UserCredentials(this);
		Sessions = new UserSessions(this);
		Grants = new UserGrants(this);
		Restrictions = new UserRestrictions(this);
		Relationships = new UserRelationships(this);
	}

	/// <summary>Gets the account this object represents.</summary>
	public UserRecord Record { get; }

	/// <summary>Gets or sets the away message shown to other users, or <see langword="null" /> when not away.</summary>
	public string? AwayMessage => Profile.AwayMessage;

	/// <summary>Gets a value that indicates whether the user accepts private messages only from their friends.</summary>
	public bool PmPrivate => Profile.PmPrivate;

	/// <summary>Gets a value that indicates whether this is the user's current online instance.</summary>
	public bool IsOnline =>
		Context.Registry.Online.TryGetValue(Record, out var online) && ReferenceEquals(online, this);

	/// <summary>Gets the user's restrictions that had not ended when they were last loaded or changed.</summary>
	public UserRestrictions Restrictions { get; }

	/// <summary>Gets the profile data and settings of this account.</summary>
	public UserProfile Profile { get; }

	/// <summary>Gets the password of this account.</summary>
	public UserCredentials Credentials { get; }

	/// <summary>Gets the live connections of this account.</summary>
	public UserSessions Sessions { get; }

	/// <summary>Gets the permissions granted to this account.</summary>
	public UserGrants Grants { get; }

	/// <summary>Gets the friends and blocks this account has set.</summary>
	public UserRelationships Relationships { get; }

	/// <summary>The runtime context for account behaviour.</summary>
	internal UserContext Context { get; }

	/// <summary>Gets the open connections of one kind.</summary>
	/// <param name="type">The kind of connection.</param>
	public IReadOnlySet<Connection> this[ConnectionType type] => Sessions[type];

	/// <summary>Gets every open connection of the user.</summary>
	public IEnumerable<Connection> Connections => Sessions.Connections;

	/// <summary>Gets the user's osu! game client connection, if any.</summary>
	public BanchoConnection? Bancho => Sessions.Osu;

	/// <summary>Gets one of the user's IRC connections, if any.</summary>
	public IrcConnection? Irc => Sessions.Irc;

	/// <summary>Gets the user's IRC connections.</summary>
	public IEnumerable<IrcConnection> Ircs => Sessions.Ircs;

	/// <summary>Gets the server's connection for BasilBot, if any.</summary>
	public BotConnection? Bot => Sessions.Bot;

	/// <summary>Gets the user's osu!tourney connections.</summary>
	public IEnumerable<TourneyConnection> Tourneys => Sessions.Tourneys;

	/// <summary>Gets the user's HTTP API connections.</summary>
	public IEnumerable<ApiConnection> Apis => Sessions.Apis;

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
		return Grants.Effective(now);
	}

	/// <summary>Adds a restriction that has started to the running ones.</summary>
	/// <param name="restriction">The restriction.</param>
	internal void AddRestriction(UserRestriction restriction)
	{
		Restrictions.Add(restriction);
	}

	/// <summary>Removes a restriction from the running ones.</summary>
	/// <param name="restriction">The restriction.</param>
	internal void RemoveRestriction(UserRestriction restriction)
	{
		Restrictions.Remove(restriction);
	}

	/// <summary>Replaces the running restrictions with the ones the user has now, as at login.</summary>
	/// <param name="restrictions">The restrictions that have not ended.</param>
	internal void ReplaceRestrictions(IEnumerable<UserRestriction> restrictions)
	{
		Restrictions.Replace(restrictions);
	}

	/// <summary>Changes the account data, keeping the online user's copy of the record in step.</summary>
	/// <param name="change">The change to apply to the data.</param>
	internal void Change(Action<UserData> change)
	{
		change(Record.Value);
		var online = Context.Registry.Online.GetValueOrDefault(Record);
		if (online is not null && !ReferenceEquals(online.Record, Record))
			change(online.Record.Value);
	}

	/// <summary>Adds a connection.</summary>
	internal void Add(Connection connection)
	{
		Sessions.Add(connection);
	}

	/// <summary>Removes a connection.</summary>
	/// <returns><see langword="true" /> if the connection was held and has been removed.</returns>
	internal bool Remove(Connection connection)
	{
		return Sessions.Remove(connection);
	}

	/// <summary>Enters the scope that synchronizes this account's parts.</summary>
	/// <returns>A scope that must be disposed when the state change is complete.</returns>
	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}
}
