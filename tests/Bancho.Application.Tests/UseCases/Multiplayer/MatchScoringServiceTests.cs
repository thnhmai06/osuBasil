using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using NSubstitute;
using static Bancho.Application.Tests.PacketHandlers.MultiplayerTestSupport;

namespace Bancho.Application.Tests.UseCases.Multiplayer;

/// <summary>Ported from Match.await_submissions + Match.update_matchpoints — scrim's per-round winner determination.</summary>
public class MatchScoringServiceTests
{
    private static Beatmap MakeBeatmap(string md5, int totalLength = 60) => new(
        Md5: md5, Id: 1, SetId: 1, Artist: "A", Title: "T", Version: "V", Creator: "C",
        LastUpdate: DateTime.UtcNow, TotalLength: totalLength, MaxCombo: 100, Status: RankedStatus.Ranked,
        Frozen: false, Plays: 0, Passes: 0, Mode: GameMode.VanillaOsu, Bpm: 120, Cs: 4, Od: 8, Ar: 9,
        Hp: 5, Diff: 5.0, Filename: "map.osu");

    private static MatchRoundSnapshot MakeSnapshot(
        IReadOnlyList<(int, MatchTeams)> wasPlaying, string mapMd5, MatchTeamTypes teamType,
        MatchWinConditions winCondition = MatchWinConditions.Score, string matchName = "test match") =>
        new(wasPlaying, mapMd5, teamType, winCondition, matchName);

    [Fact]
    public async Task ScoreCompletedRound_Ffa_HigherScoreWinsAndAccumulatesMatchPoint()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('a', 32);
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        host.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 500_000, 98.0, 400);
        guest.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 300_000, 95.0, 200);
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns(MakeBeatmap(mapMd5));
        var service = fixture.MakeScoringService();
        var snapshot = MakeSnapshot([(host.Id, MatchTeams.Neutral), (guest.Id, MatchTeams.Neutral)], mapMd5, MatchTeamTypes.HeadToHead);

        await service.ScoreCompletedRoundAsync(match, snapshot);

        Assert.Equal(1, match.GetMatchPoints(ScrimParticipant.OfPlayer(host.Id)));
        Assert.Single(match.Winners);
        Assert.Equal(host.Id, match.Winners[0]!.Value.PlayerId);
    }

    [Fact]
    public async Task ScoreCompletedRound_EqualScores_RecordsATieAndNoMatchPoint()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('b', 32);
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        host.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 400_000, 95.0, 300);
        guest.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 400_000, 95.0, 300);
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns(MakeBeatmap(mapMd5));
        var service = fixture.MakeScoringService();
        var snapshot = MakeSnapshot([(host.Id, MatchTeams.Neutral), (guest.Id, MatchTeams.Neutral)], mapMd5, MatchTeamTypes.HeadToHead);

        await service.ScoreCompletedRoundAsync(match, snapshot);

        Assert.Single(match.Winners);
        Assert.Null(match.Winners[0]);
        Assert.Empty(match.MatchPoints);
    }

    [Fact]
    public async Task ScoreCompletedRound_Teams_WinningTeamGetsMatchPointAndTeamNamesFromMatchName()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('c', 32);
        var blue = MakePlayer(1, "host");
        var red = MakePlayer(2, "guest");
        fixture.RegisterAll(blue, red);
        var match = fixture.CreateMatch(blue);
        match.Name = "OWC2020: (United States) vs. (China)";
        blue.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 500_000, 98.0, 400);
        red.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 300_000, 95.0, 200);
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns(MakeBeatmap(mapMd5));
        var service = fixture.MakeScoringService();
        var snapshot = MakeSnapshot([(blue.Id, MatchTeams.Blue), (red.Id, MatchTeams.Red)], mapMd5, MatchTeamTypes.TeamVs);

        await service.ScoreCompletedRoundAsync(match, snapshot);

        Assert.Equal(1, match.GetMatchPoints(ScrimParticipant.OfTeam(MatchTeams.Blue)));
        Assert.Equal(0, match.GetMatchPoints(ScrimParticipant.OfTeam(MatchTeams.Red)));
    }

    [Fact]
    public async Task ScoreCompletedRound_ReachingWinningPoints_EndsScrimAndResets()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('d', 32);
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        match.IsScrimming = true;
        match.WinningPoints = 1; // first point scored this round already reaches it
        host.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 500_000, 98.0, 400);
        guest.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 300_000, 95.0, 200);
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns(MakeBeatmap(mapMd5));
        var service = fixture.MakeScoringService();
        var snapshot = MakeSnapshot([(host.Id, MatchTeams.Neutral), (guest.Id, MatchTeams.Neutral)], mapMd5, MatchTeamTypes.HeadToHead);

        await service.ScoreCompletedRoundAsync(match, snapshot);

        Assert.False(match.IsScrimming);
        Assert.Empty(match.MatchPoints); // reset_scrim cleared it after the win
    }

    [Fact]
    public async Task ScoreCompletedRound_PlayerNeverSubmits_TimesOutAndIsExcluded()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('e', 32);
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        fixture.RegisterAll(host, guest);
        var match = fixture.CreateMatch(host);
        host.RecentScore = new RecentScoreSnapshot(mapMd5, DateTime.UtcNow, 500_000, 98.0, 400);
        // guest never submits a score for this map
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns(MakeBeatmap(mapMd5, totalLength: 0));
        var service = fixture.MakeScoringService(pollInterval: TimeSpan.FromMilliseconds(2), pollTimeout: TimeSpan.FromMilliseconds(10));
        var snapshot = MakeSnapshot([(host.Id, MatchTeams.Neutral), (guest.Id, MatchTeams.Neutral)], mapMd5, MatchTeamTypes.HeadToHead);

        await service.ScoreCompletedRoundAsync(match, snapshot);

        Assert.Single(match.Winners);
        Assert.Equal(host.Id, match.Winners[0]!.Value.PlayerId); // guest timed out, host's score alone still wins
    }

    [Fact]
    public async Task ScoreCompletedRound_BeatmapNotFound_RecordsNoWinnerAndDoesNotThrow()
    {
        var fixture = new Fixture();
        var mapMd5 = new string('f', 32);
        var host = MakePlayer(1, "host");
        fixture.RegisterAll(host);
        var match = fixture.CreateMatch(host);
        fixture.MapRepository.FetchOneAsync(md5: mapMd5).Returns((Beatmap?)null);
        var service = fixture.MakeScoringService();
        var snapshot = MakeSnapshot([(host.Id, MatchTeams.Neutral)], mapMd5, MatchTeamTypes.HeadToHead);

        await service.ScoreCompletedRoundAsync(match, snapshot);

        Assert.Empty(match.Winners);
    }
}
