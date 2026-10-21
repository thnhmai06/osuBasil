using System.Text.RegularExpressions;
using Basil.Domain.Common;
using Basil.Domain.Permissions;

namespace Basil.Domain.Users;

/// <summary>
///     A registered user identified by their id.
/// </summary>
public sealed class UserRecord : IEquatable<UserRecord>
{
	/// <summary>Gets the unique identifier of the user.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
	public required int Id
	{
		get;
		init => field = value >= 1
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "User ids start at 1.");
	}

	/// <summary>Gets the user data this identity wraps.</summary>
	public required UserData Value { get; init; }

	/// <summary>
	///     Determines whether another user refers to the same account.
	/// </summary>
	/// <remarks>
	///     Two users are considered equal when their <see cref="Id" /> values are equal.
	/// </remarks>
	/// <param name="other">The user to compare against, or <see langword="null" />.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="other" /> has the same <see cref="Id" />;
	///     otherwise, <see langword="false" />.
	/// </returns>
	public bool Equals(UserRecord? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>
	///     Determines whether this user equals another object.
	/// </summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="UserRecord" /> with the same
	///     <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is UserRecord other && Equals(other);
	}

	/// <summary>
	///     Returns the hash code of this user.
	/// </summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the user.</returns>
	public override int GetHashCode()
	{
		return Id.GetHashCode();
	}
}

/// <summary>
///     The account data of a registered user, separate from the user's persistent identity.
/// </summary>
public sealed partial class UserData
{
	/// <summary>Gets the form of the name that identifies the user: lower case, with spaces written as underscores.</summary>
	/// <remarks>Two names with the same safe name are the same name.</remarks>
	public string NormalizedName => NormalizeName(Name);

	/// <summary>
	///     Gets or sets the username.
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     Thrown when the username does not satisfy osu!'s registration rules.
	/// </exception>
	public required string Name
	{
		get;
		set
		{
			ArgumentNullException.ThrowIfNull(value);
			if (value.Length is < 3 or > 15)
				throw new ArgumentException("Username must be between 3 and 15 characters.", nameof(value));
			if (value.StartsWith(' ') || value.EndsWith(' '))
				throw new ArgumentException("Username cannot start or end with a space.", nameof(value));
			if (value.Contains("  "))
				throw new ArgumentException("Username cannot contain consecutive spaces.", nameof(value));
			if (value.All(char.IsDigit))
				throw new ArgumentException("Username cannot contain only digits.", nameof(value));
			if (!OsuUsernameChars().IsMatch(value))
				throw new ArgumentException("Username may only contain letters, numbers, spaces, and _ - [ ].",
					nameof(value));
			field = value;
		}
	}

	/// <summary>Gets or sets the country the user is registered in.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is not a defined country.</exception>
	public Country Country
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = Country.Xx;

	/// <summary>Gets or sets the permissions granted to the user.</summary>
	/// <remarks>New users are granted every player and supporter permission.</remarks>
	/// <exception cref="ArgumentOutOfRangeException">The value sets a bit no permission defines.</exception>
	public Permissions.Permissions Permissions
	{
		get;
		set => field = value.ThrowIfUndefined();
	} = PermissionsGroups.Default;

	/// <summary>Gets or sets the date and time when the user was permanently deleted, if any.</summary>
	/// <exception cref="InvalidOperationException">The account was deleted already and the value is <see langword="null" />.</exception>
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

	/// <summary>Gets the normalized form of a user name.</summary>
	public static string NormalizeName(string name)
	{
		return name.ToLowerInvariant().Replace(' ', '_');
	}

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+\z")]
	private static partial Regex OsuUsernameChars();
}