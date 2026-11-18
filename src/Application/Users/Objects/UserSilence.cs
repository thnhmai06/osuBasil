using Basil.Application.Users.Objects.Events;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using DomainSilence = Basil.Domain.Users.UserSilence;

namespace Basil.Application.Users.Objects;

public sealed class UserSilence
{
	private readonly User _user;

	internal UserSilence(User user)
	{
		_user = user;
	}

	public DateTimeOffset? Until => _user.State.Silence.EndsAt;

	internal async Task<UserOutcome> ImposeAsync(DateTimeOffset endsAt, string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (DomainSilence.ValidateImpose(endsAt, now) is var invalid and not UserOutcome.Ok) return invalid;

		await _user.StoreChangeAsync(data => data.SilencedUntil = endsAt);
		_user.Apply(user => user.Silence.Set(endsAt));
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Silence, actor, reason, now));

		_user.Context.Events.Publish(new UserSilenced(_user.Record, endsAt));
		return UserOutcome.Ok;
	}

	internal async Task<UserOutcome> LiftAsync(string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (_user.State.Silence.ValidateLift(now) is var absent and not UserOutcome.Ok) return absent;

		await _user.StoreChangeAsync(data => data.SilencedUntil = null);
		_user.Apply(user => user.Silence.Set(null));
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Unsilence, actor, reason, now));
		_user.Context.Events.Publish(new UserUnsilenced(_user.Record));
		return UserOutcome.Ok;
	}

	private ModerationLogData Log(ModerationAction action, UserRecord actor, string? reason,
		DateTimeOffset now)
	{
		return new ModerationLogData
		{
			Action = action,
			Actor = actor,
			Target = _user.Record,
			Reason = reason,
			Timestamp = now
		};
	}
}