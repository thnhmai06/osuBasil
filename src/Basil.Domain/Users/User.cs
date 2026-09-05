using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Basil.Domain.Login;

namespace Basil.Domain.Users;

/// <summary>
///     Represents a registered user of the server.
/// </summary>
/// <param name="Id">The unique identifier of the user.</param>
/// <param name="Name">The username of the user.</param>
/// <param name="Country">The country of the user.</param>
/// <param name="Privilege">The server-side privileges granted to the user.</param>
/// <param name="SilenceEnd">The time the user's silence expires, in UTC.</param>
/// <param name="DeletedAt">
///     The time the user was deleted, or <see langword="null" /> if the account is active. Deletion
///     is soft: the row, its score/social/anticheat history, and its name stay intact, and the
///     name remains reserved (see the <c>Users_Name_uindex</c>/<c>Users_SafeName_uindex</c>
///     constraints) so a later registration can never claim it.
/// </param>
/// <remarks>
///     Carries only the fields that the server reads back somewhere. Clans, public profiles,
///     preferred mode, play style, custom badges, and userpages are out of scope (see
///     docs/for-developers/working-scopes.md) and are not part of this record.
/// </remarks>
public sealed partial record User(
	int Id,
	string Name,
	Country Country,
	UserPrivileges Privilege,
	DateTimeOffset SilenceEnd,
	DateTimeOffset? DeletedAt = null)
{
	private static readonly Regex AllowedUsernameCharacters = OsuUsernamePattern();

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
	public static string MakeSafeName(string name)
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
	public static bool ValidateUsername(string name, [MaybeNullWhen(true)] out string error)
	{
		if (name.Length is < 3 or > 15) error = "Username must be between 3 and 15 characters.";
		else if (name.StartsWith(' ') || name.EndsWith(' ')) error = "Username cannot start or end with a space.";
		else if (name.Contains("  ")) error = "Username cannot contain consecutive spaces.";
		else if (name.All(char.IsDigit)) error = "Username cannot contain only digits.";
		else if (!AllowedUsernameCharacters.IsMatch(name))
			error = "Username may only contain letters, numbers, spaces, and _ - [ ].";
		else error = null;

		return error is null;
	}

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\[\] ]+$")]
	private static partial Regex OsuUsernamePattern();
}