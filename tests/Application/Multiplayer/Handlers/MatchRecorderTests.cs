using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Handlers;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Domain.Multiplayer;
using Basil.Testing;
using NSubstitute;

namespace Basil.Application.Multiplayer.Tests.Handlers;

/// <summary>
///     What a room and the lobby leave behind as stored matches, rounds and match events. The expected values come
///     from docs/for-developers/multiplayer.md: a match is created when a room opens, every seat and authority change
///     is a match event, a round is stored when it starts and again when it ends, and a closed room ends its match.
/// </summary>
public sealed class MatchRecorderTests
{
	[Fact]
	public async Task Handle_LobbyRoomOpened_RecordsTheCreationWithTheCreatorAsActor()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var opened = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomOpened>(),
			lobbyEvent => lobbyEvent.Room == setup.Room);

		await recorder.Handler.Handle(opened, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Match == setup.Room.Match &&
			matchEvent.Type == MatchEventType.Created &&
			matchEvent.Actor == setup.Creator.Record), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomRoundStarted_RecordsTheRound()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		var started = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundStarted>());

		await recorder.Handler.Handle(started, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(started.Round, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomRoundCompleted_RecordsTheRoundAgainWithItsEnd()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync(TestContext.Current.CancellationToken);
		await CompleteRoundAsync(setup);
		var completed = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundCompleted>());
		Assert.NotNull(completed.Round.EndedAt);

		await recorder.Handler.Handle(completed, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(completed.Round, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomPlayerJoined_RecordsTheJoinWithThePlayerAsTarget()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>()
			.First(membership => membership.Player == setup.Player);

		await recorder.Handler.Handle(joined, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.PlayerJoined &&
			matchEvent.Target == setup.Player.Record &&
			matchEvent.Actor == null), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomRefereeAdded_RecordsWhoGrantedItAndToWhom()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var added = Assert.Single(app.RoomEvents.Events.OfType<RoomRefereeAdded>());

		await recorder.Handler.Handle(added, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.RefAdded &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Target == setup.Referee.Record), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomHostChanged_RecordsTheNewHostAsTarget()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomHostChanged>());

		await recorder.Handler.Handle(changed, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.HostGranted &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Target == setup.Host.Record), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_LobbyRoomClosed_EndsTheMatchAndRecordsWhoClosedIt()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).CloseAsync(TestContext.Current.CancellationToken);
		var closed = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomClosed>());

		await recorder.Handler.Handle(closed, TestContext.Current.CancellationToken);

		await recorder.Matches.Received(1).UpdateAsync(setup.Room.Match, Arg.Any<CancellationToken>());
		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.Closed &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Detail == null), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_LobbyMatchRecovered_StoresTheAbortedRoundsAndTheEndAndRecordsTheRecovery()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var round = StoredRound(setup);
		var recovered = new LobbyMatchRecovered(setup.Room.Match, [round]);

		await recorder.Handler.Handle(recovered, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(round, Arg.Any<CancellationToken>());
		await recorder.Matches.Received(1).UpdateAsync(setup.Room.Match, Arg.Any<CancellationToken>());
		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.Closed && matchEvent.Actor == null &&
			matchEvent.Detail == "Server shutdown recovery"), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomRoundScoreSubmitted_StoresTheRelationOfTheScoreToTheRound()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var round = StoredRound(setup);
		var roundScore = RoomFixture.Score(1, round);

		await recorder.Handler.Handle(new RoomRoundScoreSubmitted(setup.Room, round, setup.Player.Record, roundScore),
			TestContext.Current.CancellationToken);

		await recorder.RoundScores.Received(1).RecordAsync(roundScore, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Handle_RoomRoundDecided_StoresTheRoundWithItsDecision()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var round = StoredRound(setup);

		await recorder.Handler.Handle(new RoomRoundDecided(setup.Room, round, null, []),
			TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(round, Arg.Any<CancellationToken>());
	}

	private static Round StoredRound(RoomFixture setup)
	{
		return new Round
		{
			Index = 1,
			Match = setup.Room.Match,
			BeatmapHash = RoomFixture.Beatmap.Hash,
			Settings = new MatchSettings(),
			StartedAt = DateTimeOffset.UnixEpoch,
			EndedAt = null
		};
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
		/// <summary>Creates the recorder over the storage ports.</summary>
		public RecordingRecorder()
		{
			Handler = new MatchRecorder(Matches, Rounds, MatchEvents, RoundScores);
		}

		/// <summary>Gets the port through which matches are stored.</summary>
		public IMatchRepository Matches { get; } = Substitute.For<IMatchRepository>();

		/// <summary>Gets the port through which rounds are stored.</summary>
		public IRoundRepository Rounds { get; } = Substitute.For<IRoundRepository>();

		/// <summary>Gets the port through which match events are stored.</summary>
		public IMatchEventRepository MatchEvents { get; } = Substitute.For<IMatchEventRepository>();

		/// <summary>Gets the port through which the scores of rounds are stored.</summary>
		public IRoundScoreRepository RoundScores { get; } = Substitute.For<IRoundScoreRepository>();

		/// <summary>Gets the recorder under test.</summary>
		public MatchRecorder Handler { get; }
	}
}