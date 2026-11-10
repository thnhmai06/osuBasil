using Basil.Domain.Users;

namespace Basil.Application.Authentication.Persistence;

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
	Task CreateOrUpdateAsync(Credentials credentials);

	/// <summary>Removes the credential stored for a user, so no password verifies for them any more.</summary>
	/// <param name="user">The user whose credential is removed.</param>
	/// <remarks>Nothing happens when the user has no credential.</remarks>
	Task DeleteAsync(UserRecord user);
}