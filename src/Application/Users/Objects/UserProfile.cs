using Basil.Domain.Users;

namespace Basil.Application.Users.Objects;

/// <summary>Owns the profile settings of one account that are kept in memory while it is online.</summary>
public sealed class UserProfile
{
	private readonly User _user;
	private string? _awayMessage;
	private bool _pmPrivate;

	/// <summary>Creates the profile state for an account.</summary>
	/// <param name="user">The account that owns the profile.</param>
	internal UserProfile(User user)
	{
		_user = user;
	}

	/// <summary>Gets the away message shown to other users, or <see langword="null" /> when the user is not away.</summary>
	public string? AwayMessage
	{
		get
		{
			using var scope = _user.Enter();
			return _awayMessage;
		}
	}

	/// <summary>Gets whether the user accepts private messages only from friends.</summary>
	public bool PmPrivate
	{
		get
		{
			using var scope = _user.Enter();
			return _pmPrivate;
		}
	}

	/// <summary>Sets the country shown on the profile.</summary>
	/// <param name="country">The country.</param>
	internal void SetCountry(Country country)
	{
		using var scope = _user.Enter();
		_user.Change(data => data.Country = country);
	}

	/// <summary>Sets the away message shown to other users.</summary>
	/// <param name="message">The message, or <see langword="null" /> or whitespace to clear it.</param>
	internal void SetAway(string? message)
	{
		using var scope = _user.Enter();
		_awayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
	}

	/// <summary>Sets whether the user accepts private messages only from friends.</summary>
	/// <param name="pmPrivate"><see langword="true" /> to restrict private messages to friends.</param>
	internal void SetPmPrivate(bool pmPrivate)
	{
		using var scope = _user.Enter();
		_pmPrivate = pmPrivate;
	}
}
