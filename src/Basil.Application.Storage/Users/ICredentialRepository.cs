using Basil.Domain.Auth;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Users;

/// <summary>
///     The single source of truth for a user's login credential.
/// </summary>
/// <remarks>
///     The stored secret can never be read back out; only verified against a candidate.
///     The administrator key is kept the same way: it can be verified but never read back.
/// </remarks>
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

	/// <summary>Verifies a candidate administrator key.</summary>
	/// <param name="key">The candidate key.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if an administrator key is set and matches <paramref name="key" />; otherwise,
	///     <see langword="false" />.
	/// </returns>
	Task<bool> VerifyAdminKeyAsync(Md5 key, CancellationToken cancellationToken = default);

	/// <summary>Gets when the administrator key was last set.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The time it was set, or <see langword="null" /> when no administrator key is set.</returns>
	ValueTask<DateTimeOffset?> GetAdminKeyUpdatedAtAsync(CancellationToken cancellationToken = default);

	/// <summary>Sets the administrator key, replacing any key already set.</summary>
	/// <param name="key">The key to set.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAdminKeyAsync(Md5 key, CancellationToken cancellationToken = default);

	/// <summary>Removes the administrator key, so that none is set.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAdminKeyAsync(CancellationToken cancellationToken = default);
}