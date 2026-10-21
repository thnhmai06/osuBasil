using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Tests.Support;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Runtime.Multiplayer;
using NSubstitute;

namespace Basil.Infrastructure.Runtime.Tests.Multiplayer;

/// <summary>
///     What a room and the lobby leave behind as stored matches, rounds and match events. The expected values come
///     from docs/for-developers/multiplayer.md: a match is created when a room opens, every seat and authority change
///     is a match event, a round is stored when it starts and again when it ends, and a closed room ends its match.
/// </summary>
public sealed class MatchRecorderTests
{
	[Fact]
	public async Task HandleAsync_LobbyRoomOpened_RecordsTheCreationWithTheCreatorAsActor()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var opened = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomOpened>(),
			lobbyEvent => lobbyEvent.Room == setup.Room);

		await recorder.Handler.HandleAsync(opened, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Match == setup.Room.Match &&
			matchEvent.Type == MatchEventType.Created &&
			matchEvent.Actor == setup.Creator.Record), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_RoomRoundStarted_RecordsTheRound()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		var started = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundStarted>());

		await recorder.Handler.HandleAsync(started, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(started.Round, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_RoomRoundCompleted_RecordsTheRoundAgainWithItsEnd()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		await CompleteRoundAsync(setup);
		var completed = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundCompleted>());
		Assert.NotNull(completed.Round.EndedAt);

		await recorder.Handler.HandleAsync(completed, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(completed.Round, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_RoomPlayerJoined_RecordsTheJoinWithThePlayerAsTarget()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>()
			.First(membership => membership.Player == setup.Player);

		await recorder.Handler.HandleAsync(joined, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.PlayerJoined &&
			matchEvent.Target == setup.Player.Record &&
			matchEvent.Actor == null), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_RoomRefereeAdded_RecordsWhoGrantedItAndToWhom()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var added = Assert.Single(app.RoomEvents.Events.OfType<RoomRefereeAdded>());

		await recorder.Handler.HandleAsync(added, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.RefAdded &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Target == setup.Referee.Record), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_RoomHostChanged_RecordsTheNewHostAsTarget()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomHostChanged>());

		await recorder.Handler.HandleAsync(changed, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.HostGranted &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Target == setup.Host.Record), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_LobbyRoomClosed_EndsTheMatchAndRecordsWhoClosedIt()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).CloseAsync(TestContext.Current.CancellationToken);
		var closed = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomClosed>());

		await recorder.Handler.HandleAsync(closed, TestContext.Current.CancellationToken);

		await recorder.Matches.Received(1).UpdateAsync(setup.Room.Match, Arg.Any<CancellationToken>());
		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.Closed &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Detail == null), Arg.Any<CancellationToken>());
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

	/// <summary>The recorder and the storage ports it writes to.</summary>
	private sealed class RecordingRecorder
	{
		/// <summary>Gets the port through which matches are stored.</summary>
		public IMatchRepository Matches { get; } = Substitute.For<IMatchRepository>();

		/// <summary>Gets the port through which rounds are stored.</summary>
		public IRoundRepository Rounds { get; } = Substitute.For<IRoundRepository>();

		/// <summary>Gets the port through which match events are stored.</summary>
		public IMatchEventRepository MatchEvents { get; } = Substitute.For<IMatchEventRepository>();

		/// <summary>Gets the recorder under test.</summary>
		public MatchRecorder Handler { get; }

		/// <summary>Creates the recorder over the storage ports.</summary>
		public RecordingRecorder()
		{
			Handler = new MatchRecorder(Matches, Rounds, MatchEvents);
		}
	}
}
