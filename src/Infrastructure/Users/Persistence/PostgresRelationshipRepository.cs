using System.Collections.Immutable;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Registry;
using Microsoft.EntityFrameworkCore;

namespace Basil.Infrastructure.Users.Persistence;

/// <summary>Stores the friends and blocks users set toward each other.</summary>
internal sealed class PostgresRelationshipRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	IUserRepository users) : IRelationshipRepository
{
	private readonly OwnedLists<int, UserRecord, Relationship> _byActor = new();

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var actor = await users.GetAsync(actorId, cancellationToken);
		var row = new RelationshipRow
		{
			ActorId = actorId,
			TargetId = targetId,
			Type = (int)relationship.Type,
			CreatedAt = relationship.CreatedAt.ToUniversalTime()
		};
		var saved = writer.SaveAsync(Root.User(actorId), (GetType(), (actorId, targetId)),
			new WriteCommand<UsersDbContext>((context, token) =>
				context.UpsertAsync(row, stored => stored.ActorId == actorId && stored.TargetId == targetId, token)));
		_byActor.Change(actorId, actor, current => Replace(current, relationship), saved);
	}

	/// <inheritdoc />
	public async Task DeleteAsync(Relationship relationship, CancellationToken cancellationToken = default)
	{
		var actorId = relationship.Actor.Id;
		var targetId = relationship.Target.Id;
		var actor = await users.GetAsync(actorId, cancellationToken);
		var saved = writer.SaveAsync(Root.User(actorId), (GetType(), (actorId, targetId)),
			new WriteCommand<UsersDbContext>(async (context, token) =>
			{
				await context.Relationships
					.Where(r => r.ActorId == actorId && r.TargetId == targetId)
					.ExecuteDeleteAsync(token);
			}));
		_byActor.Change(actorId, actor, current => current.RemoveAll(item => item.Target.Id == targetId), saved);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Relationship>> ListAsync(UserRecord actor,
		CancellationToken cancellationToken = default)
	{
		var liveActor = await users.GetAsync(actor.Id, cancellationToken) ?? actor;
		return await _byActor.GetOrLoadAsync(liveActor.Id, liveActor, async () =>
		{
			var rows = await reader.ReadAsync((UsersDbContext context) => context.Relationships
				.Where(row => row.ActorId == liveActor.Id)
				.ToListAsync(cancellationToken), cancellationToken);

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
}