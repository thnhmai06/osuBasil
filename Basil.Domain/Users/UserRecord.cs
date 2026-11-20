using System.Text.RegularExpressions;
using Basil.Shared.Utilities;

namespace Basil.Domain.Users;

/// <summary>A stored account: its identifier and its data.</summary>
/// <remarks>Two records are equal when their identifiers are equal.</remarks>
public sealed class UserRecord : IEquatable<UserRecord>
{
	/// <summary>The identifier of the account, starting at 1.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "User ids start at 1.");
	}

	/// <summary>The data of the account.</summary>
	public required UserData Value { get; init; }

	public bool Equals(UserRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is UserRecord other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

/// <summary>The data of an account: name, country, privileges, silence and deletion.</summary>
public sealed partial class UserData
{
	/// <summary>The name in the form used to compare names: lower case, with spaces read as underscores.</summary>
	public string NormalizedName => Name.ToLowerInvariant().Replace(' ', '_');

	/// <summary>Gives the form in which user names are compared, or <see langword="null" /> when the text is not a valid user name.</summary>
	public static string? TryNormalize(string name)
	{
		try
		{
			return new UserData { Name = name }.NormalizedName;
		}
		catch (InvalidUserNameException)
		{
			return null;
		}
	}

	/// <summary>The name of the account, 3 to 15 characters long, made of letters, digits, spaces and the characters _ - [ ], not starting or ending with a space, without consecutive spaces and not made only of digits.</summary>
	/// <exception cref="InvalidUserNameException">The value breaks one of those rules.</exception>
	public required string Name
	{
		get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);

			if (value.Length is < 3 or > 15)
				throw new InvalidUserNameException("Username must be between 3 and 15 characters.");
			if (value.StartsWith(' ') || value.EndsWith(' '))
				throw new InvalidUserNameException("Username cannot start or end with a space.");
			if (value.Contains("  "))
				throw new InvalidUserNameException("Username cannot contain consecutive spaces.");
			if (value.All(char.IsDigit))
				throw new InvalidUserNameException("Username cannot contain only digits.");
			if (!OsuUsernameChars().IsMatch(value))
				throw new InvalidUserNameException("Username may only contain letters, numbers, spaces, and _ - [ ].");

			field = value;
		}
	}

	/// <summary>The country of the account; it starts as <c>Xx</c>.</summary>
	/// <exception cref="InvalidCountryException">The value is not a defined country.</exception>
	public Country Country
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = Country.Xx;

	/// <summary>The privileges the account holds; a new account holds <see cref="UserPrivilegesExtensions.Default" />.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value holds a bit that is not a defined privilege.</exception>
	public UserPrivileges Privileges
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = UserPrivilegesExtensions.Default;

	/// <summary>The moment the account's silence ends, or <see langword="null" /> when it was never silenced.</summary>
	public DateTimeOffset? SilencedEnd { get; set; }

	/// <summary>The moment the account was deleted, or <see langword="null" /> while it is live.</summary>
	/// <exception cref="InvalidOperationException">The value clears the deletion of a deleted account.</exception>
	public DateTimeOffset? DeletedAt
	{
		get;
		set
		{
			if (value is null && field is not null)
				throw new InvalidOperationException("A deleted account is never restored.");
			field = value;
		}
	}

	/// <summary>Checks whether the account is silenced at a moment.</summary>
	/// <param name="now">The moment to check.</param>
	/// <returns><see langword="true" /> when the moment is before the end of the silence.</returns>
	public bool IsSilenced(DateTimeOffset now)
	{
		return now < SilencedEnd;
	}

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+\z")]
	private static partial Regex OsuUsernameChars();
}