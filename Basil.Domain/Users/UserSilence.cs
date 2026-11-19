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

	public static void ValidateImpose(DateTimeOffset endsAt, DateTimeOffset now)
	{
		if (endsAt <= now) throw new InvalidSilenceException();
	}

	public UserActionResult ValidateLift(DateTimeOffset now)
	{
		return IsSilencedAt(now) ? UserActionResult.Ok : UserActionResult.NotFound;
	}

	public void Set(DateTimeOffset? endsAt)
	{
		using var scope = _user.Enter();
		_user.Record.Value.SilencedUntil = endsAt;
	}
}