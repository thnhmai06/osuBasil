using Basil.Domain.Users;

namespace Basil.Application.Storage.Users;

/// <summary>Stores the restrictions of users.</summary>
public interface IRestrictionRepository
{
	/// <summary>Stores a new restriction and assigns its id.</summary>
	/// <param name="data">The restriction to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored restriction.</returns>
	Task<Restriction> CreateAsync(RestrictionData data, CancellationToken cancellationToken = default);

	/// <summary>Stores a restriction under its id, replacing the stored restriction.</summary>
	/// <param name="restriction">The restriction to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Restriction restriction, CancellationToken cancellationToken = default);

	/// <summary>Gets a restriction by id.</summary>
	/// <param name="id">The restriction id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The restriction, or <see langword="null" /> when no restriction has that id.</returns>
	ValueTask<Restriction?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Lists every restriction of a user, ended ones included, oldest first.</summary>
	/// <param name="user">The restricted user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user's restrictions.</returns>
	Task<IReadOnlyList<Restriction>> ListAsync(User user, CancellationToken cancellationToken = default);
}
