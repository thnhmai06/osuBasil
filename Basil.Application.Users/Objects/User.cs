using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Domain.Users;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using DomainUser = Basil.Domain.Users.User;

namespace Basil.Application.Users.Objects;

public sealed class User
{
	internal User(UserRecord record, UserContext context)
	{
		State = new DomainUser(record);
		Context = context;
		Profile = new UserProfile(this);
		Credentials = new UserCredentials(this);
		Sessions = new UserSessions(this);
		Grants = new UserGrants(this);
		Silence = new UserSilence(this);
		Relationships = new UserRelationships(this);
	}

	public DomainUser State { get; }

	public UserRecord Record => State.Record;

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

	public IReadOnlySet<Session> this[SessionType type] => Sessions[type];

	public IEnumerable<Session> Connections => Sessions.Connections;

	public StableSession? Stable => Sessions.Osu;

	public IrcSession? Irc => Sessions.Irc;

	public IEnumerable<IrcSession> Ircs => Sessions.Ircs;

	public IEnumerable<TourneySession> Tourneys => Sessions.Tourneys;

	public ScopedUser ScopeTo(Session actor)
	{
		return new ScopedUser(this, actor);
	}

	public UserPrivileges EffectivePrivileges(DateTimeOffset now)
	{
		return Grants.Effective(now);
	}

	internal void Apply(Action<DomainUser> rule)
	{
		rule(State);
		var online = Context.Registry.Online.GetValueOrDefault(Record);
		if (online is not null && !ReferenceEquals(online.Record, Record))
			rule(new DomainUser(online.Record));
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

	internal void Add(Session connection)
	{
		Sessions.Add(connection);
	}

	internal bool Remove(Session connection)
	{
		return Sessions.Remove(connection);
	}

	internal Lock.Scope Enter()
	{
		return State.Enter();
	}
}