using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

public interface IRelationshipRepository
{
	Task CreateOrUpdateAsync(Relationship relationship);

	Task DeleteAsync(Relationship relationship);

	/// <summary>Gets which of the users with the ids <paramref name="among"/> block <paramref name="target"/>.</summary>
	Task<IReadOnlySet<int>> ListBlockerIdsAsync(UserRecord target, IReadOnlyCollection<int> among,
		CancellationToken ct = default);

	Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor, CancellationToken ct = default);
}