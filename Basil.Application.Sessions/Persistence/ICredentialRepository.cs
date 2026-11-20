using Basil.Domain.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Sessions.Persistence;

public interface ICredentialRepository
{
	Task<bool> VerifyAsync(Credentials credentials, CancellationToken ct = default);

	Task CreateOrUpdateAsync(Credentials credentials);

	Task DeleteAsync(UserRecord user);
}