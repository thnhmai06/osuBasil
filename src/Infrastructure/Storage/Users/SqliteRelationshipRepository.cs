using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
internal sealed class SqliteRelationshipRepository(Database database, IUserRepository users) : IRelationshipRepository
{
	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO Relationships (ActorId, TargetId, Type, CreatedAt)
			VALUES (@ActorId, @TargetId, @Type, @CreatedAt)
			ON CONFLICT(ActorId, TargetId) DO UPDATE SET
				Type = excluded.Type,
				CreatedAt = excluded.CreatedAt;
			""",
			new
			{
				ActorId = relationship.Actor.Id,
				TargetId = relationship.Target.Id,
				Type = (long)relationship.Type,
				CreatedAt = relationship.CreatedAt.ToUnixTimeMilliseconds()
			});
	}

	/// <inheritdoc />
	public async Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"DELETE FROM Relationships WHERE ActorId = @ActorId AND TargetId = @TargetId",
			new { ActorId = relationship.Actor.Id, TargetId = relationship.Target.Id });
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Relationship>> ListAsync(User actor, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<RelationshipRow>(
			"SELECT ActorId, TargetId, Type, CreatedAt FROM Relationships WHERE ActorId = @ActorId",
			new { ActorId = actor.Id });

		var relationships = new List<Relationship>();
		foreach (var row in rows)
		{
			var target = await users.GetAsync(row.TargetId, cancellationToken);
			if (target is null)
				continue;

			relationships.Add(new Relationship
			{
				Actor = actor,
				Target = target,
				Type = (RelationshipType)row.Type,
				CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt)
			});
		}

		return relationships;
	}

	/// <summary>A stored row of the Relationships table.</summary>
	private sealed class RelationshipRow
	{
		public int ActorId { get; set; }
		public int TargetId { get; set; }
		public long Type { get; set; }
		public long CreatedAt { get; set; }
	}
}
