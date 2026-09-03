using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

public class MatchReportServiceTests
{
	private readonly IBeatmapRepository _beatmaps = Substitute.For<IBeatmapRepository>();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly IMatchRegistry _matchRegistry = Substitute.For<IMatchRegistry>();
	private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();
	private readonly IScoreRepository _scores = Substitute.For<IScoreRepository>();
	private readonly IUserRepository _users = Substitute.For<IUserRepository>();

	public MatchReportServiceTests()
	{
		// No session-registry setup in these tests means every userSession is "offline" — fall back to a
		// generic resolvable user so UserBriefResolver never returns null for the ids exercised here.
		_users.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(call => new User(call.Arg<int>(), $"player{call.Arg<int>()}", Country.Xx,
				UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));
	}

	private MatchReportService MakeService()
	{
		return new MatchReportService(_matchRegistry, _matchRepository, _scores, _gameRegistry, _ircRegistry, _users,
			_beatmaps);
	}

	private static Match MakeMatchRow(int id = 5)
	{
		return new Match(id, "Grand Finals",
			new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null);
	}

	[Fact]
	public async Task BuildAsync_UnknownMatch_ReturnsNull()
	{
		_matchRepository.FetchMatchAsync(999, Arg.Any<CancellationToken>()).Returns((Match?)null);

		var report = await MakeService().BuildAsync(999);

		Assert.Null(report);
	}

	[Fact]
	public async Task BuildAsync_NotInRegistry_IsLiveFalseAndNoSlots()
	{
		_matchRepository.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
		_matchRepository.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Round>)[]);
		_matchRepository.FetchEventsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<MatchEvent>)[]);
		_matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

		var report = await MakeService().BuildAsync(5);

		Assert.NotNull(report);
		Assert.Null(report.Live);
	}

	[Fact]
	public async Task BuildAsync_InRegistry_IsLiveTrueWithSlotsAndCurrentMap()
	{
		_matchRepository.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
		_matchRepository.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Round>)[]);
		_matchRepository.FetchEventsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<MatchEvent>)[]);

		var live = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 1, GameMode.Standard,
				Mods.NoMod, MatchWinCondition.Score, MatchTeamType.TeamVs, false, 0, "#mp_0")
			{ DbId = 5 };
		live.Slots[0].PlayerId = 7;
		_matchRegistry.GetByDbId(5).Returns(live);

		var mapset = new Beatmapset(1, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var beatmap = new Beatmap("md5", 42, mapset, "Normal", "diff.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(100), 4, 9, 8, 5, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		_beatmaps.FetchOneAsync(null, "md5", null, null, true, Arg.Any<CancellationToken>()).Returns(beatmap);

		var report = await MakeService().BuildAsync(5);

		Assert.NotNull(report);
		Assert.NotNull(report.Live);
		Assert.Equal(42, report.Live.MapId);
	}

	[Fact]
	public async Task BuildAsync_RoundWithTeamScores_WinnerIsHigherScoringTeam()
	{
		_matchRepository.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
		var round = new Round(10, 5, 1, new string('a', 32),
			0, 0, 0, false, 0,
			new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
		_matchRepository.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Round>)[round]);
		_matchRepository.FetchEventsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<MatchEvent>)[]);
		_matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

		_scores.FetchByRoundAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScoreReport>)
		[
			new ScoreReport(1, 7, "red-userSession", MatchTeam.Red, Mods.NoMod, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0,
				"S", true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
			new ScoreReport(2, 8, "blue-userSession", MatchTeam.Blue, Mods.NoMod, 300_000, 0.90, 700, 250, 20, 5, 0, 0,
				0,
				"A", false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
		]);

		var report = await MakeService().BuildAsync(5);

		Assert.NotNull(report);
		var builtRound = Assert.Single(report.Rounds);
		Assert.Equal(MatchTeam.Red, builtRound.WinnerTeam);
		Assert.Null(builtRound.Winner);
		Assert.Equal(2, builtRound.Scores.Count);
	}

	[Fact]
	public async Task BuildAsync_RoundWithoutTeams_WinnerIsTopScoringPlayer()
	{
		_matchRepository.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
		var round = new Round(10, 5, 1, new string('a', 32),
			0, 0, 0, false, 0,
			new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
		_matchRepository.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Round>)[round]);
		_matchRepository.FetchEventsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<MatchEvent>)[]);
		_matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

		_scores.FetchByRoundAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScoreReport>)
		[
			new ScoreReport(1, 7, "userSession-one", null, Mods.NoMod, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0, "S",
				true,
				new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
			new ScoreReport(2, 8, "userSession-two", null, Mods.NoMod, 600_000, 0.90, 700, 250, 20, 5, 0, 0, 0, "A",
				false,
				new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
		]);

		var report = await MakeService().BuildAsync(5);

		var builtRound = Assert.Single(report!.Rounds);
		Assert.Equal(8, builtRound.Winner!.Id);
		Assert.Null(builtRound.WinnerTeam);
	}

	/// <summary>
	///     Regression test for the N+1 fix: the same player and the same beatmap recurring across
	///     several rounds must each be resolved from the repositories only once per report build,
	///     not once per round/score that references them.
	/// </summary>
	[Fact]
	public async Task BuildAsync_SameUserAndBeatmapAcrossRounds_ResolvesEachOnlyOnce()
	{
		_matchRepository.FetchMatchAsync(5, Arg.Any<CancellationToken>()).Returns(MakeMatchRow());
		var beatmapMd5 = new string('a', 32);
		var round1 = new Round(10, 5, 1, beatmapMd5, 0, 0, 0, false, 0,
			new DateTime(2026, 1, 1, 0, 0, 5, DateTimeKind.Utc), null);
		var round2 = new Round(11, 5, 2, beatmapMd5, 0, 0, 0, false, 0,
			new DateTime(2026, 1, 1, 0, 1, 5, DateTimeKind.Utc), null);
		_matchRepository.FetchRoundsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Round>)[round1, round2]);
		_matchRepository.FetchEventsAsync(5, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<MatchEvent>)[]);
		_matchRegistry.GetByDbId(5).Returns((MatchSession?)null);

		var score = new ScoreReport(1, 7, "player7", null, Mods.NoMod, 500_000, 0.98, 800, 300, 10, 0, 0, 0, 0, "S",
			true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
		_scores.FetchByRoundAsync(10, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScoreReport>)[score]);
		_scores.FetchByRoundAsync(11, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<ScoreReport>)[score]);

		var mapset = new Beatmapset(1, "Artist", "Title", "creator", DateTime.UtcNow, DateTime.UtcNow);
		var beatmap = new Beatmap(beatmapMd5, 100, mapset, "Normal", "diff.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromSeconds(100), 4, 9, 8, 5, 6.5),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
		_beatmaps.FetchOneAsync(null, beatmapMd5, null, null, true, Arg.Any<CancellationToken>()).Returns(beatmap);
		_beatmaps.FetchAllBySetIdAsync(1, true, Arg.Any<CancellationToken>())
			.Returns((IReadOnlyList<Beatmap>)[beatmap]);

		var report = await MakeService().BuildAsync(5);

		Assert.NotNull(report);
		Assert.Equal(2, report.Rounds.Count);
		await _users.Received(1).FetchByIdAsync(7, Arg.Any<CancellationToken>());
		await _beatmaps.Received(1)
			.FetchOneAsync(null, beatmapMd5, null, null, true, Arg.Any<CancellationToken>());
		await _beatmaps.Received(1).FetchAllBySetIdAsync(1, true, Arg.Any<CancellationToken>());
	}
}