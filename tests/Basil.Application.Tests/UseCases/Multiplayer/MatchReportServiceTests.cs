using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Multiplayer;

public class MatchReportServiceTests
{
    private readonly IMatchPersistenceRepository _matchPersistence = Substitute.For<IMatchPersistenceRepository>();
    private readonly IMatchRegistry _matchRegistry = Substitute.For<IMatchRegistry>();
    private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private MatchReportService MakeService()
    {
        return new MatchReportService(_matchRegistry, _matchPersistence, _scores, _sessionRegistry);
    }

    private static MatchRow MakeMatchRow(int id = 5)
    {
        return new MatchRow(id, "Grand Finals",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
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
        _matchPersistence.FetchEventsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchEventRow>)[]);
        _matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

        var report = await MakeService().BuildAsync(5);

        Assert.NotNull(report);
        Assert.Null(report.Live);
    }

    [Fact]
    public async Task BuildAsync_InRegistry_IsLiveTrueWithSlotsAndCurrentMap()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[]);
        _matchPersistence.FetchEventsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchEventRow>)[]);

        var live = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 1, GameMode.Standard,
            Mods.NoMod, MatchWinCondition.Score, MatchTeamType.TeamVs, false, 0, "#mp_0") { DbId = 5 };
        live.Slots[0].PlayerId = 7;
        _matchRegistry.GetByDbId(5).Returns(live);

        var report = await MakeService().BuildAsync(5);

        Assert.NotNull(report);
        Assert.NotNull(report.Live);
        Assert.Equal(42, report.Live.CurrentMapId);
        Assert.Equal(7, report.Live.Slots[0].UserId);
    }

    [Fact]
    public async Task BuildAsync_RoundWithTeamScores_WinnerIsHigherScoringTeam()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        var round = new RoundRow(10, 5, 1, 42, new string('a', 32),
            0, 0, 0, "", "", "", "", false, 0,
            new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[round]);
        _matchPersistence.FetchEventsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchEventRow>)[]);
        _matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

        _scores.FetchByRoundIdAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RoundScoreRow>)
        [
            new RoundScoreRow(1, 7, "red-player", MatchTeam.Red, Mods.NoMod, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0,
                "S", true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new RoundScoreRow(2, 8, "blue-player", MatchTeam.Blue, Mods.NoMod, 300_000, 0.90, 700, 250, 20, 5, 0, 0, 0,
                "A", false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        ]);

        var report = await MakeService().BuildAsync(5);

        Assert.NotNull(report);
        var builtRound = Assert.Single(report.Rounds);
        Assert.Equal(MatchTeam.Red.ToString(), builtRound.WinnerTeam);
        Assert.Null(builtRound.WinnerUserId);
        Assert.Equal(2, builtRound.Scores.Length);
    }

    [Fact]
    public async Task BuildAsync_RoundWithoutTeams_WinnerIsTopScoringPlayer()
    {
        _matchPersistence.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
        var round = new RoundRow(10, 5, 1, 42, new string('a', 32),
            0, 0, 0, "", "", "", "", false, 0,
            new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
        _matchPersistence.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<RoundRow>)[round]);
        _matchPersistence.FetchEventsAsync(5, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchEventRow>)[]);
        _matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

        _scores.FetchByRoundIdAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<RoundScoreRow>)
        [
            new RoundScoreRow(1, 7, "player-one", null, Mods.NoMod, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0, "S", true,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new RoundScoreRow(2, 8, "player-two", null, Mods.NoMod, 600_000, 0.90, 700, 250, 20, 5, 0, 0, 0, "A", false,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        ]);

        var report = await MakeService().BuildAsync(5);

        var builtRound = Assert.Single(report!.Rounds);
        Assert.Equal(8, builtRound.WinnerUserId);
        Assert.Null(builtRound.WinnerTeam);
    }
}