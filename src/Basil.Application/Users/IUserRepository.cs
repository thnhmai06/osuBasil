using Basil.Domain.Users;

namespace Basil.Application.Users;

/// <summary>Stores registered users.</summary>
public interface IUserRepository
{
	/// <summary>Finds a user by name, ignoring case and treating spaces and underscores as equal.</summary>
	/// <param name="name">The username to look up.</param>
	/// <param name="cancellationToken">A token that cancels the read.</param>
	/// <returns>The user, or <see langword="null" /> when no user has that name.</returns>
	ValueTask<User?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>Stores a new user and assigns its id.</summary>
	/// <param name="data">The account data of the new user.</param>
	/// <param name="cancellationToken">A token that cancels the write.</param>
	/// <returns>The stored user.</returns>
	Task<User> AddAsync(UserData data, CancellationToken cancellationToken = default);
}