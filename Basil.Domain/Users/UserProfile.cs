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

	public static void ValidateName(string name)
	{
		if (!UserData.IsValidName(name)) throw new InvalidUserNameException();
	}

	public static void ValidateCountry(Country country)
	{
		if (!Enum.IsDefined(country)) throw new InvalidCountryException();
	}
}