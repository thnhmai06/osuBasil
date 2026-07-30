using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/channels.py. migrations/base.sql seeds 2 channels (#osu, #lobby),
///     only #osu has auto_join=true.
/// </summary>
public class SqliteChannelRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteChannelRepository _repository = new(fixture.ConnectionString);

	[Fact]
	public async Task FetchAll_ReturnsEveryChannelRegardlessOfAutoJoin()
	{
		var channels = await _repository.FetchAllAsync();

		Assert.Contains(channels, c => c.Name == "#osu" && c.AutoJoin);
		Assert.Contains(channels, c => c.Name == "#lobby" && !c.AutoJoin);
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