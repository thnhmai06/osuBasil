using Basil.Application.Content.Persistence;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Infrastructure.Multiplayer.Handlers;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Objects.Rooms;

/// <summary>
///     How a room takes the scores of a round and decides it. The expected behaviour comes from the plan's D16-D18: a
///     score belongs to a round when its player took part, on the round's beatmap, before the round is decided; a
///     player who left before completing is not awaited, one who completed and then left still is; the round is decided
///     once it has ended and every awaited player has a score, or ten seconds after it ended; an aborted round is
///     never decided.
/// </summary>
public sealed class RoomScoringTests
{
	/// <summary>The wait for scores the server settings default to, 10 seconds.</summary>
	private static readonly TimeSpan ScoreWait = TimeSpan.FromSeconds(10);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<(TestApplication App, RoomFixture Setup)> StartedAsync(
		GameTeamType teamType = GameTeamType.HeadToHead)
	{
		var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap, TeamType: teamType))).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync()).IsOk);
		return (app, setup);
	}

	private static async Task CompleteAsync(RoomFixture setup, params StableConnection[] players)
	{
		foreach (var player in players)
			Assert.True((await setup.Room.ScopeTo(player).Rounds.CompleteAsync()).IsOk);
	}

	private static ScoreRecord ScoreOf(int id, StableConnection player, int total = 1000)
	{
		return RoomFixture.PlayerScore(id, player.Record, RoomFixture.Beatmap.Hash, total);
	}

	[Fact]
	public async Task ParticipantRoundBeatmap_TakenAsScore()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		var result = setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		Assert.Equal(RoomOutcome.Ok, result);
		var taken = Assert.IsType<RoomRoundScoreSubmitted>(app.RoomEvents.Events[^1]);
		Assert.Equal(setup.Room.Rounds.LastRound, taken.Round);
		Assert.Equal(setup.Player.Record, taken.Player);
		Assert.Null(taken.Score.Team);
	}

	[Fact]
	public async Task TeamRound_CarriesSlotTeam()
	{
		var (app, setup) = await StartedAsync(GameTeamType.TeamVs);
		using var _ = app;
		var team = setup.Room.Slots.Find(setup.Player)!.Team;

		setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		Assert.NotNull(team);
		Assert.Equal(team, Assert.IsType<RoomRoundScoreSubmitted>(app.RoomEvents.Events[^1]).Score.Team);
	}

	[Fact]
	public async Task OnAnotherBeatmap_IsRefused()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		var other = RoomFixture.PlayerScore(1, setup.Player.Record, new Md5("11111111111111111111111111111111"));

		var result = setup.Room.ReceiveScore(other, setup.Player.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, result);
	}

	[Fact]
	public async Task NonParticipant_Refused()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		var result = setup.Room.ReceiveScore(ScoreOf(1, setup.Outsider), setup.Outsider.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, result);
	}

	[Fact]
	public async Task SecondScore_Refused()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		var result = setup.Room.ReceiveScore(ScoreOf(2, setup.Player), setup.Player.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, result);
	}

	[Fact]
	public async Task EndedAllScored_DecidedAtOnce()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator, 3000), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host, 2000), setup.Host.Record);
		setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record);
		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());

		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Empty(decided.Missing);
		var result = Assert.IsType<HeadToHeadResult>(decided.Result);
		Assert.Equal(setup.Creator.Record.Id, result.WinnerId);
		Assert.Same(result, decided.Round.Result);
	}

	[Fact]
	public async Task EndEvent_DecidedAfter()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);
		setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record);

		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		var events = app.RoomEvents.Events;
		Assert.True(events.ToList().FindIndex(e => e is RoomRoundCompleted) <
		            events.ToList().FindIndex(e => e is RoomRoundDecided));
	}

	[Fact]
	public async Task MissingScore_DecidedBeforeNext()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		var first = setup.Room.Rounds.LastRound!;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync()).IsOk);

		var events = app.RoomEvents.Events;
		var decided = Assert.Single(events.OfType<RoomRoundDecided>());
		Assert.Same(first, decided.Round);
		Assert.Equal(2, decided.Missing.Count);
		Assert.True(events.ToList().FindIndex(e => e is RoomRoundDecided) <
		            events.ToList().FindLastIndex(e => e is RoomRoundStarted));
		app.Clock.Advance(ScoreWait * 2);
		Assert.Single(events.OfType<RoomRoundDecided>());
	}

	[Fact]
	public async Task LateScore_WaitsThenDecides()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);
		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());

		setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record);

		Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
	}

	[Fact]
	public async Task TenSecondsAfter_DecidedWithScores()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		app.Clock.Advance(ScoreWait - TimeSpan.FromMilliseconds(1));
		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		app.Clock.Advance(TimeSpan.FromMilliseconds(1));

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Equal([setup.Host.Record, setup.Player.Record], decided.Missing.OrderBy(user => user.Id));
		Assert.Single(decided.Result!.Scores);
	}

	[Fact]
	public async Task EndAnnounced_ScoreWaitFirst()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		var ended = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundEnded>());
		Assert.Same(setup.Room.Rounds.LastRound, ended.Round);
		Assert.Equal(ScoreWait, ended.ScoreWait);
		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());
	}

	[Fact]
	public async Task ScoreWait_UsesServerSettings()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await app.SetSettingsAsync(new ServerSettings
		{
			Multiplayer = new ServerSettings.MultiplayerSettings
			{
				Rounds = new ServerSettings.RoundSettings
					{ ScoreWait = TimeSpan.FromSeconds(3) }
			}
		});
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		app.Clock.Advance(TimeSpan.FromSeconds(3));

		Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(app.RoomEvents.Events.OfType<RoomRoundEnded>()).ScoreWait);
		Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
	}

	[Fact]
	public async Task NoScores_DecidedNamesEveryone()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		app.Clock.Advance(ScoreWait);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Null(decided.Result);
		Assert.Equal(3, decided.Missing.Count);
	}

	[Fact]
	public async Task AnAbortedRound_IsNeverDecided()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.AbortAsync()).IsOk);

		app.Clock.Advance(ScoreWait * 2);
		var late = setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);

		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Equal(RoomOutcome.RoundMismatch, late);
	}

	[Fact]
	public async Task DecidedRound_NoMoreScores()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);
		app.Clock.Advance(ScoreWait);

		var late = setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, late);
	}

	[Fact]
	public async Task LeftBeforeCompleting_NotAwaited()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		Assert.True((await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync()).IsOk);
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);

		await CompleteAsync(setup, setup.Creator, setup.Host);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Empty(decided.Missing);
		Assert.Equal(RoomOutcome.RoundMismatch,
			setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record));
	}

	[Fact]
	public async Task EveryoneLeft_NotAnnounced()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		foreach (var player in new[] { setup.Player, setup.Host, setup.Creator })
			Assert.True((await setup.Room.ScopeTo(player).Members.LeaveAsync()).IsOk);

		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundEnded>());
	}

	[Fact]
	public async Task CompletedThenLeft_StillAwaited()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await CompleteAsync(setup, setup.Player);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync()).IsOk);
		await CompleteAsync(setup, setup.Creator, setup.Host);
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);
		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());

		setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Equal(3, decided.Result!.Scores.Count);
	}

	[Fact]
	public async Task RoomClosing_DecidesWithScores()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync()).IsOk);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Single(decided.Result!.Scores);
		Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
	}

	[Fact]
	public async Task SubmittedScore_ReachesRoom()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		var handler = new ScoreHandler(app.Lobby);
		var score = ScoreOf(1, setup.Player);

		await handler.Handle(new ScoreSubmitted(setup.Player.Record, score, null!), Token);

		Assert.IsType<RoomRoundScoreSubmitted>(app.RoomEvents.Events[^1]);
	}
}