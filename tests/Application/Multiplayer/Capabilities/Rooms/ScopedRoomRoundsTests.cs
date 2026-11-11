using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Testing;

namespace Basil.Application.Multiplayer.Tests.Capabilities.Rooms;

/// <summary>
///     The rounds of a room. The expected behaviour comes from
///     docs/for-developers/multiplayer.md ("Rounds and score association") and the osu! multiplayer
///     rules: a round needs a beatmap, the players who have it start playing, and the round ends when everybody
///     has finished or failed.
/// </summary>
public sealed class ScopedRoomRoundsTests
{
	[Fact]
	public async Task NoBeatmapStart_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		Assert.Equal(RoomOutcome.NoBeatmap, result);
		Assert.Null(setup.Room.Rounds.LastRound);
	}

	[Fact]
	public async Task ActiveRoundStart_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync()) == RoomOutcome.Ok);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		Assert.Equal(RoomOutcome.InProgress, result);
		Assert.Equal(1, setup.Room.Rounds.LastRound!.Index);
	}

	[Fact]
	public async Task FirstRoundStart_MovesToPlaying()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.NotNull(setup.Room.Rounds.LastRound);
		Assert.Equal(1, setup.Room.Rounds.LastRound!.Index);
		Assert.Equal(RoomSlotStatus.Playing, setup.Room.Slots.Find(setup.Player)!.Status);
		var started = Assert.IsType<RoomRoundStarted>(app.RoomEvents.Events[^1]);
		Assert.Equal(3, started.Players.Count);
	}

	[Fact]
	public async Task AfterRoundEnd_StartsNext()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();
		await CompleteRoundAsync(setup);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.Equal(2, setup.Room.Rounds.LastRound!.Index);
	}

	[Fact]
	public async Task LastPlayerLoaded_AnnouncesAll()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.MarkLoadedAsync()) == RoomOutcome.Ok);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.MarkLoadedAsync()) == RoomOutcome.Ok);
		Assert.IsType<RoomRoundPlayerLoaded>(app.RoomEvents.Events[^1]);

		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.MarkLoadedAsync()) == RoomOutcome.Ok);

		Assert.IsType<RoomRoundAllLoaded>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task NonPlayerLoaded_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.MarkLoadedAsync();

		Assert.Equal(RoomOutcome.NotPlaying, result);
	}

	[Fact]
	public async Task LastPlayerSkipped_AnnouncesAll()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.SkipAsync()) == RoomOutcome.Ok);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.SkipAsync()) == RoomOutcome.Ok);
		Assert.IsType<RoomRoundPlayerSkipped>(app.RoomEvents.Events[^1]);

		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.SkipAsync()) == RoomOutcome.Ok);

		Assert.IsType<RoomRoundAllSkipped>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task PlayerFailure_Announced()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.FailAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.IsType<RoomRoundPlayerFailed>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task PlayerCompletion_Announced()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.IsType<RoomRoundPlayerCompleted>(app.RoomEvents.Events[^1]);
		Assert.True(setup.Room.Rounds.InProgress);
	}

	[Fact]
	public async Task NonPlayerComplete_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync();

		Assert.Equal(RoomOutcome.NotPlaying, result);
	}

	[Fact]
	public async Task LastPlayerComplete_EndsRound()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();
		await CompleteRoundAsync(setup);

		Assert.False(setup.Room.Rounds.InProgress);
		Assert.NotNull(setup.Room.Rounds.LastRound!.EndedAt);
		Assert.Equal(RoomSlotStatus.NotReady, setup.Room.Slots.Find(setup.Player)!.Status);
		Assert.IsType<RoomRoundCompleted>(app.RoomEvents.Events[^2]);
		Assert.IsType<RoomRoundEnded>(app.RoomEvents.Events[^1]);
	}

	[Fact]
	public async Task NoRoundAbort_Refused()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.AbortAsync();

		Assert.Equal(RoomOutcome.NotInProgress, result);
	}

	[Fact]
	public async Task Abort_EndsAndResets()
	{
		using var app = new TestApplication();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();

		var result = await setup.Room.ScopeTo(setup.Creator).Rounds.AbortAsync();

		Assert.Equal(RoomOutcome.Ok, result);
		Assert.False(setup.Room.Rounds.InProgress);
		Assert.True(setup.Room.Rounds.LastRound!.Aborted);
		Assert.Equal(RoomSlotStatus.NotReady, setup.Room.Slots.Find(setup.Player)!.Status);
		Assert.IsType<RoomRoundAborted>(app.RoomEvents.Events[^1]);
	}

	private static async Task SelectBeatmapAsync(RoomFixture setup)
	{
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap))) == RoomOutcome.Ok);
	}

	private static async Task CompleteRoundAsync(RoomFixture setup)
	{
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.CompleteAsync()) == RoomOutcome.Ok);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.CompleteAsync()) == RoomOutcome.Ok);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync()) == RoomOutcome.Ok);
	}
}