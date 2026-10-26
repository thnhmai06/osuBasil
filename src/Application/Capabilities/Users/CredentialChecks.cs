using Basil.Application.Persistence.Auth;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Application.Capabilities.Users;

/// <summary>Checks a claimed password hash the way every login and password change does.</summary>
internal static class CredentialChecks
{
	/// <summary>Checks whether a claimed password hash is the account's password.</summary>
	/// <param name="credentials">The stored passwords.</param>
	/// <param name="user">The account.</param>
	/// <param name="passwordHash">The claimed password hash.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> when it is the account's password; a hash that is not even well formed is simply
	///     wrong.
	/// </returns>
	public static async Task<bool> IsPasswordAsync(this ICredentialRepository credentials, UserRecord user,
		Md5 passwordHash, CancellationToken cancellationToken)
	{
		try
		{
			return await credentials.VerifyAsync(new Credentials(user, passwordHash), cancellationToken);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}
}