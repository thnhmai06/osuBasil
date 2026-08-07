using Basil.Domain.Login;
using Basil.Domain.Social;
using Basil.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Verifies `SqliteRelationshipRepository`'s friend/block relationship CRUD between users.</summary>
public class SqliteRelationshipRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteRelationshipRepository _repository = new(fixture.ConnectionString,
		NullLogger<SqliteRelationshipRepository>.Instance);

	private readonly SqliteUserRepository _users = new(fixture.ConnectionString,
		NullLogger<SqliteUserRepository>.Instance);

	[Fact]
	public async Task Create_ThenFetchOne_ReturnsRelationship()
	{
		var friend = await _users.CreateAsync("rel friend", "hash", Country.Xx);

		await _repository.CreateAsync(1, friend!.Id, RelationshipType.Friend);

		var relationship = await _repository.FetchOneAsync(1, friend.Id);
		Assert.NotNull(relationship);
		Assert.Equal(RelationshipType.Friend, relationship.Type);
	}

	[Fact]
	public async Task FetchAll_FiltersByType()
	{
		var friend = await _users.CreateAsync("rel friend 2", "hash", Country.Xx);
		var blocked = await _users.CreateAsync("rel blocked", "hash", Country.Xx);
		await _repository.CreateAsync(1, friend!.Id, RelationshipType.Friend);
		await _repository.CreateAsync(1, blocked!.Id, RelationshipType.Block);

		var friends = await _repository.FetchAllAsync(1, RelationshipType.Friend);

		Assert.Contains(friends, r => r.User2 == friend.Id);
		Assert.DoesNotContain(friends, r => r.User2 == blocked.Id);
	}

	[Fact]
	public async Task Delete_RemovesRelationship()
	{
		var friend = await _users.CreateAsync("rel friend 3", "hash", Country.Xx);
		await _repository.CreateAsync(1, friend!.Id, RelationshipType.Friend);

		await _repository.DeleteAsync(1, friend.Id);

		Assert.Null(await _repository.FetchOneAsync(1, friend.Id));
	}

	[Fact]
	public async Task FetchOne_NoRelationship_ReturnsNull()
	{
		var stranger = await _users.CreateAsync("rel stranger", "hash", Country.Xx);

		Assert.Null(await _repository.FetchOneAsync(1, stranger!.Id));
	}
}