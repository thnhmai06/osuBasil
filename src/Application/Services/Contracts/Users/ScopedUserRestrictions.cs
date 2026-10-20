using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Permissions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Users;

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
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and the new restriction, if one was created.</returns>
	public Task<(UserResult Result, Restriction? Restriction)> SilenceAsync(DateTimeOffset endsAt,
		string? reason = null, CancellationToken ct = default)
	{
		return ImposeAsync(Permissions.UserSilence, RestrictionData.Silence, endsAt, reason, ct);
	}

	/// <summary>Suspends selected permissions until lifted or their end time.</summary>
	/// <param name="permissions">The permissions to suspend.</param>
	/// <param name="endsAt">When the restriction ends, or <see langword="null" /> to keep it until lifted.</param>
	/// <param name="reason">Why the account is restricted, if known.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and the new restriction, if one was created.</returns>
	public Task<(UserResult Result, Restriction? Restriction)> RestrictAsync(Permissions permissions,
		DateTimeOffset? endsAt, string? reason = null, CancellationToken ct = default)
	{
		return ImposeAsync(Permissions.UserRestrict, permissions, endsAt, reason, ct);
	}

	/// <summary>Ends a restriction now.</summary>
	/// <param name="restriction">The restriction to end.</param>
	/// <param name="reason">Why the restriction is lifted, if known.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome of lifting the restriction.</returns>
	public async Task<UserResult> LiftAsync(Restriction restriction, string? reason = null,
		CancellationToken ct = default)
	{
		var silenceOnly = RestrictionData.Silence.Allows(restriction.Value.Permissions);
		var needed = silenceOnly ? Permissions.UserSilence : Permissions.UserRestrict;
		if (_scoped.Guard(needed, false) is { } denied) return denied;
		if (!restriction.Value.User.Equals(_scoped.User.Record)) return UserResult.NotAuthorized;

		var now = _scoped.User.Context.Clock.GetUtcNow();
		if (restriction.Value.EndsAt is { } endsAt && endsAt <= now) return UserResult.Ok;

		var log = new ModerationData
		{
			Action = ModerationAction.Lift,
			By = _scoped.By.User,
			Target = _scoped.User.Record,
			Reason = reason,
			Timestamp = now
		};

		restriction.Value.EndsAt = now;
		await _scoped.User.Context.Restrictions.UpdateAsync(restriction, ct);
		_scoped.User.Context.Registry.Sessions.GetValueOrDefault(_scoped.User.Record)
			?.ChangeRestrictions(running => running.RemoveAll(restriction.Equals));
		await _scoped.User.Context.ModerationLog.AppendAsync(log, ct);

		_scoped.User.Context.Events.Emit(new UserRestrictionLifted(_scoped.User.Record, restriction));
		return UserResult.Ok;
	}

	private async Task<(UserResult Result, Restriction? Restriction)> ImposeAsync(Permissions needed,
		Permissions permissions, DateTimeOffset? endsAt, string? reason, CancellationToken ct)
	{
		if (_scoped.Guard(needed, false) is { } denied) return (denied, null);

		var now = _scoped.User.Context.Clock.GetUtcNow();
		if (permissions == Permissions.None || (endsAt is { } end && end <= now))
			return (UserResult.InvalidRestriction, null);

		var log = new ModerationData
		{
			Action = ModerationAction.Restrict,
			By = _scoped.By.User,
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
		var restriction = await context.Restrictions.CreateAsync(data, ct);
		if (context.Registry.Sessions.GetValueOrDefault(_scoped.User.Record) is { } session)
		{
			session.ChangeRestrictions(running => running.Add(restriction));
			context.Sessions.CloseDisallowed(session);
		}

		await context.ModerationLog.AppendAsync(log, ct);

		context.Events.Emit(new UserRestricted(_scoped.User.Record, restriction));
		return (UserResult.Ok, restriction);
	}
}