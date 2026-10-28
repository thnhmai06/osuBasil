using Basil.Application.Users.Connections;
using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Multiplayer.Runtime.Events;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Application.Scores.Runtime;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Runtime.Rooms;

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
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap, TeamType: teamType), Token)).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(Token)).IsOk);
		return (app, setup);
	}

	private static async Task CompleteAsync(RoomFixture setup, params BanchoConnection[] players)
	{
		foreach (var player in players)
			Assert.True((await setup.Room.ScopeTo(player).Rounds.CompleteAsync(Token)).IsOk);
	}

	private static ScoreRecord ScoreOf(int id, BanchoConnection player, int total = 1000)
	{
		return RoomFixture.PlayerScore(id, player.Record, RoomFixture.Beatmap.Hash, total);
	}

	[Fact]
	public async Task ReceiveScore_FromAParticipantOnTheRoundsBeatmap_IsTakenAsAScoreOfTheRound()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		var result = setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		var taken = Assert.IsType<RoomRoundScoreSubmitted>(app.RoomEvents.Events[^1]);
		Assert.Equal(setup.Room.Rounds.LastRound, taken.Round);
		Assert.Equal(setup.Player.Record, taken.Player);
		Assert.Null(taken.Score.Team);
	}

	[Fact]
	public async Task ReceiveScore_InATeamRound_CarriesTheTeamTheSlotHadWhenTheRoundStarted()
	{
		var (app, setup) = await StartedAsync(GameTeamType.TeamVs);
		using var _ = app;
		var team = setup.Room.Slots.Find(setup.Player)!.Team;

		setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		Assert.NotNull(team);
		Assert.Equal(team, Assert.IsType<RoomRoundScoreSubmitted>(app.RoomEvents.Events[^1]).Score.Team);
	}

	[Fact]
	public async Task ReceiveScore_OnAnotherBeatmap_IsRefused()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		var other = RoomFixture.PlayerScore(1, setup.Player.Record, new Md5("11111111111111111111111111111111"));

		var result = setup.Room.ReceiveScore(other, setup.Player.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, result.Outcome);
	}

	[Fact]
	public async Task ReceiveScore_FromSomeoneWhoDidNotTakePart_IsRefused()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		var result = setup.Room.ReceiveScore(ScoreOf(1, setup.Outsider), setup.Outsider.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, result.Outcome);
	}

	[Fact]
	public async Task ReceiveScore_ASecondTimeFromTheSamePlayer_IsRefused()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		var result = setup.Room.ReceiveScore(ScoreOf(2, setup.Player), setup.Player.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, result.Outcome);
	}

	[Fact]
	public async Task TheRound_IsDecidedAsSoonAsItEndedAndEveryParticipantHasAScore()
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
	public async Task TheRound_IsDecidedAfterTheEventThatEndedIt()
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
	public async Task TheRound_IsDecidedBeforeTheNextRoundStartsEvenWhileAScoreIsMissing()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		var first = setup.Room.Rounds.LastRound!;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(Token)).IsOk);

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
	public async Task TheRound_WaitsForAScoreThatComesAfterItEnded_ThenIsDecidedAtOnce()
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
	public async Task TheRound_IsDecidedTenSecondsAfterItEndedWithTheScoresThatCameIn()
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
	public async Task TheRound_AnnouncesItEndedWithTheScoreWaitBeforeItIsDecided()
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
	public async Task TheRound_UsesTheScoreWaitOfTheServerSettings()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await app.SetSettingsAsync(new Basil.Application.Content.Persistence.ServerSettings
		{
			Multiplayer = new Basil.Application.Content.Persistence.ServerSettings.MultiplayerSettings
			{
				Rounds = new Basil.Application.Content.Persistence.ServerSettings.RoundSettings
					{ ScoreWait = TimeSpan.FromSeconds(3) }
			}
		});
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		app.Clock.Advance(TimeSpan.FromSeconds(3));

		Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(app.RoomEvents.Events.OfType<RoomRoundEnded>()).ScoreWait);
		Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
	}

	[Fact]
	public async Task TheRound_WithNoScoreAtAll_IsDecidedWithoutAResultAndNamesEveryone()
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
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.AbortAsync(Token)).IsOk);

		app.Clock.Advance(ScoreWait * 2);
		var late = setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);

		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Equal(RoomOutcome.RoundMismatch, late.Outcome);
	}

	[Fact]
	public async Task ADecidedRound_TakesNoMoreScores()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);
		app.Clock.Advance(ScoreWait);

		var late = setup.Room.ReceiveScore(ScoreOf(1, setup.Player), setup.Player.Record);

		Assert.Equal(RoomOutcome.RoundMismatch, late.Outcome);
	}

	[Fact]
	public async Task APlayerWhoLeftBeforeCompleting_IsNotAwaited()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		Assert.True((await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync(Token)).IsOk);
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);

		await CompleteAsync(setup, setup.Creator, setup.Host);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Empty(decided.Missing);
		Assert.Equal(RoomOutcome.RoundMismatch,
			setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record).Outcome);
	}

	[Fact]
	public async Task WhenEveryPlayerLeavesBeforeCompleting_TheRoundIsNotAnnouncedAsEnded()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;

		foreach (var player in new[] { setup.Player, setup.Host, setup.Creator })
			Assert.True((await setup.Room.ScopeTo(player).Members.LeaveAsync(Token)).IsOk);

		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundEnded>());
	}

	[Fact]
	public async Task APlayerWhoCompletedAndThenLeft_IsStillAwaitedAndCounts()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		await CompleteAsync(setup, setup.Player);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Members.LeaveAsync(Token)).IsOk);
		await CompleteAsync(setup, setup.Creator, setup.Host);
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		setup.Room.ReceiveScore(ScoreOf(2, setup.Host), setup.Host.Record);
		Assert.Empty(app.RoomEvents.Events.OfType<RoomRoundDecided>());

		setup.Room.ReceiveScore(ScoreOf(3, setup.Player), setup.Player.Record);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Equal(3, decided.Result!.Scores.Count);
	}

	[Fact]
	public async Task ClosingTheRoomWhileTheRoundWaits_DecidesItWithTheScoresThatCameIn()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		setup.Room.ReceiveScore(ScoreOf(1, setup.Creator), setup.Creator.Record);
		await CompleteAsync(setup, setup.Creator, setup.Host, setup.Player);

		Assert.True((await setup.Room.ScopeTo(setup.Creator).CloseAsync(Token)).IsOk);

		var decided = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundDecided>());
		Assert.Single(decided.Result!.Scores);
		Assert.IsType<LobbyRoomClosed>(app.LobbyEvents.Events[^1]);
	}

	[Fact]
	public async Task ASubmittedScore_ReachesTheRoomThroughTheScoreHandler()
	{
		var (app, setup) = await StartedAsync();
		using var _ = app;
		var handler = new ScoreHandler(app.Lobby);
		var score = ScoreOf(1, setup.Player);

		await handler.Handle(new ScoreSubmitted(setup.Player.Record, score, null!), Token);

		Assert.IsType<RoomRoundScoreSubmitted>(app.RoomEvents.Events[^1]);
	}
}