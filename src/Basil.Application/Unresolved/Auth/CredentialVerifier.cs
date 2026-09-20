using System.Text;
using Basil.Application.Persistence.Repository;

namespace Basil.Application.Unresolved.Auth;

/// <summary>
///     Verifies a user's password MD5 against their stored hash.
/// </summary>
public sealed class CredentialVerifier(IUserRepository users, IPasswordHasher passwordHasher)
{
	/// <summary>
	///     Verifies <paramref name="passwordMd5" /> against the stored password hash of the user
	///     identified by <paramref name="userId" />.
	/// </summary>
	/// <param name="userId">The id of the user to verify.</param>
	/// <param name="passwordMd5">The hex-encoded MD5 digest of the user's password.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     <see langword="true" /> if the user has a stored password hash and it matches
	///     <paramref name="passwordMd5" />; otherwise, <see langword="false" />.
	/// </returns>
	public async Task<bool> VerifyPasswordAsync(
		int userId, string passwordMd5, CancellationToken cancellationToken = default)
	{
		var passwordHash = await users.FetchPasswordHashAsync(userId, cancellationToken);
		return passwordHash is not null && passwordHasher.Verify(Encoding.UTF8.GetBytes(passwordMd5), passwordHash);
	}
}