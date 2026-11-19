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

	internal async Task<UserActionResult> ImposeAsync(DateTimeOffset endsAt, string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		DomainSilence.ValidateImpose(endsAt, now);

		await _user.StoreChangeAsync(data => data.SilencedUntil = endsAt);
		_user.Apply(user => user.Silence.Set(endsAt));
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Silence, actor, reason, now));

		_user.Context.Events.Publish(new UserSilenced(_user.Record, endsAt));
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> LiftAsync(string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (_user.State.Silence.ValidateLift(now) is var absent and not UserActionResult.Ok) return absent;

		await _user.StoreChangeAsync(data => data.SilencedUntil = null);
		_user.Apply(user => user.Silence.Set(null));
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Unsilence, actor, reason, now));
		_user.Context.Events.Publish(new UserUnsilenced(_user.Record));
		return UserActionResult.Ok;
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