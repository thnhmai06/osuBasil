using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Infrastructure.Multiplayer.Handlers;
using Basil.Testing;
using NSubstitute;

namespace Basil.Infrastructure.Multiplayer.Tests.Handlers;

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
			lobbyEvent => lobbyEvent.Room.Equals(setup.Room));

		await new LobbyRoomOpenedRecorder(recorder.MatchEvents).Handle(opened, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Match == setup.Room.Match &&
			matchEvent.Type == MatchEventType.Created &&
			matchEvent.Actor == setup.Creator.Record));
	}

	[Fact]
	public async Task Handle_RoomRoundStarted_RecordsTheRound()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();
		var started = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundStarted>());

		await new RoomRoundStartedRecorder(recorder.Rounds).Handle(started, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(started.Round);
	}

	[Fact]
	public async Task Handle_RoomRoundCompleted_RecordsTheRoundAgainWithItsEnd()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await SelectBeatmapAsync(setup);
		await setup.Room.ScopeTo(setup.Creator).Rounds.StartAsync();
		await CompleteRoundAsync(setup);
		var completed = Assert.Single(app.RoomEvents.Events.OfType<RoomRoundCompleted>());
		Assert.NotNull(completed.Round.EndedAt);

		await new RoomRoundCompletedRecorder(recorder.Rounds).Handle(completed, TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(completed.Round);
	}

	[Fact]
	public async Task Handle_RoomPlayerJoined_RecordsTheJoinWithThePlayerAsTarget()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var joined = app.RoomEvents.Events.OfType<RoomPlayerJoined>()
			.First(membership => membership.Player == setup.Player);

		await new RoomPlayerJoinedRecorder(recorder.Rounds, recorder.MatchEvents).Handle(joined,
			TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.PlayerJoined &&
			matchEvent.Target == setup.Player.Record &&
			matchEvent.Actor == null));
	}

	[Fact]
	public async Task Handle_RoomRefereeAdded_RecordsWhoGrantedItAndToWhom()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var added = Assert.Single(app.RoomEvents.Events.OfType<RoomRefereeAdded>());

		await new RoomRefereeAddedRecorder(recorder.MatchEvents).Handle(added, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.RefAdded &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Target == setup.Referee.Record));
	}

	[Fact]
	public async Task Handle_RoomHostChanged_RecordsTheNewHostAsTarget()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var changed = Assert.Single(app.RoomEvents.Events.OfType<RoomHostChanged>());

		await new RoomHostChangedRecorder(recorder.MatchEvents).Handle(changed, TestContext.Current.CancellationToken);

		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.HostGranted &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Target == setup.Host.Record));
	}

	[Fact]
	public async Task Handle_LobbyRoomClosed_EndsTheMatchAndRecordsWhoClosedIt()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).CloseAsync();
		var closed = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomClosed>());

		await new LobbyRoomClosedRecorder(recorder.Rounds, recorder.Matches, recorder.MatchEvents).Handle(closed,
			TestContext.Current.CancellationToken);

		await recorder.Matches.Received(1).UpdateAsync(setup.Room.Match);
		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.Closed &&
			matchEvent.Actor == setup.Creator.Record &&
			matchEvent.Detail == null));
	}

	[Fact]
	public async Task Handle_LobbyRoomClosed_NeverLetsTheMatchReadAsEndedBeforeItsClosingEntryIsStored()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		await setup.Room.ScopeTo(setup.Creator).CloseAsync();
		var closed = Assert.Single(app.LobbyEvents.Events.OfType<LobbyRoomClosed>());
		var closingStoredWhenEnded = false;
		recorder.Matches.UpdateAsync(Arg.Any<MatchRecord>()).Returns(_ =>
		{
			closingStoredWhenEnded = recorder.MatchEvents.ReceivedCalls().Any();
			return Task.CompletedTask;
		});

		await new LobbyRoomClosedRecorder(recorder.Rounds, recorder.Matches, recorder.MatchEvents).Handle(closed,
			TestContext.Current.CancellationToken);

		Assert.True(closingStoredWhenEnded);
	}

	[Fact]
	public async Task Handle_LobbyMatchRecovered_NeverLetsTheMatchReadAsEndedBeforeItsClosingEntryIsStored()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var closingStoredWhenEnded = false;
		recorder.Matches.UpdateAsync(Arg.Any<MatchRecord>()).Returns(_ =>
		{
			closingStoredWhenEnded = recorder.MatchEvents.ReceivedCalls().Any();
			return Task.CompletedTask;
		});

		await new LobbyMatchRecoveredRecorder(recorder.Rounds, recorder.Matches, recorder.MatchEvents).Handle(
			new LobbyMatchRecovered(setup.Room.Match, []), TestContext.Current.CancellationToken);

		Assert.True(closingStoredWhenEnded);
	}

	[Fact]
	public async Task Handle_LobbyMatchRecovered_StoresTheAbortedRoundsAndTheEndAndRecordsTheRecovery()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var round = StoredRound(setup);
		var recovered = new LobbyMatchRecovered(setup.Room.Match, [round]);

		await new LobbyMatchRecoveredRecorder(recorder.Rounds, recorder.Matches, recorder.MatchEvents).Handle(recovered,
			TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(round);
		await recorder.Matches.Received(1).UpdateAsync(setup.Room.Match);
		await recorder.MatchEvents.Received(1).RecordAsync(Arg.Is<MatchEvent>(matchEvent =>
			matchEvent.Type == MatchEventType.Closed && matchEvent.Actor == null &&
			matchEvent.Detail == MatchEvent.ServerStoppedDetail));
	}

	[Fact]
	public async Task Handle_RoomRoundScoreSubmitted_StoresTheRelationOfTheScoreToTheRound()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var round = StoredRound(setup);
		var roundScore = RoomFixture.Score(1, round);

		await new RoomRoundScoreSubmittedRecorder(recorder.RoundScores).Handle(
			new RoomRoundScoreSubmitted(setup.Room, round, setup.Player.Record, roundScore),
			TestContext.Current.CancellationToken);

		await recorder.RoundScores.Received(1).RecordAsync(roundScore);
	}

	[Fact]
	public async Task Handle_RoomRoundDecided_StoresTheRoundWithItsDecision()
	{
		using var app = new TestApplication();
		var recorder = new RecordingRecorder();
		var setup = await RoomFixture.CreateAsync(app);
		var round = StoredRound(setup);

		await new RoomRoundDecidedRecorder(recorder.Rounds).Handle(new RoomRoundDecided(setup.Room, round, null, []),
			TestContext.Current.CancellationToken);

		await recorder.Rounds.Received(1).CreateOrUpdateAsync(round);
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
			new RoomSettingsChange(Beatmap: RoomFixture.Beatmap))).IsOk);
	}

	private static async Task CompleteRoundAsync(RoomFixture setup)
	{
		Assert.True((await setup.Room.ScopeTo(setup.Creator).Rounds.CompleteAsync()).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Host).Rounds.CompleteAsync()).IsOk);
		Assert.True((await setup.Room.ScopeTo(setup.Player).Rounds.CompleteAsync()).IsOk);
	}

	/// <summary>The storage ports the recorders write to.</summary>
	private sealed class RecordingRecorder
	{
		/// <summary>Gets the port through which matches are stored.</summary>
		public IMatchRepository Matches { get; } = Substitute.For<IMatchRepository>();

		/// <summary>Gets the port through which rounds are stored.</summary>
		public IRoundRepository Rounds { get; } = Substitute.For<IRoundRepository>();

		/// <summary>Gets the port through which match events are stored.</summary>
		public IMatchEventRepository MatchEvents { get; } = Substitute.For<IMatchEventRepository>();

		/// <summary>Gets the port through which the scores of rounds are stored.</summary>
		public IRoundScoreRepository RoundScores { get; } = Substitute.For<IRoundScoreRepository>();
	}
}