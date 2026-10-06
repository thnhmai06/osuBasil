using Basil.Application.Storage.Contracts.Common;
using Basil.Domain.Users;

namespace Basil.Application.Storage.Contracts.Users;

/// <summary>Stores registered users.</summary>
public interface IUserRepository
{
	/// <summary>Stores a new user and assigns its id.</summary>
	/// <param name="data">The account data of the new user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored user.</returns>
	/// <remarks>Ids start at 1.</remarks>
	Task<User> CreateAsync(UserData data, CancellationToken cancellationToken = default);

	/// <summary>Stores a user under its id, adding it when no user has that id and replacing the stored user otherwise.</summary>
	/// <param name="user">The user to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(User user, CancellationToken cancellationToken = default);

	/// <summary>Gets a user by id.</summary>
	/// <param name="id">The user id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user, or <see langword="null" /> when no user has that id.</returns>
	ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Gets a user by name, ignoring case and treating spaces and underscores as equal.</summary>
	/// <param name="name">The username to look up.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user, or <see langword="null" /> when no user has that name.</returns>
	ValueTask<User?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>Lists the users a query includes, ordered by id.</summary>
	/// <param name="query">Which users to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of users.</returns>
	Task<Page<User>> ListAsync(UserQuery query, PageRequest page, CancellationToken cancellationToken = default);
}