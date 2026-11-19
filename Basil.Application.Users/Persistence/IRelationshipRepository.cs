using Basil.Domain.Users;

namespace Basil.Application.Users.Persistence;

public interface IRelationshipRepository
{
	Task CreateOrUpdateAsync(Relationship relationship);

	Task DeleteAsync(Relationship relationship);

	Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor, CancellationToken ct = default);
}