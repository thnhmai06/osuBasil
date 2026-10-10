using Basil.Domain.Auth;

namespace Basil.Application.Storage.Contracts.Auth;

/// <summary>
///     The single source of truth for a user's login credential.
/// </summary>
/// <remarks>The stored secret can never be read back out; only verified against a candidate.</remarks>
public interface ICredentialRepository
{
	/// <summary>Verifies whether <paramref name="credentials" /> match the stored credential for its user.</summary>
	/// <param name="credentials">The candidate credential to verify.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> if the credential matches; otherwise, <see langword="false" />.</returns>
	Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default);

	/// <summary>Stores a user's credential, replacing any credential stored for that user.</summary>
	/// <param name="credentials">The credential to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Credentials credentials, CancellationToken cancellationToken = default);
}
