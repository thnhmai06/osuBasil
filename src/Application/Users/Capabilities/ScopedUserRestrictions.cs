using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Restricts an account's permissions and lifts its restrictions.</summary>
public readonly struct ScopedUserRestrictions
{
	private readonly ScopedUser _scoped;

	/// <summary>Creates restriction operations scoped to an account.</summary>
	/// <param name="scoped">The account scope.</param>
	internal ScopedUserRestrictions(ScopedUser scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Suspends the permissions covered by a silence until a given time.</summary>
	/// <param name="endsAt">When the silence ends.</param>
	/// <param name="reason">Why the account is silenced, if known.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and the new restriction, if one was created.</returns>
	public Task<(Result<UserOutcome> Result, UserRestriction? Restriction)> SilenceAsync(DateTimeOffset endsAt,
		string? reason = null, CancellationToken cancellationToken = default)
	{
		return ImposeAsync(UserPermissions.UserManageSilence, RestrictionData.Silence, endsAt, reason,
			cancellationToken);
	}

	/// <summary>Suspends selected permissions until lifted or their end time.</summary>
	/// <param name="permissions">The permissions to suspend.</param>
	/// <param name="endsAt">When the restriction ends, or <see langword="null" /> to keep it until lifted.</param>
	/// <param name="reason">Why the account is restricted, if known.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and the new restriction, if one was created.</returns>
	public Task<(Result<UserOutcome> Result, UserRestriction? Restriction)> RestrictAsync(UserPermissions permissions,
		DateTimeOffset? endsAt, string? reason = null, CancellationToken cancellationToken = default)
	{
		return ImposeAsync(UserPermissions.UserManageRestrict, permissions, endsAt, reason, cancellationToken);
	}

	/// <summary>Lists this account's restrictions, including those that have ended.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The restriction history, oldest first.</returns>
	public Task<IReadOnlyList<UserRestriction>> ListAsync(CancellationToken cancellationToken = default)
	{
		return _scoped.User.Context.Restrictions.ListAsync(_scoped.User.Record, cancellationToken);
	}

	/// <summary>Ends a restriction now.</summary>
	/// <param name="restriction">The restriction to end.</param>
	/// <param name="reason">Why the restriction is lifted, if known.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of lifting the restriction.</returns>
	public async Task<Result<UserOutcome>> LiftAsync(UserRestriction restriction, string? reason = null,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Guard(UserPermissions.UserManageRestrict, false) is { } denied) return denied;
		if (!restriction.Value.User.Equals(_scoped.User.Record)) return UserOutcome.NotFound;

		var stored = await _scoped.User.Context.Restrictions.GetAsync(restriction.Id, cancellationToken);
		if (stored is null || !stored.Value.User.Equals(_scoped.User.Record)) return UserOutcome.NotFound;

		var now = _scoped.User.Context.Clock.GetUtcNow();
		if (stored.Value.EndsAt is { } endsAt && endsAt <= now) return UserOutcome.Ok;

		var log = new ModerationData
		{
			Action = ModerationAction.Lift,
			Actor = _scoped.Actor.Record,
			Target = _scoped.User.Record,
			Reason = reason,
			Timestamp = now
		};

		stored.Value.EndsAt = now;
		await _scoped.User.Context.Restrictions.UpdateAsync(stored, log, cancellationToken);
		_scoped.User.Context.Registry.Online.GetValueOrDefault(_scoped.User.Record)
			?.Restrictions.Remove(stored);
		_scoped.User.Context.Events.Publish(new UserRestrictionLifted(_scoped.User.Record, stored));
		return UserOutcome.Ok;
	}

	/// <summary>Removes a restriction from this account's history.</summary>
	/// <param name="restriction">The restriction to remove.</param>
	/// <param name="reason">Why the restriction is deleted, if known.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of deleting the restriction.</returns>
	public async Task<Result<UserOutcome>> DeleteAsync(UserRestriction restriction, string? reason = null,
		CancellationToken cancellationToken = default)
	{
		if (_scoped.Guard(UserPermissions.UserManage, false) is { } denied) return denied;
		if (!restriction.Value.User.Equals(_scoped.User.Record)) return UserOutcome.NotFound;

		var stored = await _scoped.User.Context.Restrictions.GetAsync(restriction.Id, cancellationToken);
		if (stored is null || !stored.Value.User.Equals(_scoped.User.Record)) return UserOutcome.NotFound;

		var now = _scoped.User.Context.Clock.GetUtcNow();
		var active = stored.Value.EndsAt is null || stored.Value.EndsAt > now;
		var context = _scoped.User.Context;
		await context.Restrictions.DeleteAsync(stored, new ModerationData
		{
			Action = ModerationAction.Delete,
			Actor = _scoped.Actor.Record,
			Target = _scoped.User.Record,
			Reason = reason,
			Timestamp = now
		}, cancellationToken);

		if (active && context.Registry.Online.GetValueOrDefault(_scoped.User.Record) is { } user)
		{
			user.Restrictions.Remove(stored);
			context.Registry.CloseDisallowed(user);
		}

		context.Events.Publish(new UserRestrictionDeleted(_scoped.User.Record, stored));
		return UserOutcome.Ok;
	}

	private async Task<(Result<UserOutcome> Result, UserRestriction? Restriction)> ImposeAsync(UserPermissions needed,
		UserPermissions permissions, DateTimeOffset? endsAt, string? reason, CancellationToken cancellationToken)
	{
		if (_scoped.Guard(needed, false) is { } denied) return (denied, null);

		var now = _scoped.User.Context.Clock.GetUtcNow();
		if (permissions == UserPermissions.None || (endsAt is { } end && end <= now))
			return (UserOutcome.InvalidRestriction, null);

		var log = new ModerationData
		{
			Action = ModerationAction.Restrict,
			Actor = _scoped.Actor.Record,
			Target = _scoped.User.Record,
			Reason = reason,
			Timestamp = now
		};
		var data = new RestrictionData
		{
			User = _scoped.User.Record,
			Permissions = permissions,
			StartsAt = now,
			EndsAt = endsAt
		};
		var context = _scoped.User.Context;
		var restriction = await context.Restrictions.CreateAsync(data, log, cancellationToken);
		if (context.Registry.Online.GetValueOrDefault(_scoped.User.Record) is { } user)
		{
			user.Restrictions.Add(restriction);
			context.Registry.CloseDisallowed(user);
		}

		context.Events.Publish(new UserRestricted(_scoped.User.Record, restriction));
		return (UserOutcome.Ok, restriction);
	}
}