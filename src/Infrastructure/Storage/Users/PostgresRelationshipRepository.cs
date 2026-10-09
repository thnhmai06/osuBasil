using System.Collections.Immutable;
using Basil.Application.Storage.Contracts.Users;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Infrastructure.Storage.Memory;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Users;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
internal sealed class PostgresRelationshipRepository(
	Database database,
	DatabaseWriter writer,
	IUserRepository users)
	: IRelationshipRepository
{
	private readonly OwnedLists<int, User, Relationship> _byActor = new();

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var actor = await users.GetAsync(actorId, cancellationToken);
		var values = Values(relationship);
		var saved = writer.SaveAsync(Root.User(actorId), (GetType(), (actorId, targetId)), new WriteCommand(
			"""
			insert into relationships (actor_id, target_id, type, created_at)
			values (@ActorId, @TargetId, @Type, @CreatedAt)
			on conflict (actor_id, target_id) do update set
				type = excluded.type,
				created_at = excluded.created_at;
			""",
			values));
		_byActor.Change(actorId, actor, current => Replace(current, relationship), saved);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var actor = await users.GetAsync(actorId, cancellationToken);
		var parameters = new { ActorId = actorId, TargetId = targetId };
		var deleted = writer.SaveAsync(Root.User(actorId), (GetType(), (actorId, targetId)), new WriteCommand(
			"delete from relationships where actor_id = @ActorId and target_id = @TargetId",
			parameters));
		_byActor.Change(actorId, actor, current => current.RemoveAll(item => item.Target.Id == targetId), deleted);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Relationship>> ListAsync(User actor, CancellationToken cancellationToken = default)
	{
		var liveActor = await users.GetAsync(actor.Id, cancellationToken) ?? actor;
		return await _byActor.GetOrLoadAsync(liveActor.Id, liveActor, async () =>
		{
			var rows = await database.ReadAsync(connection => connection.QueryAsync<RelationshipRow>(
				"select actor_id, target_id, type, created_at from relationships where actor_id = @ActorId",
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
						CreatedAt = row.CreatedAt
					});
			}

			return items.ToImmutable();
		});
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
			Type = (int)relationship.Type,
			CreatedAt = relationship.CreatedAt.ToUniversalTime()
		};
	}

	/// <summary>A stored row of the <c>relationships</c> table.</summary>
	private sealed class RelationshipRow
	{
		public int ActorId { get; set; }
		public int TargetId { get; set; }
		public int Type { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
	}
}