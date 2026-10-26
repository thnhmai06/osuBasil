using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Multiplayer.Rooms;
using Basil.Domain.Mechanics;
using Basil.Testing;

namespace Basil.Application.Runtime.Tests.Multiplayer.Rooms;

/// <summary>
///     The rounds of a room and their countdown. The expected behaviour comes from
///     docs/for-developers/multiplayer.md ("Rounds and score association" and "Countdown") and the osu! multiplayer
///     rules: a round needs a beatmap, the players who have it start playing, and the round ends when everybody
///     has finished or failed.
/// </summary>
public sealed class ScopedRoomRoundsTests
{
	[Fact]
	public async Task StartAsync_WithoutABeatmap_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NoBeatmap, result.Outcome);
		Assert.Null(setup.Room.Rounds.LastRound);
	}

	[Fact]
	public async Task StartAsync_WhileARoundIsInProgress_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(
			TestContext.Current.CancellationToken)).IsOk);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
		Assert.Equal(1, setup.Room.Rounds.LastRound!.Index);
	}

	[Fact]
	public async Task StartAsync_StartsTheFirstRoundAndMovesThePlayersToPlaying()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.NotNull(setup.Room.Rounds.LastRound);
		Assert.Equal(1, setup.Room.Rounds.LastRound!.Index);
		Assert.Equal(RoomSlotStatus.Playing, setup.Room.Slots.Find(setup.Player)!.Status);
		var started = Assert.IsType<RoomRoundStarted>(app.RoomEvents.Events[^1]);
		Assert.Equal(3, started.Players.Count);
	}

	[Fact]
	public async Task StartAsync_AfterARoundEnded_StartsTheNextRound()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		await CompleteRoundAsync(setup);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.Equal(2, setup.Room.Rounds.LastRound!.Index);
	}

	[Fact]
	public async Task MarkLoadedAsync_ForTheLastPlayer_AnnouncesThatEverybodyLoaded()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.MarkLoadedAsync(
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.MarkLoadedAsync(
			TestContext.Current.CancellationToken)).IsOk);
		Assert.IsType<RoomRoundPlayerLoaded>(app.RoomEvents.Events[^1]);

		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.MarkLoadedAsync(
			TestContext.Current.CancellationToken)).IsOk);

		Assert.IsType<RoomRoundAllLoaded>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task MarkLoadedAsync_ForSomebodyWhoIsNotPlaying_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.MarkLoadedAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotPlaying, result.Outcome);
	}

	[Fact]
	public async Task SkipAsync_ForTheLastPlayer_AnnouncesThatEverybodySkipped()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.SkipAsync(
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.SkipAsync(
			TestContext.Current.CancellationToken)).IsOk);
		Assert.IsType<RoomRoundPlayerSkipped>(app.RoomEvents.Events[^1]);

		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.SkipAsync(
			TestContext.Current.CancellationToken)).IsOk);

		Assert.IsType<RoomRoundAllSkipped>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task FailAsync_ForAPlayer_AnnouncesTheFailure()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.FailAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.IsType<RoomRoundPlayerFailed>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task CompleteAsync_ForAPlayer_AnnouncesTheCompletion()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.IsType<RoomRoundPlayerCompleted>(app.RoomEvents.Events[^1]);
		Assert.True(setup.Room.Rounds.InProgress);
	}

	[Fact]
	public async Task CompleteAsync_ForSomebodyWhoIsNotPlaying_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotPlaying, result.Outcome);
	}

	[Fact]
	public async Task CompleteAsync_ForTheLastPlayer_EndsTheRound()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		await CompleteRoundAsync(setup);

		Assert.False(setup.Room.Rounds.InProgress);
		Assert.NotNull(setup.Room.Rounds.LastRound!.EndedAt);
		Assert.Equal(RoomSlotStatus.NotReady, setup.Room.Slots.Find(setup.Player)!.Status);
		Assert.IsType<RoomRoundCompleted>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task AbortAsync_WithoutARound_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.AbortAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NotInProgress, result.Outcome);
	}

	[Fact]
	public async Task AbortAsync_EndsTheRoundAndSetsThePlayersBackToNotReady()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.AbortAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.False(setup.Room.Rounds.InProgress);
		Assert.True(setup.Room.Rounds.LastRound!.Aborted);
		Assert.Equal(RoomSlotStatus.NotReady, setup.Room.Slots.Find(setup.Player)!.Status);
		Assert.IsType<RoomRoundAborted>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task StartCountdownAsync_ForATimer_AnnouncesTheSixtyThirtyTenAndFiveSecondMarks()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), false,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.IsType<RoomCountdownStarted>(app.RoomEvents.Events[^1]);

		app.Clock.Advance(TimeSpan.FromMinutes(4));
		Assert.Equal(TimeSpan.FromSeconds(60), Assert.IsType<RoomCountdownTicked>(app.RoomEvents.Events[^1]).Remaining);

		app.Clock.Advance(TimeSpan.FromSeconds(30));
		Assert.Equal(TimeSpan.FromSeconds(30), Assert.IsType<RoomCountdownTicked>(app.RoomEvents.Events[^1]).Remaining);

		app.Clock.Advance(TimeSpan.FromSeconds(20));
		Assert.Equal(TimeSpan.FromSeconds(10), Assert.IsType<RoomCountdownTicked>(app.RoomEvents.Events[^1]).Remaining);

		app.Clock.Advance(TimeSpan.FromSeconds(5));
		Assert.Equal(TimeSpan.FromSeconds(5), Assert.IsType<RoomCountdownTicked>(app.RoomEvents.Events[^1]).Remaining);

		app.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(5));
		Assert.IsType<RoomCountdownElapsed>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task StartCountdownAsync_ForARound_StartsTheRoundWhenItElapses()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);

		var result = await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), true,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.Ok, result.Outcome);
		Assert.IsType<RoomCountdownStarted>(app.RoomEvents.Events[^1]);

		app.Clock.Advance(TimeSpan.FromMinutes(5));

		var elapsed = IndexOf<RoomCountdownElapsed>(app);
		var started = IndexOf<RoomRoundStarted>(app);
		Assert.True(elapsed >= 0);
		Assert.True(started > elapsed);
		Assert.Equal(1, setup.Room.Rounds.LastRound!.Index);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task StartCountdownAsync_ByAPlainPlayer_IsRefusedAndStartsNothing(bool startsRound)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5),
			startsRound, TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.RoleDenied, result.Outcome);
		Assert.Null(setup.Room.Rounds.Countdown);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task StartCountdownAsync_WithANonPositiveLength_IsRefused(int seconds)
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(
			TimeSpan.FromSeconds(seconds), false, TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
		Assert.Null(setup.Room.Rounds.Countdown);
	}

	[Fact]
	public async Task StartCountdownAsync_WithALengthLongerThanAnHour_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(
			TimeSpan.FromSeconds(3601), false, TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InvalidSettings, result.Outcome);
		Assert.Null(setup.Room.Rounds.Countdown);
	}

	[Fact]
	public async Task StartCountdownAsync_WhileARoundIsInProgress_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);

		var result = await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), true,
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.InProgress, result.Outcome);
		Assert.Null(setup.Room.Rounds.Countdown);
	}

	[Fact]
	public async Task CancelCountdownAsync_WithoutACountdown_IsRefused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Host).Rounds.CancelCountdownAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(RoomOutcome.NoCountdown, result.Outcome);
	}

	[Fact]
	public async Task StartAsync_WithACountdown_CancelsTheCountdown()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), true,
			TestContext.Current.CancellationToken);

		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.StartAsync(
			TestContext.Current.CancellationToken)).IsOk);

		Assert.Null(setup.Room.Rounds.Countdown);
		Assert.Equal(RoomCountdownCancelReason.RoundStarted,
			Assert.Single(app.RoomEvents.Events.OfType<RoomCountdownCancelled>()).Reason);
	}

	[Fact]
	public async Task AbortAsync_WithACountdown_CancelsTheCountdown()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), false,
			TestContext.Current.CancellationToken);

		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.AbortAsync(
			TestContext.Current.CancellationToken)).IsOk);

		Assert.Null(setup.Room.Rounds.Countdown);
		Assert.Equal(RoomCountdownCancelReason.RoundAborted,
			Assert.Single(app.RoomEvents.Events.OfType<RoomCountdownCancelled>()).Reason);
	}

	[Fact]
	public async Task ConfigureAsync_WithACountdownThatStartsARound_CancelsTheCountdown()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Host).Rounds.StartCountdownAsync(TimeSpan.FromMinutes(5), true,
			TestContext.Current.CancellationToken);

		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(WinCondition: GameWinCondition.Accuracy),
			TestContext.Current.CancellationToken)).IsOk);

		Assert.Null(setup.Room.Rounds.Countdown);
		Assert.Equal(RoomCountdownCancelReason.SettingsChanged,
			Assert.Single(app.RoomEvents.Events.OfType<RoomCountdownCancelled>()).Reason);
	}

	private static int IndexOf<T>(TestApplication app) where T : RoomEvent
	{
		for (var index = 0; index < app.RoomEvents.Events.Count; index++)
			if (app.RoomEvents.Events[index] is T)
				return index;

		return -1;
	}

	private static async Task SelectBeatmapAsync(RoomFixture setup)
	{
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap), TestContext.Current.CancellationToken)).IsOk);
	}

	private static async Task CompleteRoundAsync(RoomFixture setup)
	{
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.CompleteAsync(
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.CompleteAsync(
			TestContext.Current.CancellationToken)).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync(
			TestContext.Current.CancellationToken)).IsOk);
	}
}