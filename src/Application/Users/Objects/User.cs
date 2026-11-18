using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Authentication;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class User
{
	private readonly Lock _sync = new();

	internal User(UserRecord record, UserContext context)
	{
		Record = record;
		Context = context;
		Profile = new UserProfile(this);
		Credentials = new UserCredentials(this);
		Sessions = new UserSessions(this);
		Grants = new UserGrants(this);
		Silence = new UserSilence(this);
		Relationships = new UserRelationships(this);
	}

	public UserRecord Record { get; }

	public string? AwayMessage => Profile.AwayMessage;

	public bool PmPrivate => Profile.PmPrivate;

	public bool IsOnline =>
		Context.Registry.Online.TryGetValue(Record, out var online) && ReferenceEquals(online, this);

	public UserSilence Silence { get; }

	public UserProfile Profile { get; }

	public UserCredentials Credentials { get; }

	public UserSessions Sessions { get; }

	public UserGrants Grants { get; }

	public UserRelationships Relationships { get; }

	internal UserContext Context { get; }

	public IReadOnlySet<Connection> this[SessionType type] => Sessions[type];

	public IEnumerable<Connection> Connections => Sessions.Connections;

	public StableConnection? Stable => Sessions.Osu;

	public IrcConnection? Irc => Sessions.Irc;

	public IEnumerable<IrcConnection> Ircs => Sessions.Ircs;

	public IEnumerable<TourneyConnection> Tourneys => Sessions.Tourneys;

	public ScopedUser ScopeTo(Connection actor)
	{
		return new ScopedUser(this, actor);
	}

	public UserPrivileges EffectivePrivileges(DateTimeOffset now)
	{
		return Grants.Effective(now);
	}

	internal void Change(Action<UserData> change)
	{
		change(Record.Value);
		var online = Context.Registry.Online.GetValueOrDefault(Record);
		if (online is not null && !ReferenceEquals(online.Record, Record))
			change(online.Record.Value);
	}

	internal Task StoreChangeAsync(Action<UserData> change)
	{
		UserData copy;
		using (Enter())
		{
			var current = Record.Value;
			copy = new UserData
			{
				Name = current.Name,
				Country = current.Country,
				Privileges = current.Privileges,
				SilencedUntil = current.SilencedUntil,
				DeletedAt = current.DeletedAt
			};
		}

		change(copy);
		return Context.Users.UpdateAsync(new UserRecord { Id = Record.Id, Value = copy });
	}

	internal async Task<UserOutcome> DeleteAsync()
	{
		if (Record.Value.DeletedAt is not null) return UserOutcome.Ok;

		var now = Context.Clock.GetUtcNow();
		await StoreChangeAsync(data =>
		{
			data.DeletedAt = now;
			data.Privileges = UserPrivileges.None;
		});
		Grants.MarkDeleted(now);
		Context.Registry.CloseAll(Record, SessionEndReason.Deleted);
		return UserOutcome.Ok;
	}

	internal void Add(Connection connection)
	{
		Sessions.Add(connection);
	}

	internal bool Remove(Connection connection)
	{
		return Sessions.Remove(connection);
	}

	internal Lock.Scope Enter()
	{
		return _sync.EnterScope();
	}
}