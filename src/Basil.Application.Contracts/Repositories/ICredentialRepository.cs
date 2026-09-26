using Basil.Domain.Auth;

namespace Basil.Application.Contracts.Repositories;

/// <summary>
///     The single source of truth for a user's login credential.
/// </summary>
/// <remarks>
///     The stored secret can never be read back out; only verified against a candidate.
/// </remarks>
public interface ICredentialRepository
{
	/// <summary>Verifies whether <paramref name="credentials" /> match the stored credential for its user.</summary>
	/// <param name="credentials">The candidate credential to verify.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="true" /> if the credential matches; otherwise, <see langword="false" />.</returns>
	Task<bool> VerifyAsync(Credentials credentials, CancellationToken cancellationToken = default);

	/// <summary>Durably stores <paramref name="credentials" />, replacing any existing credential for its user.</summary>
	/// <param name="credentials">The credential to store.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	Task SaveAsync(Credentials credentials, CancellationToken cancellationToken = default);
}