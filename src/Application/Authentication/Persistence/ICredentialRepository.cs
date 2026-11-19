using Basil.Domain.Authentication;
using Basil.Domain.Users;

namespace Basil.Application.Authentication.Persistence;

public interface ICredentialRepository
{
	Task<bool> VerifyAsync(Credentials credentials, CancellationToken ct = default);

	Task CreateOrUpdateAsync(Credentials credentials);

	Task DeleteAsync(UserRecord user);
}