using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.UseCases.Multiplayer;
using Basil.Domain;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Multiplayer;

public class MatchReportServiceTests
{
    private readonly IMatchPersistenceRepository _matchPersistence = Substitute.For<IMatchPersistenceRepository>();
    private readonly IMatchRegistry _matchRegistry = Substitute.For<IMatchRegistry>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();

    private MatchReportService MakeService()
    {
        return new MatchReportService(_matchRegistry, _matchPersistence, _scores);
    }

    private static MatchRow MakeMatchRow(int id = 5)
    {
        return new MatchRow(id, "Grand Finals", (int)GameMode.VanillaOsu, (int)MatchWinConditions.Score,
            (int)MatchTeamTypes.TeamVs, 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
    }

    [Fact]
    public async Task BuildAsync_UnknownMatch_ReturnsNull()
    {
        _matchPersistence.FetchMatchAsync(999, Arg.Any<CancellationToken>()).Returns((MatchRow?)null);

        var report = await MakeService().BuildAsync(999);

        Assert.Null(report);
    }

    [Fact]
    public async Task BuildAsync_NotInRegistry_IsLiveFalseAndNoSlots()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[]);
        _matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

        var report = await MakeService().BuildAsync(5);

        Assert.NotNull(report);
        Assert.False(report.IsLive);
        Assert.Null(report.LiveSlots);
        Assert.Null(report.CurrentMapId);
    }

    [Fact]
    public async Task BuildAsync_InRegistry_IsLiveTrueWithSlotsAndCurrentMap()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[]);

        var live = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 1, GameMode.VanillaOsu,
            Mods.NoMod, MatchWinConditions.Score, MatchTeamTypes.TeamVs, false, 0, "#mp_0") { DbId = 5 };
        live.Slots[0].PlayerId = 7;
        _matchRegistry.GetByDbId(5).Returns(live);

        var report = await MakeService().BuildAsync(5);

        Assert.NotNull(report);
        Assert.True(report.IsLive);
        Assert.Equal(42, report.CurrentMapId);
        Assert.NotNull(report.LiveSlots);
        Assert.Equal(7, report.LiveSlots![0].UserId);
    }

    [Fact]
    public async Task BuildAsync_RoundWithTeamScores_WinnerIsHigherScoringTeam()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        var round = new RoundRow(10, 5, 1, 42, new string('a', 32), 0,
            new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[round]);
        _matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

        _scores.FetchByRoundIdAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RoundScoreRow>)
        [
            new RoundScoreRow(1, 7, "red-player", (int)MatchTeams.Red, 0, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0,
                "S", true),
            new RoundScoreRow(2, 8, "blue-player", (int)MatchTeams.Blue, 0, 300_000, 0.90, 700, 250, 20, 5, 0, 0, 0,
                "A", false)
        ]);

        var report = await MakeService().BuildAsync(5);

        Assert.NotNull(report);
        var builtRound = Assert.Single(report.Rounds);
        Assert.Equal(MatchTeams.Red.ToString(), builtRound.WinnerTeam);
        Assert.Null(builtRound.WinnerUserId);
        Assert.Equal(2, builtRound.Scores.Length);
    }

    [Fact]
    public async Task BuildAsync_RoundWithoutTeams_WinnerIsTopScoringPlayer()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        var round = new RoundRow(10, 5, 1, 42, new string('a', 32), 0,
            new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[round]);
        _matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

        _scores.FetchByRoundIdAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RoundScoreRow>)
        [
            new RoundScoreRow(1, 7, "player-one", null, 0, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0, "S", true),
            new RoundScoreRow(2, 8, "player-two", null, 0, 600_000, 0.90, 700, 250, 20, 5, 0, 0, 0, "A", false)
        ]);

        var report = await MakeService().BuildAsync(5);

        var builtRound = Assert.Single(report!.Rounds);
        Assert.Equal(8, builtRound.WinnerUserId);
        Assert.Null(builtRound.WinnerTeam);
    }
}