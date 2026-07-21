using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/channels.py, scoped to what login needs: the auto-join channel
///     list. migrations/base.sql seeds 2 channels (#osu, #lobby), only #osu has auto_join=true.
/// </summary>
public class SqliteChannelRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteChannelRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task FetchAllAutoJoin_ReturnsOnlyAutoJoinChannels()
    {
        var channels = await _repository.FetchAllAutoJoinAsync();

        Assert.Contains(channels, c => c.Name == "#osu");
        Assert.DoesNotContain(channels, c => c.Name == "#lobby");
        Assert.All(channels, c => Assert.True(c.AutoJoin));
    }

    [Fact]
    public async Task FetchOneByName_SeededChannel_ReturnsChannel()
    {
        var channel = await _repository.FetchOneByNameAsync("#osu");

        Assert.NotNull(channel);
        Assert.Equal("General discussion.", channel.Topic);
    }

    [Fact]
    public async Task FetchOneByName_Unknown_ReturnsNull()
    {
        Assert.Null(await _repository.FetchOneByNameAsync("#does-not-exist"));
    }
}