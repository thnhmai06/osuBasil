using Basil.Application.Shared;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
internal sealed class EfRelationshipRepository(DatabaseSession database) : IRelationshipRepository
{
	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Relationship relationship)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var row = new RelationshipRow
		{
			ActorId = actorId,
			TargetId = targetId,
			Type = (int)relationship.Type,
			CreatedAt = relationship.CreatedAt.ToUniversalTime()
		};
		return database.WriteAsync((UsersDbContext context) =>
				context.UpsertAsync(row, stored => stored.ActorId == actorId && stored.TargetId == targetId));
	}

	/// <inheritdoc />
	public Task DeleteAsync(Relationship relationship)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		return database.WriteAsync((UsersDbContext context) => context.Relationships
			.Where(r => r.ActorId == actorId && r.TargetId == targetId)
			.ExecuteDeleteAsync());
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor,
		CancellationToken ct = default)
	{
		var actorId = actor.Id;
		var rows = await database.ReadAsync((UsersDbContext context) =>
				(from relationship in context.Relationships
					where relationship.ActorId == actorId
					join target in context.Users on relationship.TargetId equals target.Id
					orderby relationship.CreatedAt, relationship.TargetId
					select new { relationship, target }).Take(ListLimits.Ceiling).ToListAsync(ct),
			ct);

		return
		[
			.. rows.Select(row => new Relationship
			{
				Actor = actor,
				Target = EfUserRepository.ToUser(row.target),
				Type = (RelationshipType)row.relationship.Type,
				CreatedAt = row.relationship.CreatedAt
			})
		];
	}
}