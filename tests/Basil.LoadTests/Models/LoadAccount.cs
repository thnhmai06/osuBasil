namespace Basil.LoadTests.Models;

/// <summary>
///     One seeded account a virtual user logs in as. Claimed by exactly one virtual user per run,
///     since a second concurrent session for the same username is rejected within 10 seconds of the first.
/// </summary>
public sealed class LoadAccount
{
	/// <summary>The account's stable position in the seeded pool.</summary>
	public required int Index { get; init; }

	/// <summary>The account's username.</summary>
	public required string Name { get; init; }

	/// <summary>The account's plaintext password, as sent to the account-creation API.</summary>
	public required string Password { get; init; }

	/// <summary>The lowercase hex MD5 of <see cref="Password" />, as the bancho login body requires.</summary>
	public required string PasswordMd5 { get; init; }

	/// <summary>The account's persistent user id, set once known (seeding response or a successful login).</summary>
	public int? UserId { get; set; }
}