using Basil.Application.Authentication.Persistence;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

internal static class CredentialChecks
{
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