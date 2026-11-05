using System.Collections;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the active restrictions of one account.</summary>
public sealed class UserRestrictions : IReadOnlyList<UserRestriction>
{
	private readonly User _user;
	private IReadOnlyList<UserRestriction> _items = Array.Empty<UserRestriction>();

	/// <summary>Creates the restriction state for an account.</summary>
	/// <param name="user">The account whose restrictions are held.</param>
	internal UserRestrictions(User user)
	{
		_user = user;
	}

	/// <inheritdoc />
	public int Count
	{
		get
		{
			using var scope = _user.Enter();
			return _items.Count;
		}
	}

	/// <inheritdoc />
	public UserRestriction this[int index]
	{
		get
		{
			using var scope = _user.Enter();
			return _items[index];
		}
	}

	/// <inheritdoc />
	public IEnumerator<UserRestriction> GetEnumerator()
	{
		using var scope = _user.Enter();
		return _items.GetEnumerator();
	}

	/// <inheritdoc />
	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	/// <summary>Adds a restriction that has started to the active restrictions.</summary>
	/// <param name="restriction">The restriction to add.</param>
	internal void Add(UserRestriction restriction)
	{
		using var scope = _user.Enter();
		_items = [.. _items, restriction];
	}

	/// <summary>Removes a restriction from the active restrictions.</summary>
	/// <param name="restriction">The restriction to remove.</param>
	internal void Remove(UserRestriction restriction)
	{
		using var scope = _user.Enter();
		_items = _items.Where(item => !item.Equals(restriction)).ToArray();
	}

	/// <summary>Replaces the active restrictions with the restrictions that have not ended.</summary>
	/// <param name="restrictions">The restrictions that remain active.</param>
	internal void Replace(IEnumerable<UserRestriction> restrictions)
	{
		using var scope = _user.Enter();
		_items = [.. restrictions];
	}

	/// <summary>Lists this account's restrictions, including those that have ended.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The restriction history, oldest first.</returns>
	internal Task<IReadOnlyList<UserRestriction>> ListAsync(CancellationToken cancellationToken)
	{
		return _user.Context.Restrictions.ListAsync(_user.Record, cancellationToken);
	}

	/// <summary>Suspends selected permissions until lifted or their end time.</summary>
	/// <param name="permissions">The permissions to suspend.</param>
	/// <param name="endsAt">When the restriction ends, or <see langword="null" /> to keep it until lifted.</param>
	/// <param name="reason">Why the account is restricted, if known.</param>
	/// <param name="actor">The account imposing the restriction.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and the new restriction, if one was created.</returns>
	internal async Task<(UserOutcome Outcome, UserRestriction? Restriction)> ImposeAsync(
		UserPermissions permissions, DateTimeOffset? endsAt, string? reason, UserRecord actor,
		CancellationToken cancellationToken)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (permissions == UserPermissions.None || (endsAt is { } end && end <= now))
			return (UserOutcome.InvalidRestriction, null);

		var log = new ModerationData
		{
			Action = ModerationAction.Restrict,
			Actor = actor,
			Target = _user.Record,
			Reason = reason,
			Timestamp = now
		};
		var data = new RestrictionData
		{
			User = _user.Record,
			Permissions = permissions,
			StartsAt = now,
			EndsAt = endsAt
		};
		var context = _user.Context;
		var restriction = await context.Restrictions.CreateAsync(data, log, cancellationToken);
		if (context.Registry.Online.GetValueOrDefault(_user.Record) is { } user)
		{
			user.Restrictions.Add(restriction);
			context.Registry.CloseDisallowed(user);
		}

		context.Events.Publish(new UserRestricted(_user.Record, restriction));
		return (UserOutcome.Ok, restriction);
	}

	/// <summary>Ends a restriction now.</summary>
	/// <param name="restriction">The restriction to end.</param>
	/// <param name="reason">Why the restriction is lifted, if known.</param>
	/// <param name="actor">The account lifting the restriction.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of lifting the restriction.</returns>
	internal async Task<UserOutcome> LiftAsync(UserRestriction restriction, string? reason, UserRecord actor,
		CancellationToken cancellationToken)
	{
		if (!restriction.Value.User.Equals(_user.Record)) return UserOutcome.NotFound;

		var stored = await _user.Context.Restrictions.GetAsync(restriction.Id, cancellationToken);
		if (stored is null || !stored.Value.User.Equals(_user.Record)) return UserOutcome.NotFound;

		var now = _user.Context.Clock.GetUtcNow();
		if (stored.Value.EndsAt is { } endsAt && endsAt <= now) return UserOutcome.Ok;

		var log = new ModerationData
		{
			Action = ModerationAction.Lift,
			Actor = actor,
			Target = _user.Record,
			Reason = reason,
			Timestamp = now
		};

		stored.Value.EndsAt = now;
		await _user.Context.Restrictions.UpdateAsync(stored, log, cancellationToken);
		_user.Context.Registry.Online.GetValueOrDefault(_user.Record)
			?.Restrictions.Remove(stored);
		_user.Context.Events.Publish(new UserRestrictionLifted(_user.Record, stored));
		return UserOutcome.Ok;
	}

	/// <summary>Removes a restriction from this account's history.</summary>
	/// <param name="restriction">The restriction to remove.</param>
	/// <param name="reason">Why the restriction is deleted, if known.</param>
	/// <param name="actor">The account deleting the restriction.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the restriction.</returns>
	internal async Task<UserOutcome> DeleteAsync(UserRestriction restriction, string? reason, UserRecord actor,
		CancellationToken cancellationToken)
	{
		if (!restriction.Value.User.Equals(_user.Record)) return UserOutcome.NotFound;

		var stored = await _user.Context.Restrictions.GetAsync(restriction.Id, cancellationToken);
		if (stored is null || !stored.Value.User.Equals(_user.Record)) return UserOutcome.NotFound;

		var now = _user.Context.Clock.GetUtcNow();
		var active = stored.Value.EndsAt is null || stored.Value.EndsAt > now;
		var context = _user.Context;
		await context.Restrictions.DeleteAsync(stored, new ModerationData
		{
			Action = ModerationAction.Delete,
			Actor = actor,
			Target = _user.Record,
			Reason = reason,
			Timestamp = now
		}, cancellationToken);

		if (active && context.Registry.Online.GetValueOrDefault(_user.Record) is { } user)
		{
			user.Restrictions.Remove(stored);
			context.Registry.CloseDisallowed(user);
		}

		context.Events.Publish(new UserRestrictionDeleted(_user.Record, stored));
		return UserOutcome.Ok;
	}
}