using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Authentication;
using Basil.Domain.Game;
using Basil.Domain.Users;
using Session = Basil.Application.Users.Objects.Sessions.Session;
using DomainUser = Basil.Domain.Users.User;

namespace Basil.Application.Users.Objects;

public sealed class User
{
	internal User(UserRecord record, UserContext context)
	{
		State = new DomainUser(record, new Dictionary<GameMode, UserStats>());
		Context = context;
		Profile = new UserProfile(this);
		Credentials = new UserCredentials(this);
		Sessions = new UserSessions(this);
		Silence = new UserSilence(this);
		Relationships = new UserRelationships(this);
	}

	public DomainUser State { get; }

	public UserRecord Record => State.Record;

	public string? AwayMessage => State.AwayMessage;

	public bool PmPrivate => State.PmPrivate;

	public bool IsOnline =>
		Context.Registry.OnlineNow.TryGetValue(Record, out var online) && ReferenceEquals(online, this);

	public UserSilence Silence { get; }

	public UserProfile Profile { get; }

	public UserCredentials Credentials { get; }

	public UserSessions Sessions { get; }

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
		return Record.Value.Privileges;
	}

	internal void Apply(Action<UserData> rule)
	{
		rule(Record.Value);
		var online = Context.Registry.OnlineNow.GetValueOrDefault(Record);
		if (online is not null && !ReferenceEquals(online.Record, Record))
			rule(online.Record.Value);
	}

	internal Task StoreChangeAsync(Action<UserData> change)
	{
		var current = Record.Value;
		var copy = new UserData
		{
			Name = current.Name,
			Country = current.Country,
			Privileges = current.Privileges,
			SilencedEnd = current.SilencedEnd,
			DeletedAt = current.DeletedAt
		};

		change(copy);
		return Context.Users.UpdateAsync(new UserRecord { Id = Record.Id, Value = copy });
	}

	internal async Task<UserActionResult> DeleteAsync()
	{
		if (Record.Value.DeletedAt is not null) return UserActionResult.Ok;

		var now = Context.Clock.GetUtcNow();
		await StoreChangeAsync(data =>
		{
			data.DeletedAt = now;
			data.Privileges = UserPrivileges.None;
		});
		Apply(data =>
		{
			data.DeletedAt = now;
			data.Privileges = UserPrivileges.None;
		});
		Context.Registry.CloseAll(Record, SessionEndReason.Deleted);
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> SetPrivilegesAsync(UserPrivileges privileges)
	{
		await StoreChangeAsync(data => data.Privileges = privileges);
		Apply(data => data.Privileges = privileges);
		if (Context.Registry.OnlineNow.GetValueOrDefault(Record) is { } online)
			Context.Registry.CloseDisallowed(online);

		Context.Events.Publish(new UserPrivilegesChanged(Record, privileges));
		return UserActionResult.Ok;
	}

	internal void Add(Session connection)
	{
		Sessions.Add(connection);
	}

	internal bool Remove(Session connection)
	{
		return Sessions.Remove(connection);
	}
}