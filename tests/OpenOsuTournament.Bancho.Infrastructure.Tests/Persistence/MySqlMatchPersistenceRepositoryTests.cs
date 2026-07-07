using OpenOsuTournament.Bancho.Infrastructure.Persistence.Repositories;

namespace OpenOsuTournament.Bancho.Infrastructure.Tests.Persistence;

/// <summary>Covers the Matches/Rounds persistence added in Phase A and extended (read/delete) in Phase C.</summary>
public class MySqlMatchPersistenceRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlMatchPersistenceRepository _repository;

    public MySqlMatchPersistenceRepositoryTests(MySqlFixture fixture)
    {
        _repository = new MySqlMatchPersistenceRepository(fixture.ConnectionString);
    }

    private static readonly DateTime FixedTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateMatchThenFetch_RoundTrips()
    {
        var matchId = await _repository.CreateMatchAsync("Grand Finals", 0, 0, 1, 1, true, FixedTime);

        var row = await _repository.FetchMatchAsync(matchId);

        Assert.NotNull(row);
        Assert.Equal("Grand Finals", row!.Name);
        Assert.Equal(1, row.HostId);
        Assert.True(row.HasPublicHistory);
        Assert.Null(row.EndedAt);
    }

    [Fact]
    public async Task SetMatchEnded_PersistsEndedAt()
    {
        var matchId = await _repository.CreateMatchAsync("Ends Soon", 0, 0, 1, 1, true, FixedTime);

        await _repository.SetMatchEndedAsync(matchId, FixedTime.AddHours(1));

        var row = await _repository.FetchMatchAsync(matchId);
        Assert.Equal(FixedTime.AddHours(1), row!.EndedAt);
    }

    [Fact]
    public async Task CreateRoundThenFetchRounds_ReturnsOrderedByRoundIndex()
    {
        var matchId = await _repository.CreateMatchAsync("Multi-Round", 0, 0, 1, 1, true, FixedTime);
        var mapMd5 = new string('r', 32);
        await _repository.CreateRoundAsync(matchId, 2, 200, mapMd5, 0, FixedTime.AddMinutes(5));
        await _repository.CreateRoundAsync(matchId, 1, 100, mapMd5, 0, FixedTime);

        var rounds = await _repository.FetchRoundsAsync(matchId);

        Assert.Equal(2, rounds.Count);
        Assert.Equal(1, rounds[0].RoundIndex);
        Assert.Equal(100, rounds[0].BeatmapId);
        Assert.Equal(2, rounds[1].RoundIndex);
    }

    [Fact]
    public async Task SetRoundEnded_PersistsEndedAt()
    {
        var matchId = await _repository.CreateMatchAsync("Round End", 0, 0, 1, 1, true, FixedTime);
        var roundId = await _repository.CreateRoundAsync(matchId, 1, 100, new string('s', 32), 0, FixedTime);

        await _repository.SetRoundEndedAsync(roundId, FixedTime.AddMinutes(3));

        var rounds = await _repository.FetchRoundsAsync(matchId);
        Assert.Equal(FixedTime.AddMinutes(3), rounds[0].EndedAt);
    }

    [Fact]
    public async Task FetchAllMatches_OrdersByIdDescending()
    {
        var first = await _repository.CreateMatchAsync("First", 0, 0, 1, 1, true, FixedTime);
        var second = await _repository.CreateMatchAsync("Second", 0, 0, 1, 1, true, FixedTime);

        var all = await _repository.FetchAllMatchesAsync();

        var index1 = all.ToList().FindIndex(m => m.Id == first);
        var index2 = all.ToList().FindIndex(m => m.Id == second);
        Assert.True(index2 < index1);
    }

    [Fact]
    public async Task DeleteMatch_RemovesMatchAndItsRounds()
    {
        var matchId = await _repository.CreateMatchAsync("To Delete", 0, 0, 1, 1, true, FixedTime);
        await _repository.CreateRoundAsync(matchId, 1, 100, new string('d', 32), 0, FixedTime);

        await _repository.DeleteMatchAsync(matchId);

        Assert.Null(await _repository.FetchMatchAsync(matchId));
        Assert.Empty(await _repository.FetchRoundsAsync(matchId));
    }

    [Fact]
    public async Task FetchMatch_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.FetchMatchAsync(999_999));
    }
}
