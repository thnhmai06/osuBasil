using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Basil.Domain.Client;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a registered user of the server.
/// </summary>
public sealed class User : IEquatable<User>
{
	/// <summary>Gets the unique identifier of the user.</summary>
	public required int Id { get; init; }

	/// <summary>Gets or sets the username of the user.</summary>
	public required string Name { get; set; }

	/// <summary>Gets or sets the country the user is registered in.</summary>
	public Country Country { get; set; } = Country.Xx;

	/// <summary>
	///     Gets or sets the privileges granted to the user.
	/// </summary>
	/// <remarks>
	///     The getter reports <see cref="ClientPrivileges.None" /> when the user is deleted and the
	///     stored value otherwise. The setter stores <see cref="ClientPrivileges.None" /> when the
	///     user is not deleted and the assigned value when the user is deleted, so the privileges of
	///     a live user are effectively fixed by the property initializer. New users default to
	///     <see cref="ClientPrivileges.Player" /> combined with
	///     <see cref="ClientPrivileges.Supporter" />.
	/// </remarks>
	public ClientPrivileges Privilege
	{
		get => DeletedAt is not null ? ClientPrivileges.None : field;
		set => field = DeletedAt is null ? ClientPrivileges.None : value;
	} = ClientPrivileges.Player | ClientPrivileges.Supporter;

	/// <summary>
	///     Gets or sets the date and time when the user's silence expires, if the user is silenced.
	/// </summary>
	/// <remarks>
	///     The osu! client enforces the silence itself once it is told about it.
	/// </remarks>
	public DateTimeOffset? SilenceEnd { get; set; } = null; // osu! client will handle this.

	/// <summary>Gets or sets the date and time when the user was deleted, if any.</summary>
	public DateTimeOffset? DeletedAt { get; set; } = null;

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
	public bool Equals(User? other)
	{
		if (other is null) return false;
		return Id == other.Id;
	}

	/// <summary>
	///     Determines whether this user equals another object.
	/// </summary>
	/// <param name="obj">The object to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="obj" /> is a <see cref="User" /> with the same
	///     <see cref="Id" />; otherwise, <see langword="false" />.
	/// </returns>
	public override bool Equals(object? obj)
	{
		return obj is User other && Equals(other);
	}

	/// <summary>
	///     Returns the hash code of this user.
	/// </summary>
	/// <returns>The <see cref="Id" />, which uniquely identifies the user.</returns>
	public override int GetHashCode()
	{
		return Id;
	}
}

/// <summary>
///     Provides username normalization and validation rules that mirror the osu! server.
/// </summary>
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