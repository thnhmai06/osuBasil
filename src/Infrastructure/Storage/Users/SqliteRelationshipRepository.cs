using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Batching;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
internal sealed class SqliteRelationshipRepository(DatabaseBatcher batcher, IUserRepository users)
	: IRelationshipRepository
{
	private readonly IdentityMap<int, ImmutableList<Relationship>> _byActor = new();

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var values = Values(relationship);
		_byActor.TryUpdate(actorId, current => Replace(current, relationship));
		return batcher.EnqueueAsync((GetType(), (actorId, targetId)), (connection, transaction) => connection.ExecuteAsync(
			"""
			INSERT INTO Relationships (ActorId, TargetId, Type, CreatedAt)
			VALUES (@ActorId, @TargetId, @Type, @CreatedAt)
			ON CONFLICT(ActorId, TargetId) DO UPDATE SET
				Type = excluded.Type,
				CreatedAt = excluded.CreatedAt;
			""",
			values, transaction));
	}

	/// <inheritdoc />
	public Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var parameters = new { ActorId = actorId, TargetId = targetId };
		_byActor.TryUpdate(actorId, current => current.RemoveAll(item => item.Target.Id == targetId));
		return batcher.EnqueueAsync((GetType(), (actorId, targetId)), (connection, transaction) => connection.ExecuteAsync(
			"DELETE FROM Relationships WHERE ActorId = @ActorId AND TargetId = @TargetId",
			parameters, transaction));
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Relationship>> ListAsync(User actor, CancellationToken cancellationToken = default)
	{
		var liveActor = await users.GetAsync(actor.Id, cancellationToken) ?? actor;
		var relationships = await _byActor.GetOrAddAsync(liveActor.Id, async _ =>
		{
			var rows = await batcher.ReadAsync(connection => connection.QueryAsync<RelationshipRow>(
				"SELECT ActorId, TargetId, Type, CreatedAt FROM Relationships WHERE ActorId = @ActorId",
				new { ActorId = liveActor.Id }), cancellationToken);

			var items = ImmutableList.CreateBuilder<Relationship>();
			foreach (var row in rows)
			{
				var target = await users.GetAsync(row.TargetId, cancellationToken);
				if (target is not null)
					items.Add(new Relationship
					{
						Actor = liveActor,
						Target = target,
						Type = (RelationshipType)row.Type,
						CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt)
					});
			}

			return items.ToImmutable();
		});

		return relationships ?? ImmutableList<Relationship>.Empty;
	}

	private static ImmutableList<Relationship> Replace(ImmutableList<Relationship> current, Relationship item)
	{
		var index = current.FindIndex(relationship => relationship.Target.Id == item.Target.Id);
		return index < 0 ? current.Add(item) : current.SetItem(index, item);
	}

	private static object Values(Relationship relationship)
	{
		return new
		{
			ActorId = relationship.Actor.Id,
			TargetId = relationship.Target.Id,
			Type = (long)relationship.Type,
			CreatedAt = relationship.CreatedAt.ToUnixTimeMilliseconds()
		};
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
