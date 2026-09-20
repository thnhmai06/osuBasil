using Basil.Domain.Auth;

namespace Basil.Application.Persistence.Repository;

public interface ICredentialRepository
{
	Task<IReadOnlyCollection<bool>> VerifyAsync(
		IReadOnlyCollection<Credentials> credentials,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyCollection<bool>> CreateOrUpdateAsync(
		IReadOnlyCollection<Credentials> credentials,
		CancellationToken cancellationToken = default);
}