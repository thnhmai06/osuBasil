namespace Basil.Application.Contracts.Repositories;

/// <summary>
///     Computes the case- and space-insensitive key <see cref="IRepository{TKey,T}" /> uses to look up a user by
///     name.
/// </summary>
public static class UserSafeName
{
	/// <summary>Normalizes a username into its safe-name lookup key.</summary>
	/// <param name="name">The username to normalize.</param>
	/// <returns>The name, trimmed, lowercased, and with spaces replaced by underscores.</returns>
	public static string Of(string name)
	{
		return name.Trim().ToLowerInvariant().Replace(' ', '_');
	}
}