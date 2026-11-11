using Basil.Application.Shared;
using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
public interface IRelationshipRepository
{
	/// <summary>Stores a relationship, replacing any relationship the same user holds toward the same target.</summary>
	/// <param name="relationship">The relationship to store.</param>
	Task CreateOrUpdateAsync(Relationship relationship);

	/// <summary>Deletes a relationship; deleting a missing one does nothing.</summary>
	/// <param name="relationship">The relationship to delete.</param>
	Task DeleteAsync(Relationship relationship);

	/// <summary>Lists the relationships a user holds, oldest first.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> relationships are returned.</remarks>
	/// <param name="actor">The user whose relationships to list.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The relationships the user holds.</returns>
	Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor, CancellationToken ct = default);
}