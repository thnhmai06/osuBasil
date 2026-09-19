using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Basil.Domain.Client;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a registered user of the server.
/// </summary>
public sealed class User : IEquatable<User>
{
	public required int Id { get; init; }
	public required string Name { get; set; }
	public Country Country { get; set; } = Country.Xx;

	public ClientPrivileges Privilege
	{
		get => DeletedAt is not null ? ClientPrivileges.None : field;
		set => field = DeletedAt is null ? ClientPrivileges.None : value;
	} = ClientPrivileges.Player | ClientPrivileges.Supporter;

	public DateTimeOffset? SilenceEnd { get; set; } = null; // osu! client will handle this.
	public DateTimeOffset? DeletedAt { get; set; } = null;

	public bool Equals(User? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	public override bool Equals(object? obj)
	{
		return obj is User other && Equals(other);
	}

	public override int GetHashCode()
	{
		return Id;
	}
}

public static partial class Username
{
	[GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+$")]
	private static partial Regex OsuUsernameChars();

	/// <summary>
	///     Normalizes a username for case-insensitive and space-insensitive identity comparisons.
	/// </summary>
	/// <param name="name">The raw username to normalize.</param>
	/// <returns>The username converted to lowercase with spaces replaced by underscores.</returns>
	/// <remarks>
	///     Matches osu!'s own deduplication rule, where "Peppy", "peppy", "pe_ppy", and "pe ppy"
	///     all resolve to the same identity. This is a database lookup and uniqueness detail, not a
	///     field carried on <see cref="User" /> itself.
	/// </remarks>
	public static string ToSafeName(string name)
	{
		return name.ToLowerInvariant().Replace(' ', '_');
	}

	/// <summary>
	///     Validates a username against osu!'s registration rules.
	/// </summary>
	/// <param name="name">The username to validate.</param>
	/// <param name="error">
	///     When this method returns <see langword="false" />, contains a user-facing message
	///     describing why the username is invalid.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if the username is valid; otherwise, <see langword="false" />.
	/// </returns>
	public static bool Validate(string name, [MaybeNullWhen(true)] out string error)
	{
		if (name.Length is < 3 or > 15) error = "Username must be between 3 and 15 characters.";
		else if (name.StartsWith(' ') || name.EndsWith(' ')) error = "Username cannot start or end with a space.";
		else if (name.Contains("  ")) error = "Username cannot contain consecutive spaces.";
		else if (name.All(char.IsDigit)) error = "Username cannot contain only digits.";
		else if (!OsuUsernameChars().IsMatch(name))
			error = "Username may only contain letters, numbers, spaces, and _ - [ ].";
		else error = null;

		return error is null;
	}
}