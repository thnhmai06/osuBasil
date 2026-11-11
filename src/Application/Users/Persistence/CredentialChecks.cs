using Basil.Application.Authentication.Persistence;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

/// <summary>Checks a claimed password hash the way every login and password change does.</summary>
internal static class CredentialChecks
{
	/// <summary>Checks whether a claimed password hash is the account's password.</summary>
	/// <param name="credentials">The stored passwords.</param>
	/// <param name="user">The account.</param>
	/// <param name="passwordHash">The claimed password hash.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when it is the account's password; a hash that is not even well formed is simply
	///     wrong.
	/// </returns>
	public static async Task<bool> IsPasswordAsync(this ICredentialRepository credentials, UserRecord user,
		Md5 passwordHash, CancellationToken ct)
	{
		try
		{
			return await credentials.VerifyAsync(new Credentials(user, passwordHash), ct);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}