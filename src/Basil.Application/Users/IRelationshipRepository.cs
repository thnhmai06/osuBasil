using Basil.Domain.Social;
using Basil.Domain.Users;

namespace Basil.Application.Users;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
public interface IRelationshipRepository
{
	/// <summary>Stores a relationship, replacing any relationship the same user holds toward the same target.</summary>
	/// <param name="relationship">The relationship to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default);

	/// <summary>Deletes a relationship; deleting a missing one does nothing.</summary>
	/// <param name="relationship">The relationship to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default);

	/// <summary>Lists the relationships a user holds.</summary>
	/// <param name="actor">The user whose relationships to list.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The relationships the user holds.</returns>
	Task<IReadOnlyList<Relationship>> ListAsync(User actor, CancellationToken cancellationToken = default);
}