using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>Looks up registered users.</summary>
public interface IBasilUsers
{
	/// <summary>Gets a user by id.</summary>
	/// <param name="id">The user id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user, or <see langword="null" /> when none has that id.</returns>
	ValueTask<User?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Gets a user by name, ignoring case and treating spaces and underscores as equal.</summary>
	/// <param name="name">The username.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user, or <see langword="null" /> when none has that name.</returns>
	ValueTask<User?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
}

