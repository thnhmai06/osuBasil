using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Users.Tests.Persistence;

/// <summary>Contract tests of the storing of the relationships between accounts.</summary>
[Collection("Persistence")]
public sealed class EfRelationshipRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset Moment = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private IRelationshipRepository Relationships => Get<IRelationshipRepository>();

	[Fact]
	public async Task A_listing_reads_no_more_relationships_than_the_ceiling()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "many_friends" }, TestContext.Current.CancellationToken);
		var targets = Enumerable.Range(1, ListLimits.Ceiling + 1).Select(g => 100_000_000 + g).ToArray();
		await StoredRows.InsertAsync(ConnectionString, "users", targets.Select(id =>
			(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
			{
				["id"] = id, ["name"] = "bulk" + id, ["normalized_name"] = "bulk" + id, ["active_name"] = "bulk" + id,
				["country"] = 0, ["permissions"] = 0L
			}), TestContext.Current.CancellationToken);
		// Each of those accounts is a target, one more than the ceiling.
		var stored = await StoredRows.InsertAsync(ConnectionString, "relationships", targets.Select(id =>
			(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
			{
				["actor_id"] = actor.Id, ["target_id"] = id, ["type"] = 0, ["created_at"] = DateTimeOffset.UtcNow
			}), TestContext.Current.CancellationToken);
		Assert.Equal(ListLimits.Ceiling + 1, stored);

		var listed = await Relationships.ListAsync(actor, TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count);
	}

	[Fact]
	public async Task A_relationship_is_added()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_one" }, TestContext.Current.CancellationToken);
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_one" }, TestContext.Current.CancellationToken);

		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		}, TestContext.Current.CancellationToken);
		await WaitForRelationshipsAsync(1);

		var listed = await Relationships.ListAsync(actor, TestContext.Current.CancellationToken);
		var relationship = Assert.Single(listed);
		Assert.Equal(target.Id, relationship.Target.Id);
		Assert.Equal(RelationshipType.Friend, relationship.Type);
	}

	[Fact]
	public async Task A_relationship_toward_an_account_the_database_does_not_hold_is_refused_to_the_caller()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_alone" }, TestContext.Current.CancellationToken);
		var unknown = new UserRecord { Id = 4242, Value = new UserData { Name = "not_stored" } };

		await Assert.ThrowsAnyAsync<Exception>(() => Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = actor,
			Target = unknown,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		}, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_relationship_toward_the_same_account_replaces_the_earlier_one()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_one" }, TestContext.Current.CancellationToken);
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_one" }, TestContext.Current.CancellationToken);
		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		}, TestContext.Current.CancellationToken);

		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Block,
			CreatedAt = Moment.AddHours(1)
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select type from relationships where actor_id = @actor and target_id = @target",
			new Dictionary<string, object?>
			{
				["@actor"] = actor.Id,
				["@target"] = target.Id
			},
			rows => rows.Count == 1 && rows[0].Number("type") == (int)RelationshipType.Block,
			"the replaced relationship", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_replaced_relationship_is_listed_once()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_one" }, TestContext.Current.CancellationToken);
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_one" }, TestContext.Current.CancellationToken);
		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		}, TestContext.Current.CancellationToken);
		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Block,
			CreatedAt = Moment.AddHours(1)
		}, TestContext.Current.CancellationToken);
		await WaitForRelationshipsAsync(1);

		var listed = await Relationships.ListAsync(actor, TestContext.Current.CancellationToken);

		Assert.Equal(RelationshipType.Block, Assert.Single(listed).Type);
	}

	[Fact]
	public async Task A_relationship_is_deleted()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_one" }, TestContext.Current.CancellationToken);
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_one" }, TestContext.Current.CancellationToken);
		var relationship = new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		};
		await Relationships.CreateOrUpdateAsync(relationship, TestContext.Current.CancellationToken);
		await WaitForRelationshipsAsync(1);

		await Relationships.DeleteAsync(relationship, TestContext.Current.CancellationToken);

		Assert.Empty(await Relationships.ListAsync(actor, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_deleted_relationship_is_stored_as_gone()
	{
		var actor = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_one" }, TestContext.Current.CancellationToken);
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_one" }, TestContext.Current.CancellationToken);
		var relationship = new Relationship
		{
			Actor = actor,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		};
		await Relationships.CreateOrUpdateAsync(relationship, TestContext.Current.CancellationToken);
		await WaitForRelationshipsAsync(1);

		await Relationships.DeleteAsync(relationship, TestContext.Current.CancellationToken);

		await StoredRows.WaitForAsync(ConnectionString,
			"select actor_id from relationships where actor_id = @actor and target_id = @target",
			new Dictionary<string, object?>
			{
				["@actor"] = actor.Id,
				["@target"] = target.Id
			},
			rows => rows.Count == 0, "the deleted relationship", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_user_lists_only_the_relationships_they_hold()
	{
		var first = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_one" }, TestContext.Current.CancellationToken);
		var second = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "actor_two" }, TestContext.Current.CancellationToken);
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_one" }, TestContext.Current.CancellationToken);
		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = first,
			Target = target,
			Type = RelationshipType.Friend,
			CreatedAt = Moment
		}, TestContext.Current.CancellationToken);
		await Relationships.CreateOrUpdateAsync(new Relationship
		{
			Actor = second,
			Target = target,
			Type = RelationshipType.Block,
			CreatedAt = Moment
		}, TestContext.Current.CancellationToken);
		await WaitForRelationshipsAsync(2);

		var firstList = await Relationships.ListAsync(first, TestContext.Current.CancellationToken);
		var secondList = await Relationships.ListAsync(second, TestContext.Current.CancellationToken);

		Assert.Equal(first.Id, Assert.Single(firstList).Actor.Id);
		Assert.Equal(second.Id, Assert.Single(secondList).Actor.Id);
	}

	/// <summary>Waits until the given number of relationships is stored.</summary>
	/// <param name="count">How many relationships to wait for.</param>
	private async Task WaitForRelationshipsAsync(int count)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select actor_id from relationships", null,
			rows => rows.Count == count, $"{count} stored relationships", TestContext.Current.CancellationToken);
	}
}