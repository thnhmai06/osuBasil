using Basil.Application.Users.Objects.Events;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the silence of one account: the moment it ends, and the moderation log entries it leaves.</summary>
public sealed class UserSilence
{
	private readonly User _user;

	/// <summary>Creates the silence state for an account.</summary>
	/// <param name="user">The account that owns the silence.</param>
	internal UserSilence(User user)
	{
		_user = user;
	}

	/// <summary>Gets the moment the silence ends, or <see langword="null" /> when the account is not silenced.</summary>
	public DateTimeOffset? Until
	{
		get
		{
			using var scope = _user.Enter();
			return _user.Record.Value.SilencedUntil;
		}
	}

	/// <summary>Silences the account until a moment.</summary>
	/// <param name="endsAt">When the silence ends.</param>
	/// <param name="reason">Why the account is silenced, if known.</param>
	/// <param name="actor">The account imposing the silence.</param>
	/// <returns>The outcome of silencing the account.</returns>
	internal async Task<UserOutcome> ImposeAsync(DateTimeOffset endsAt, string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (endsAt <= now) return UserOutcome.InvalidSilence;

		await _user.StoreChangeAsync(data => data.SilencedUntil = endsAt);
		_user.Change(data => data.SilencedUntil = endsAt);
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Silence, actor, reason, now));

		_user.Context.Events.Publish(new UserSilenced(_user.Record, endsAt));
		return UserOutcome.Ok;
	}

	/// <summary>Ends the silence of the account now.</summary>
	/// <param name="reason">Why the silence is lifted, if known.</param>
	/// <param name="actor">The account lifting the silence.</param>
	/// <returns>The outcome of lifting the silence.</returns>
	internal async Task<UserOutcome> LiftAsync(string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (!_user.Record.Value.IsSilenced(now)) return UserOutcome.Ok;

		await _user.StoreChangeAsync(data => data.SilencedUntil = null);
		_user.Change(data => data.SilencedUntil = null);
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Unsilence, actor, reason, now));
		_user.Context.Events.Publish(new UserUnsilenced(_user.Record));
		return UserOutcome.Ok;
	}

	private ModerationData Log(ModerationAction action, UserRecord actor, string? reason,
		DateTimeOffset now)
	{
		return new ModerationData
		{
			Action = action,
			Actor = actor,
			Target = _user.Record,
			Reason = reason,
			Timestamp = now
		};
	}
}
