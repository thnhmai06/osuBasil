using Basil.Server.Shared.Eventing;
using Basil.Server.Features.Irc;
using System.Text;
using Basil.Server.Features.Beatmaps;
using Basil.Server.Features.Multiplayer;
using Basil.Server.Features.Users;
using Basil.Server.Shared.Configuration;
using Basil.Server.Features.Bot;
using Basil.Server.Shared.Sessions;
using Basil.Server.Features.Chat;
using Basil.Server.Tests.Features.Multiplayer.Packets;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Server.Tests.Features.Multiplayer;

/// <summary>
///     Verifies <see cref="MatchMembership" />'s join/leave slot handling, <see cref="MatchLifecycle" />'s
///     create/close/start lifecycle, and the state broadcasts <see cref="MatchBroadcast" /> sends as a
///     result.
/// </summary>
public class MatchMembershipServiceTests
{
	/// <summary>Defaults to resolving any lookup to a valid beatmap — override per-test for missing-map scenarios.</summary>
	private readonly IBeatmapRepository _beatmapRepository = Substitute.For<IBeatmapRepository>();

	private readonly MultiplayerTestSupport.FakeChannelRegistry _channelRegistry = new();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly MultiplayerTestSupport.FakeMatchRegistry _matchRegistry;

	private readonly FakeMatchRepository _matchRepository = new();
	private readonly MultiplayerTestSupport.FakeMatchRoundEndOutbox _roundEndOutbox = new();
	private readonly MultiplayerTestSupport.FakeMatchLiveEvents _eventBus = new();
	private readonly ILiveEventHub _hub = new LiveEventHub();

	private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

	public MatchMembershipServiceTests()
	{
		_matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(_channelRegistry, _matchRepository);

		_beatmapRepository.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
			Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(MultiplayerTestSupport.MakeBeatmap());
	}

	/// <summary>
	///     Builds the three collaborators the same way DI wires them, sharing the fixture's fakes so a
	///     mutation made through one is visible through the others.
	/// </summary>
	private (MatchMembership Membership, MatchLifecycle Lifecycle, MatchBroadcast Broadcast) MakeService(
		IMatchLiveEvents? eventBus = null)
	{
		var channelMembership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions()));
		var broadcast = new MatchBroadcast(_channelRegistry, channelMembership, _gameRegistry, _ircRegistry, _hub,
			_beatmapRepository, _userRepository);
		var serviceProvider = Substitute.For<IServiceProvider>();
		var lifecycle = new MatchLifecycle(_matchRegistry, _channelRegistry, channelMembership, _gameRegistry,
			_matchRepository, _roundEndOutbox, eventBus ?? _eventBus, _beatmapRepository, broadcast, serviceProvider,
			NullLogger<MatchLifecycle>.Instance);
		var membership = new MatchMembership(_channelRegistry, _gameRegistry, channelMembership, _matchRepository,
			lifecycle, NullLogger<MatchMembership>.Instance);
		serviceProvider.GetService(typeof(MatchMembership)).Returns(membership);
		return (membership, lifecycle, broadcast);
	}

	/// <summary>
	///     The fake persistence repo completes synchronously, so blocking here is safe and keeps every test's synchronous
	///     shape.
	/// </summary>
	private static MatchSession? Create(MatchLifecycle lifecycle, UserSession host, MatchState data)
	{
		return lifecycle.CreateAsync(host, data).GetAwaiter().GetResult();
	}

	private static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	private void RegisterAll(params GameSession[] sessions)
	{
		_gameRegistry.All.Returns(sessions);

		foreach (var session in sessions)
		{
			_gameRegistry.GetByUserId(session.Id).Returns(session);
			_gameRegistry.GetByName(session.Name).Returns(session);
			_gameRegistry.GetByUserId(session.Id).Returns(session);
		}
	}

	private static MatchState MakeMatchData(int hostId, string name = "test match", string password = "",
		bool freeMods = false)
	{
		return new MatchState(
			0, false, 0, 0, name, password,
			"Some Map", 100, new string('a', 32),
			[], [], [], hostId, 0,
			0, 0, freeMods, [], 0);
	}

	[Fact]
	public void Create_RegistersMatchAndJoinsHostIntoSlotZero()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);

		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id));

		Assert.NotNull(match);
		Assert.Same(match, host.Match);
		Assert.Equal(host.Id, match.Slots[0].PlayerId);
		// Named from the persistent id, the same one every command and route calls the room by — not
		// the registry slot id, which is reused once a room closes.
		Assert.Equal($"#mp_{match.DbId}", match.ChatChannelName);
		Assert.NotNull(_channelRegistry.GetByName(match.ChatChannelName));
	}

	[Fact]
	public void Create_KeepsPasswordAsIs_NoPrivateHistoryConcept()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);

		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id, password: "secret"));

		Assert.Equal("secret", match!.Password);
	}

	[Fact]
	public async Task CreateEmptyAsync_CreatesMatchWithNoHostAndNoOccupants()
	{
		var (_, lifecycle, _) = MakeService();

		var match = await lifecycle.CreateEmptyAsync(MakeMatchData(0));

		Assert.NotNull(match);
		Assert.Equal(0, match.HostId);
		Assert.Empty(match.Referees);
		Assert.All(match.Slots, slot => Assert.True(slot.Empty));
		Assert.True(match.DbId > 0);
	}

	[Fact]
	public async Task Join_CorrectPassword_OccupiesFreeSlotAndSendsMatchJoinSuccess()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id, password: "pw"))!;
		host.Dequeue();

		var joined = await membership.JoinAsync(guest, match, "pw");

		Assert.Equal(MatchMembership.JoinResult.Ok, joined);
		Assert.Same(match, guest.Match);
		Assert.Equal(1, match.GetSlotId(guest.Id));
		Assert.Contains(ServerPacketWriter.MatchJoinSuccess(match.ToPacket()),
			Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Join_WrongPassword_FailsAndSendsMatchJoinFail()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id, password: "pw"))!;

		var joined = await membership.JoinAsync(guest, match, "wrong");

		Assert.Equal(MatchMembership.JoinResult.WrongPassword, joined);
		Assert.Null(guest.Match);
		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Join_StaffBypassesWrongPassword()
	{
		var host = MakePlayer(1, "host");
		var staff = MakePlayer(2, "mod");
		staff.Privilege = UserPrivileges.Unrestricted | UserPrivileges.Moderator;
		RegisterAll(host, staff);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id, password: "pw"))!;

		Assert.Equal(MatchMembership.JoinResult.Ok, await membership.JoinAsync(staff, match, "wrong"));
	}

	[Fact]
	public async Task Join_AlreadyInAnotherMatch_Fails()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var matchA = Create(lifecycle, host, MakeMatchData(host.Id))!;
		var otherHost = MakePlayer(3, "other");
		RegisterAll(host, guest, otherHost);
		var matchB = Create(lifecycle, otherHost, MakeMatchData(otherHost.Id))!;
		await membership.JoinAsync(guest, matchA, "");

		Assert.Equal(MatchMembership.JoinResult.AlreadyInMatch, await membership.JoinAsync(guest, matchB, ""));
	}

	[Fact]
	public async Task Join_MatchFull_FailsAndSendsMatchJoinFail()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		for (var i = 1; i < 16; i++)
		{
			match.Slots[i].Status = SlotStatus.NotReady;
			match.Slots[i].PlayerId = 100 + i;
		}

		var overflow = MakePlayer(2, "overflow");
		RegisterAll(host, overflow);

		Assert.Equal(MatchMembership.JoinResult.NoFreeSlot, await membership.JoinAsync(overflow, match, ""));
		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(overflow.Dequeue()));
	}

	[Fact]
	public async Task Join_TeamVsMode_AssignsRedTeamToJoiningPlayer()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		match.TeamType = MatchTeamType.TeamVs;

		await membership.JoinAsync(guest, match, "");

		Assert.Equal(MatchTeam.Red, match.GetSlot(guest.Id)!.Team);
	}

	[Fact]
	public async Task Leave_LastPlayer_StartsEmptyRoomTimerInsteadOfImmediateTeardown()
	{
		var host = MakePlayer(1, "host");
		var lobbyMember = MakePlayer(2, "lobbyguy");
		RegisterAll(host, lobbyMember);
		_channelRegistry.Add(new ChannelSession(1, "#lobby", 0, 0, true));
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		var lobby = _channelRegistry.GetByName("#lobby")!;
		var lobbyMembership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions()));
		lobbyMembership.Join(lobbyMember, lobby);
		lobbyMember.Dequeue();

		await membership.LeaveAsync(host, match);

		// The room no longer tears down the instant it's empty — it starts a 5-minute auto-close
		// timer instead (see MatchLifecycle.SyncEmptyRoomTimer), so nothing is disposed yet.
		Assert.NotNull(_matchRegistry.GetById(match.Id));
		Assert.NotNull(_channelRegistry.GetByName(match.ChatChannelName));
		Assert.Null(host.Match);
		Assert.NotNull(match.EmptyRoomTimer);
		lobbyMember.Dequeue(); // drain the lobby's UpdateMatch broadcast from the slot becoming empty

		await lifecycle.CloseAsync(match);

		Assert.Null(_matchRegistry.GetById(match.Id));
		Assert.Null(_channelRegistry.GetByName(match.ChatChannelName));
		Assert.Contains(ServerPacketWriter.DisposeMatch(match.Id), Chunk(lobbyMember.Dequeue()));
		Assert.Contains(match.DbId, _matchRepository.EndedMatchIds);
		// Regression test (ADR-003): teardown drains the match's round-end outbox before discarding
		// its in-memory state, so the last round's end is never silently lost to a teardown race.
		Assert.Contains(match.DbId, _roundEndOutbox.Drained);
		// Regression test (ADR-004): teardown also drops the live-event hub's bookkeeping for this
		// match, once every subscriber has been completed via SseSubscribers.CompleteAll().
		Assert.Contains(match.DbId, _eventBus.Forgotten);
	}

	/// <summary>
	///     A host leaving stays consistent even when the chat broadcast that accompanies the leave
	///     throws: the match never ends up naming a host who occupies no slot.
	/// </summary>
	/// <remarks>
	///     Regression test. The channel part used to run between clearing the leaving player's slot
	///     and reassigning the host, so an IRC send failure in the middle left HostId pointing at a
	///     player who had already been removed from every slot.
	/// </remarks>
	[Fact]
	public async Task Leave_WhenTheChannelBroadcastThrows_NeverLeavesAnUnseatedHost()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		await membership.JoinAsync(guest, match, "");

		var connection = Substitute.For<IIrcConnection>();
		connection.When(c => c.Send(Arg.Any<IrcMessage>()))
			.Do(_ => throw new InvalidOperationException("irc connection gone"));
		_ircRegistry.GetByUserId(guest.Id).Returns(new IrcSession(
			guest.Id, guest.Name, "irc-2", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
		{
			IrcConnection = connection
		});

		await Assert.ThrowsAsync<InvalidOperationException>(() => membership.LeaveAsync(host, match));

		Assert.True(
			match.HostId == MatchSession.NoHostId
			|| match.Slots.Any(slot => !slot.Empty && slot.PlayerId == match.HostId),
			$"HostId {match.HostId} names no occupied slot.");
	}


	/// <summary>
	///     Regression test (ADR-004): a client still connected to one of this match's live SSE
	///     streams when it closes must observe end-of-stream right away — TeardownMatch completes
	///     every subscriber registered on the match's <c>SseSubscribers</c> registry.
	/// </summary>
	[Fact]
	public async Task CloseAsync_CompletesEverySseSubscriberRegisteredOnTheMatch()
	{
		var (_, lifecycle, _) = MakeService();
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		var completed = false;
		match.SseSubscribers.Subscribe(() => completed = true);

		await lifecycle.CloseAsync(match);

		Assert.True(completed);
	}

	[Fact]
	public async Task Leave_HostLeaves_TransfersHostToFirstOccupiedSlot()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		await membership.JoinAsync(guest, match, "");
		guest.Dequeue();

		await membership.LeaveAsync(host, match);

		Assert.Equal(guest.Id, match.HostId);
		Assert.Contains(ServerPacketWriter.MatchTransferHost(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Leave_SlotWasLocked_StaysLockedAfterReset()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		await membership.JoinAsync(guest, match, "");
		match.GetSlot(guest.Id)!.Status = SlotStatus.Locked;

		await membership.LeaveAsync(guest, match);

		Assert.Equal(SlotStatus.Locked, match.Slots[1].Status);
		Assert.True(match.Slots[1].Empty);
	}

	[Fact]
	public async Task EnqueueState_BroadcastsToLobbyOnlyWhenLobbyHasMembers()
	{
		var host = MakePlayer(1, "host");
		var lobbyMember = MakePlayer(2, "lobbyguy");
		RegisterAll(host, lobbyMember);
		_channelRegistry.Add(new ChannelSession(1, "#lobby", 0, 0, true));
		var (_, lifecycle, broadcast) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		host.Dequeue();

		await broadcast.EnqueueStateAsync(match, match.AllocateStateVersion());
		Assert.Empty(lobbyMember.Dequeue()); // nobody in #lobby yet — no broadcast

		var lobby = _channelRegistry.GetByName("#lobby")!;
		new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry, Substitute.For<IMatchRegistry>(), Substitute.For<ILiveEventHub>(), Options.Create(new IrcOptions())).Join(lobbyMember, lobby);
		lobbyMember.Dequeue();

		await broadcast.EnqueueStateAsync(match, match.AllocateStateVersion());
		Assert.NotEmpty(lobbyMember.Dequeue());
	}

	/// <summary>
	///     <see cref="MatchLifecycle.CreateAsync" /> already calls <see cref="MatchMembership.JoinAsync" />
	///     (which itself calls <see cref="MatchBroadcast.EnqueueStateAsync" />) for the host, so
	///     <see cref="MatchSession.MainSnapshot" /> already holds a full snapshot by the time
	///     <c>Create</c> returns — <see cref="Basil.Server.Tests.Shared.Eventing.StateStreamTests" /> covers that
	///     "first publish is full" behavior standalone. This test covers what happens after that: a
	///     call with no changes publishes an empty patch, and a call after an actual change publishes
	///     only the changed field.
	/// </summary>
	[Fact]
	public async Task EnqueueState_CalledAgainAfterAChange_PublishesDeltaOnly()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var (_, lifecycle, broadcast) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await using var subscription = _hub.Open(MatchStreams.Main(match.DbId));

		// Create already published the initial full snapshot internally, so this first call has
		// nothing new to report — and, per the ADR-004 "{}" spam fix, produces no publish at all
		// rather than a no-op "{}" (regression-tested directly in JsonMergePatchTests/
		// StateStreamTests; this test covers the same behavior at EnqueueStateAsync's call site).
		await broadcast.EnqueueStateAsync(match, match.AllocateStateVersion());

		match.Name = "Renamed";
		await broadcast.EnqueueStateAsync(match, match.AllocateStateVersion());

		await using var events = subscription.Events.GetAsyncEnumerator(cts.Token);
		Assert.True(await events.MoveNextAsync());
		var json = Encoding.UTF8.GetString(events.Current.Payload.Span);
		Assert.Contains("\"name\":\"Renamed\"", json);
		Assert.DoesNotContain("\"referees\"", json);
	}

	/// <summary>
	///     `EnqueueChat` is `MatchControlService.Announce`'s transport — asserts it produces the
	///     bancho SendMessage bytes a match-channel recipient receives.
	/// </summary>
	[Fact]
	public void EnqueueChat_BroadcastsBanchoSendMessageToMatchChannelMembers()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var (_, lifecycle, broadcast) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		_gameRegistry.GetByUserId(host.Id).Returns(host);
		host.Dequeue();

		broadcast.EnqueueChat(match, "BasilBot", BotBootstrapService.BotId, "Match starting soon");

		Assert.Equal(
			ServerPacketWriter.SendMessage("BasilBot", "Match starting soon", "#multiplayer",
				BotBootstrapService.BotId),
			host.Dequeue());
	}

	[Fact]
	public void CancelQueuedAutoStart_PendingAutoStartTimer_CancelsAndAnnounces()
	{
		var host = MakePlayer(1, "host");
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		RegisterAll(host, bot);
		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		var cts = new CancellationTokenSource();
		match.PendingTimer = cts;
		match.PendingTimerIsAutoStart = true;
		host.Dequeue();

		lifecycle.CancelQueuedAutoStart(match);

		Assert.Null(match.PendingTimer);
		Assert.False(match.PendingTimerIsAutoStart);
		Assert.True(cts.IsCancellationRequested);
		Assert.Contains(
			ServerPacketWriter.SendMessage(bot.Name, "Match start cancelled — room settings changed.",
				"#multiplayer", bot.Id),
			Chunk(host.Dequeue()));
	}

	[Fact]
	public void CancelQueuedAutoStart_PendingPlainTimer_LeavesItRunning()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		var cts = new CancellationTokenSource();
		match.PendingTimer = cts;
		match.PendingTimerIsAutoStart = false;

		lifecycle.CancelQueuedAutoStart(match);

		Assert.Same(cts, match.PendingTimer);
		Assert.False(cts.IsCancellationRequested);
	}

	[Fact]
	public void CancelQueuedAutoStart_NoPendingTimer_NoOp()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;

		lifecycle.CancelQueuedAutoStart(match);

		Assert.Null(match.PendingTimer);
	}

	[Fact]
	public async Task StartAsync_BeatmapExists_StartsMatch()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;

		await using var mutation = await match.BeginMutationAsync();
		var started = await lifecycle.StartAsync(match, mutation);

		Assert.Equal(MatchLifecycle.StartOutcome.Started, started);
		Assert.True(match.InProgress);
		Assert.NotNull(match.CurrentRoundId);
	}

	[Fact]
	public async Task StartAsync_NoPlayersSeated_NeverSetsInProgressWithoutAnOccupiedSlot()
	{
		// Regression: a queued `!mp start <seconds>` countdown can outlive every player leaving, so
		// the countdown's eventual StartAsync call must not be allowed to leave the match InProgress
		// with nobody seated — that would violate the invariant that InProgress implies at least one
		// occupied slot. This pins the invariant itself, not the specific leave-then-start sequence.
		var host = MakePlayer(1, "host");
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		RegisterAll(host, bot);
		var (membership, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		host.Dequeue();

		await membership.LeaveAsync(host, match);
		host.Dequeue();

		await using var mutation = await match.BeginMutationAsync();
		var started = await lifecycle.StartAsync(match, mutation);

		Assert.Equal(MatchLifecycle.StartOutcome.NoOccupiedSlots, started);
		Assert.Null(match.CurrentRoundId);
		Assert.True(!match.InProgress || match.Slots.Any(s => !s.Empty));
	}

	[Fact]
	public async Task StartAsync_NoBeatmapSelected_DoesNotStartAndAnnouncesError()
	{
		// Regression for RC4: MapId == 0 (no beatmap selected) previously skipped the beatmap-missing
		// guard entirely and started the match anyway, leaving every slot permanently "Playing" since
		// no client can complete a round for a map it never received — every later !mp start then
		// returned AlreadyInProgress until someone ran !mp abort.
		var host = MakePlayer(1, "host");
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		RegisterAll(host, bot);
		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id) with { MapId = 0 })!;
		host.Dequeue();

		await using var mutation = await match.BeginMutationAsync();
		var started = await lifecycle.StartAsync(match, mutation);

		Assert.Equal(MatchLifecycle.StartOutcome.BeatmapMissing, started);
		Assert.False(match.InProgress);
		Assert.Null(match.CurrentRoundId);
		Assert.Contains(
			ServerPacketWriter.SendMessage(bot.Name,
				"Match cannot start because no beatmap has been selected.",
				"#multiplayer", bot.Id),
			Chunk(host.Dequeue()));
	}

	[Fact]
	public async Task StartAsync_BeatmapMissingFromDb_DoesNotStartAndAnnouncesError()
	{
		var host = MakePlayer(1, "host");
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		RegisterAll(host, bot);
		var (_, lifecycle, _) = MakeService();
		var match = Create(lifecycle, host, MakeMatchData(host.Id))!;
		_beatmapRepository.FetchOneAsync(100, cancellationToken: Arg.Any<CancellationToken>())
			.Returns((Beatmap?)null);
		host.Dequeue();

		await using var mutation = await match.BeginMutationAsync();
		var started = await lifecycle.StartAsync(match, mutation);

		Assert.Equal(MatchLifecycle.StartOutcome.BeatmapMissing, started);
		Assert.False(match.InProgress);
		Assert.Null(match.CurrentRoundId);
		Assert.Contains(
			ServerPacketWriter.SendMessage(bot.Name,
				"Match cannot start because the beatmap does not exist on the server.",
				"#multiplayer", bot.Id),
			Chunk(host.Dequeue()));
	}

	private static List<byte[]> Chunk(byte[] data)
	{
		var chunks = new List<byte[]>();
		var offset = 0;
		while (offset < data.Length)
		{
			var length = BitConverter.ToInt32(data, offset + 3);
			var total = 7 + length;
			chunks.Add(data[offset..(offset + total)]);
			offset += total;
		}

		return chunks;
	}

	private sealed class FakeMatchRepository : IMatchRepository
	{
		private int _nextMatchId = 1;
		private int _nextRoundId = 1;

		public List<int> EndedMatchIds { get; } = [];

		public Task<int> CreateMatchAsync(string name, DateTime createdAt,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_nextMatchId++);
		}

		public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
		{
			EndedMatchIds.Add(matchId);
			return Task.CompletedTask;
		}

		public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5,
			GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
			Mods mods, DateTime startedAt, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_nextRoundId++);
		}

		public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<Match?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<Match?>(null);
		}

		public Task<IReadOnlyList<Round>> FetchRoundsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Round>>([]);
		}

		public Task<IReadOnlyList<Match>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Match>>([]);
		}

		public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task CreateEventAsync(MatchEvent row, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<IReadOnlyList<MatchEvent>> FetchEventsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<MatchEvent>>([]);
		}

		public Task<IReadOnlyList<Match>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Match>>([]);
		}

		public Task<IReadOnlyList<Round>> FetchUnrecoveredRoundsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<Round>>([]);
		}
	}
}