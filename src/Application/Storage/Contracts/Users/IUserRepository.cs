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
	/// <exception cref="AlreadyExistsException">Another user already has the same safe name.</exception>
	Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default);

	/// <summary>Stores the new values of a created user, replacing the stored ones.</summary>
	/// <param name="user">The user to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The name of a user already stored is kept; a stored user changes name only through <see cref="RenameAsync" />.</remarks>
	Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default);

	/// <summary>Gives a stored user a new name, unless another user already has it.</summary>
	/// <param name="user">The user to rename.</param>
	/// <param name="name">The new name; names that differ only in case or in spaces versus underscores are the same name.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns><see langword="true" /> when the user now has the new name; <see langword="false" /> when another user has it.</returns>
	/// <remarks>The user keeps the old name until the new one is stored.</remarks>
	/// <exception cref="ArgumentException">The name is not a valid user name.</exception>
	Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default);

	/// <summary>Gets a user by id.</summary>
	/// <param name="id">The user id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user, or <see langword="null" /> when no user has that id.</returns>
	ValueTask<UserRecord?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Gets a user by name, ignoring case and treating spaces and underscores as equal.</summary>
	/// <param name="name">The username to look up.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user, or <see langword="null" /> when no user has that name.</returns>
	ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>Lists the users a query includes, ordered by id.</summary>
	/// <param name="query">Which users to include.</param>
	/// <param name="page">Which part of the listing to return.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>A page of users.</returns>
	Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page, CancellationToken cancellationToken = default);
}