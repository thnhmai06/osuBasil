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

	public DateTimeOffset? Until
	{
		get
		{
			using var scope = _user.Enter();
			return _user.Record.Value.SilencedUntil;
		}
	}

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

	internal async Task<UserOutcome> LiftAsync(string? reason, UserRecord actor)
	{
		var now = _user.Context.Clock.GetUtcNow();
		if (!_user.Record.Value.IsSilenced(now)) return UserOutcome.NotFound;

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