namespace Basil.Application.Users;

/// <summary>Holds the server's administrator key.</summary>
public interface IAdminKeyRepository
{
	/// <summary>Checks a candidate administrator key.</summary>
	/// <param name="key">The candidate key.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns><see langword="true" /> if the key matches, or if no key has been set; otherwise, <see langword="false" />.</returns>
	Task<bool> VerifyAsync(string key, CancellationToken cancellationToken = default);
}