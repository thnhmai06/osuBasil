using Basil.Application.Sessions.Persistence;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Users.Persistence;

internal static class CredentialChecks
{
	public static async Task<bool> IsPasswordAsync(this ICredentialRepository credentials, UserRecord user,
		Md5Hash passwordHash, CancellationToken ct)
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