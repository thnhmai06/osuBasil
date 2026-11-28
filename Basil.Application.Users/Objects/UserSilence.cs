using Basil.Application.Users.Objects.Events;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

public sealed class UserSilence
{
	private readonly User _user;

	internal UserSilence(User user)
	{
		_user = user;
	}

	public DateTimeOffset? Until => _user.Record.Value.SilencedEnd;

	internal async Task<UserActionResult> ImposeAsync(DateTimeOffset endsAt, string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (endsAt <= now) throw new InvalidSilenceException();

		await _user.StoreChangeAsync(data => data.SilencedEnd = endsAt);
		_user.Apply(data => data.SilencedEnd = endsAt);
		await _user.Context.ModerationLog.AppendAsync(Log(ModerationAction.Silence, actor, reason, now));

		_user.Context.Events.Publish(new UserSilenced(_user.Record, endsAt));
		return UserActionResult.Ok;
	}

	internal async Task<UserActionResult> LiftAsync(string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (!_user.Record.Value.IsSilenced(now)) return UserActionResult.NotFound;

		await _user.StoreChangeAsync(data => data.SilencedEnd = null);
		_user.Apply(data => data.SilencedEnd = null);
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