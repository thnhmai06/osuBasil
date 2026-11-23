using System.Collections.Concurrent;
using Basil.Application.Shared.Actors;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Registry;

/// <summary>Hands out the live account objects: the one shared <see cref="User"/> of an account that is kept, or a new one.</summary>
public sealed class UserRegistry
{
	private readonly ConcurrentDictionary<UserRecord, User> _kept = new();

	internal UserRegistry(UserContext context)
	{
		Context = context;
	}

	internal UserContext Context { get; }

	/// <summary>The account object of <paramref name="record"/>: the kept one while its account is kept, otherwise a new one.</summary>
	public User Of(UserRecord record)
	{
		return Live(record) ?? new User(record, Context);
	}

	/// <summary>Keeps <paramref name="user"/> as the shared object of its account, so every change reaches whoever holds it.</summary>
	public void Keep(User user)
	{
		_kept[user.Record] = user;
	}

	/// <summary>Stops keeping <paramref name="user"/>; returns false when another object had taken its place.</summary>
	public bool Release(User user)
	{
		return _kept.TryRemove(new KeyValuePair<UserRecord, User>(user.Record, user));
	}

	public ScopedUser ScopeTo(IActor actor, int targetId)
	{
		return new ScopedUser(this, targetId, actor);
	}

	internal User? Live(UserRecord record)
	{
		return _kept.GetValueOrDefault(record);
	}
}