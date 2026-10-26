using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Sessions;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Users;

/// <summary>Restricts an account's permissions and lifts its restrictions.</summary>
public sealed class ScopedUserRestrictions
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
		return ImposeAsync(UserPermissions.UserSilence, RestrictionData.Silence, endsAt, reason, cancellationToken);
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
		return ImposeAsync(UserPermissions.UserRestrict, permissions, endsAt, reason, cancellationToken);
	}

	/// <summary>Ends a restriction now.</summary>
	/// <param name="restriction">The restriction to end.</param>
	/// <param name="reason">Why the restriction is lifted, if known.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of lifting the restriction.</returns>
	public async Task<Result<UserOutcome>> LiftAsync(UserRestriction restriction, string? reason = null,
		CancellationToken cancellationToken = default)
	{
		var silenceOnly = RestrictionData.Silence.Allows(restriction.Value.Permissions);
		var needed = silenceOnly ? UserPermissions.UserSilence : UserPermissions.UserRestrict;
		if (_scoped.Guard(needed, false) is { } denied) return denied;
		if (!restriction.Value.User.Equals(_scoped.User.Record)) return UserOutcome.NotFound;

		var now = _scoped.User.Context.Clock.GetUtcNow();
		if (restriction.Value.EndsAt is { } endsAt && endsAt <= now) return UserOutcome.Ok;

		var log = new ModerationData
		{
			Action = ModerationAction.Lift,
			By = _scoped.By.Record,
			Target = _scoped.User.Record,
			Reason = reason,
			Timestamp = now
		};

		restriction.Value.EndsAt = now;
		await _scoped.User.Context.Restrictions.UpdateAsync(restriction, cancellationToken);
		_scoped.User.Context.Registry.Online.GetValueOrDefault(_scoped.User.Record)
			?.RemoveRestriction(restriction);
		await _scoped.User.Context.ModerationLog.AppendAsync(log, cancellationToken);

		_scoped.User.Context.Events.Publish(new UserRestrictionLifted(_scoped.User.Record, restriction));
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
			By = _scoped.By.Record,
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
		var restriction = await context.Restrictions.CreateAsync(data, cancellationToken);
		if (context.Registry.Online.GetValueOrDefault(_scoped.User.Record) is { } user)
		{
			user.AddRestriction(restriction);
			context.Registry.CloseDisallowed(user);
		}

		await context.ModerationLog.AppendAsync(log, cancellationToken);

		context.Events.Publish(new UserRestricted(_scoped.User.Record, restriction));
		return (UserOutcome.Ok, restriction);
	}
}