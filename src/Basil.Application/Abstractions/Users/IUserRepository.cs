using Basil.Domain.Login;
using Basil.Domain.Users;

namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Provides access to the Users table, scoped to what login needs.
/// </summary>
/// <remarks>
///     Broader filter and paging methods are added here when a use case needs them.
/// </remarks>
public interface IUserRepository
{
	/// <summary>
	///     Fetches a user by id.
	/// </summary>
	/// <param name="id">The id of the user to find.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching user, or <see langword="null" /> when no such user exists.</returns>
	Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches a user by name.
	/// </summary>
	/// <param name="name">The name to look up, normalized before the query.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The matching user, or <see langword="null" /> when no such user exists.</returns>
	/// <remarks>
	///     The lookup is by the safe form of the name, the normalization produced by
	///     <see cref="User.MakeSafeName" />, so a name matches regardless of case or spaces.
	/// </remarks>
	Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches a user's stored password hash.
	/// </summary>
	/// <param name="id">The id of the user to read.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored bcrypt hash, or <see langword="null" /> when no such user exists.</returns>
	/// <remarks>
	///     Kept separate from <see cref="User" /> on purpose so the hash never rides along into
	///     general-purpose flows.
	/// </remarks>
	Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>
	///     Updates a user's country.
	/// </summary>
	/// <param name="id">The id of the user to update.</param>
	/// <param name="country">The new country to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateCountryAsync(int id, Country country, CancellationToken cancellationToken = default);

	/// <summary>
	///     Updates a user's privileges.
	/// </summary>
	/// <param name="id">The id of the user to update.</param>
	/// <param name="privilege">The new privilege flags to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdatePrivilegesAsync(int id, UserPrivileges privilege, CancellationToken cancellationToken = default);

	/// <summary>
	///     Updates a user's name and its safe form together.
	/// </summary>
	/// <param name="id">The id of the user to update.</param>
	/// <param name="name">The new display name.</param>
	/// <param name="safeName">The new normalized name, produced by <see cref="User.MakeSafeName" />.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default);

	/// <summary>
	///     Creates a new user and returns it as persisted.
	/// </summary>
	/// <param name="name">The display name of the new user.</param>
	/// <param name="pwBcrypt">The bcrypt hash of the user's password.</param>
	/// <param name="country">The country of the new user.</param>
	/// <param name="privilege">The initial privilege flags, or <see langword="null" /> for the defaults.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The newly created user, or <see langword="null" /> when the name was taken.</returns>
	/// <remarks>
	///     Returns <see langword="null" /> when the requested name, in either its display or safe
	///     form, collides with an existing row. That collision is a lost race with a concurrent
	///     registration, since name availability was checked beforehand.
	/// </remarks>
	Task<User?> CreateAsync(string name, string pwBcrypt, Country country, UserPrivileges? privilege = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	///     Marks a user as deleted.
	/// </summary>
	/// <param name="id">The id of the user to delete.</param>
	/// <param name="deletedAt">The time of deletion.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>
	///     Soft delete: the row, its name, and its history are never removed. Stamping
	///     <see cref="User.DeletedAt" /> is the authoritative deletion signal; callers must not infer
	///     deletion from <see cref="UserPrivileges" /> being zero.
	/// </remarks>
	Task SoftDeleteAsync(int id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default);

	/// <summary>
	///     Fetches every user in the database.
	/// </summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Every stored user, in ascending id order.</returns>
	/// <remarks>
	///     Used by the management REST API's user listing.
	/// </remarks>
	Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default);
}