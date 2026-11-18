namespace Basil.Domain.Users;

public sealed class UserSilence
{
	private readonly User _user;

	internal UserSilence(User user)
	{
		_user = user;
	}

	public DateTimeOffset? EndsAt
	{
		get
		{
			using var scope = _user.Enter();
			return _user.Record.Value.SilencedUntil;
		}
	}

	public bool IsSilencedAt(DateTimeOffset now)
	{
		return _user.Record.Value.IsSilenced(now);
	}

	public static UserOutcome ValidateImpose(DateTimeOffset endsAt, DateTimeOffset now)
	{
		return endsAt <= now ? UserOutcome.InvalidSilence : UserOutcome.Ok;
	}

	public UserOutcome ValidateLift(DateTimeOffset now)
	{
		return IsSilencedAt(now) ? UserOutcome.Ok : UserOutcome.NotFound;
	}

	public void Set(DateTimeOffset? endsAt)
	{
		using var scope = _user.Enter();
		_user.Record.Value.SilencedUntil = endsAt;
	}
}
