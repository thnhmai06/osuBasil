using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Covers the Matches/Rounds persistence added in Phase A and extended (read/delete) in Phase C.</summary>
public class SqliteMatchPersistenceRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private static readonly DateTime FixedTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly SqliteMatchPersistenceRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task CreateMatchThenFetch_RoundTrips()
    {
        var matchId = await _repository.CreateMatchAsync("Grand Finals", FixedTime);

        var row = await _repository.FetchMatchAsync(matchId);

        Assert.NotNull(row);
        Assert.Equal("Grand Finals", row.Name);
        Assert.Null(row.EndedAt);
    }

    [Fact]
    public async Task SetMatchEnded_PersistsEndedAt()
    {
        var matchId = await _repository.CreateMatchAsync("Ends Soon", FixedTime);

        await _repository.SetMatchEndedAsync(matchId, FixedTime.AddHours(1));

        var row = await _repository.FetchMatchAsync(matchId);
        Assert.Equal(FixedTime.AddHours(1), row!.EndedAt);
    }

    [Fact]
    public async Task CreateRoundThenFetchRounds_ReturnsOrderedByRoundIndex()
    {
        var matchId = await _repository.CreateMatchAsync("Multi-Round", FixedTime);
        var mapMd5 = new string('r', 32);
        await _repository.CreateRoundAsync(matchId, 2, mapMd5, GameMode.Standard, MatchWinCondition.Score, MatchTeamType.HeadToHead, Mods.NoMod, FixedTime.AddMinutes(5));
        await _repository.CreateRoundAsync(matchId, 1, mapMd5, GameMode.Standard, MatchWinCondition.Score, MatchTeamType.HeadToHead, Mods.NoMod, FixedTime);

        var rounds = await _repository.FetchRoundsAsync(matchId);

        Assert.Equal(2, rounds.Count);
        Assert.Equal(1, rounds[0].RoundIndex);
        Assert.Equal(2, rounds[1].RoundIndex);
    }

    [Fact]
    public async Task SetRoundEnded_PersistsEndedAt()
    {
        var matchId = await _repository.CreateMatchAsync("Round End", FixedTime);
        var roundId = await _repository.CreateRoundAsync(matchId, 1, new string('s', 32), GameMode.Standard, MatchWinCondition.Score, MatchTeamType.HeadToHead, Mods.NoMod, FixedTime);

        await _repository.SetRoundEndedAsync(roundId, FixedTime.AddMinutes(3), false);

        var rounds = await _repository.FetchRoundsAsync(matchId);
        Assert.Equal(FixedTime.AddMinutes(3), rounds[0].EndedAt);
    }

    [Fact]
    public async Task FetchAllMatches_OrdersByIdDescending()
    {
        var first = await _repository.CreateMatchAsync("First", FixedTime);
        var second = await _repository.CreateMatchAsync("Second", FixedTime);

        var all = await _repository.FetchAllMatchesAsync();

        var index1 = all.ToList().FindIndex(m => m.Id == first);
        var index2 = all.ToList().FindIndex(m => m.Id == second);
        Assert.True(index2 < index1);
    }

    [Fact]
    public async Task DeleteMatch_RemovesMatchAndItsRounds()
    {
        var matchId = await _repository.CreateMatchAsync("To Delete", FixedTime);
        await _repository.CreateRoundAsync(matchId, 1, new string('d', 32), GameMode.Standard, MatchWinCondition.Score, MatchTeamType.HeadToHead, Mods.NoMod, FixedTime);

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