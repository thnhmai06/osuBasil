using System.Text.RegularExpressions;
using Basil.Domain.Users;
using Basil.Shared.Utilities;

namespace Basil.Domain.Users;

public sealed class UserRecord : IEquatable<UserRecord>
{
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "User ids start at 1.");
	}

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

public sealed partial class UserData
{
	public string NormalizedName => NormalizeName(Name);

	public required string Name
	{
		get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			if (NameError(value) is not null) throw new InvalidUserNameException();
			field = value;
		}
	}

	public Country Country
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = Country.Xx;

	public UserPrivileges Privileges
	{
		get;
		set => field = value.ThrowIfInvalid();
	} = UserPrivilegesExtensions.Default;

	public DateTimeOffset? SilencedUntil { get; set; }

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

	public static bool IsValidName(string name)
	{
		return NameError(name) is null;
	}

	private static string? NameError(string value)
	{
		if (value.Length is < 3 or > 15) return "Username must be between 3 and 15 characters.";
		if (value.StartsWith(' ') || value.EndsWith(' ')) return "Username cannot start or end with a space.";
		if (value.Contains("  ")) return "Username cannot contain consecutive spaces.";
		if (value.All(char.IsDigit)) return "Username cannot contain only digits.";
		if (!OsuUsernameChars().IsMatch(value))
			return "Username may only contain letters, numbers, spaces, and _ - [ ].";
		return null;
	}

	public bool IsSilenced(DateTimeOffset now)
	{
		return now < SilencedUntil;
	}

	public static string NormalizeName(string name)
	{
		return name.ToLowerInvariant().Replace(' ', '_');
	}

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+\z")]
	private static partial Regex OsuUsernameChars();
}