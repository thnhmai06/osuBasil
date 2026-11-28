using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Shared.Actors;
using Basil.Domain.Game;
using Basil.Domain.Users;
using DomainUser = Basil.Domain.Users.User;

namespace Basil.Application.Users.Objects;

public sealed class User
{
	internal User(UserRecord record, UserContext context)
	{
		State = new DomainUser(record, new Dictionary<GameMode, UserStats>());
		Context = context;
		Profile = new UserProfile(this);
		Silence = new UserSilence(this);
		Relationships = new UserRelationships(this);
	}

	public DomainUser State { get; }

	public UserRecord Record => State.Record;

	public string? AwayMessage => State.AwayMessage;

	public bool PmPrivate => State.PmPrivate;

	public UserSilence Silence { get; }

	public UserProfile Profile { get; }

	public UserRelationships Relationships { get; }

	internal UserContext Context { get; }

	public ScopedUser ScopeTo(IActor actor)
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
		var online = Context.Registry.Live(Record);
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
		Context.Events.Publish(new UserDeleted(Record));
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> SetPrivilegesAsync(UserPrivileges privileges)
	{
		await StoreChangeAsync(data => data.Privileges = privileges);
		Apply(data => data.Privileges = privileges);
		Context.Events.Publish(new UserPrivilegesChanged(Record, privileges));
		return UserActionResult.Ok;
	}
}