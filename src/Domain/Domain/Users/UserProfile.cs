using Basil.Domain.Users;

namespace Basil.Domain.Users;

public sealed class UserProfile
{
	private readonly User _user;
	private string? _awayMessage;
	private bool _pmPrivate;

	internal UserProfile(User user)
	{
		_user = user;
	}

	public string? AwayMessage
	{
		get
		{
			using var scope = _user.Enter();
			return _awayMessage;
		}
	}

	public bool PmPrivate
	{
		get
		{
			using var scope = _user.Enter();
			return _pmPrivate;
		}
	}

	public void SetCountry(Country country)
	{
		using var scope = _user.Enter();
		_user.Record.Value.Country = country;
	}

	public void SetAway(string? message)
	{
		using var scope = _user.Enter();
		_awayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
	}

	public void SetPmPrivate(bool pmPrivate)
	{
		using var scope = _user.Enter();
		_pmPrivate = pmPrivate;
	}

	public static UserOutcome ValidateName(string name)
	{
		return UserData.IsValidName(name) ? UserOutcome.Ok : UserOutcome.InvalidName;
	}

	public static UserOutcome ValidateCountry(Country country)
	{
		return Enum.IsDefined(country) ? UserOutcome.Ok : UserOutcome.InvalidCountry;
	}
}