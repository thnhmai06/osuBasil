using Basil.Application.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

/// <summary>Stores the restrictions of users.</summary>
public interface IRestrictionRepository
{
	/// <summary>Stores a new restriction and assigns its id.</summary>
	/// <param name="data">The restriction to store.</param>
	/// <param name="log">
	///     The moderation action to record with the change, stored together with it or not at all, or
	///     <see langword="null" /> to record none.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored restriction.</returns>
	Task<UserRestriction> CreateAsync(RestrictionData data, ModerationData? log = null,
		CancellationToken cancellationToken = default);

	/// <summary>Stores the new values of a created restriction, replacing the stored ones.</summary>
	/// <param name="restriction">The restriction to store.</param>
	/// <param name="log">
	///     The moderation action to record with the change, stored together with it or not at all, or
	///     <see langword="null" /> to record none.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task UpdateAsync(UserRestriction restriction, ModerationData? log = null,
		CancellationToken cancellationToken = default);

	/// <summary>Removes a restriction from the user's history.</summary>
	/// <param name="restriction">The restriction to remove.</param>
	/// <param name="log">
	///     The moderation action to record with the change, stored together with it or not at all, or
	///     <see langword="null" /> to record none.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(UserRestriction restriction, ModerationData? log = null,
		CancellationToken cancellationToken = default);

	/// <summary>Gets a restriction by id.</summary>
	/// <param name="id">The restriction id.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The restriction, or <see langword="null" /> when no restriction has that id.</returns>
	ValueTask<UserRestriction?> GetAsync(int id, CancellationToken cancellationToken = default);

	/// <summary>Lists every restriction of a user, ended ones included, oldest first.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> restrictions are returned.</remarks>
	/// <param name="user">The restricted user.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The user's restrictions.</returns>
	Task<IReadOnlyList<UserRestriction>> ListAsync(UserRecord user, CancellationToken cancellationToken = default);
}