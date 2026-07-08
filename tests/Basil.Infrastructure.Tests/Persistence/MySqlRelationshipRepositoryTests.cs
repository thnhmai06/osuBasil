using Basil.Application.Abstractions.Social;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/relationships.py — friends/blocks between users.</summary>
public class MySqlRelationshipRepositoryTests(MySqlFixture fixture) : IClassFixture<MySqlFixture>
{
    private readonly MySqlRelationshipRepository _repository = new(fixture.ConnectionString);
    private readonly MySqlUserRepository _users = new(fixture.ConnectionString);

    [Fact]
    public async Task Create_ThenFetchOne_ReturnsRelationship()
    {
        var friend = await _users.CreateAsync("rel friend", "rel-friend@example.test", "hash", "xx");

        await _repository.CreateAsync(1, friend.Id, RelationshipType.Friend);

        var relationship = await _repository.FetchOneAsync(1, friend.Id);
        Assert.NotNull(relationship);
        Assert.Equal(RelationshipType.Friend, relationship!.Type);
    }

    [Fact]
    public async Task FetchAll_FiltersByType()
    {
        var friend = await _users.CreateAsync("rel friend 2", "rel-friend2@example.test", "hash", "xx");
        var blocked = await _users.CreateAsync("rel blocked", "rel-blocked@example.test", "hash", "xx");
        await _repository.CreateAsync(1, friend.Id, RelationshipType.Friend);
        await _repository.CreateAsync(1, blocked.Id, RelationshipType.Block);

        var friends = await _repository.FetchAllAsync(1, RelationshipType.Friend);

        Assert.Contains(friends, r => r.User2 == friend.Id);
        Assert.DoesNotContain(friends, r => r.User2 == blocked.Id);
    }

    [Fact]
    public async Task Delete_RemovesRelationship()
    {
        var friend = await _users.CreateAsync("rel friend 3", "rel-friend3@example.test", "hash", "xx");
        await _repository.CreateAsync(1, friend.Id, RelationshipType.Friend);

        await _repository.DeleteAsync(1, friend.Id);

        Assert.Null(await _repository.FetchOneAsync(1, friend.Id));
    }

    [Fact]
    public async Task FetchOne_NoRelationship_ReturnsNull()
    {
        var stranger = await _users.CreateAsync("rel stranger", "rel-stranger@example.test", "hash", "xx");

        Assert.Null(await _repository.FetchOneAsync(1, stranger.Id));
    }
}