using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Tests.Packets;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

/// <summary>Verifies `MatchMembershipService`'s match join/leave handling and the match-state broadcasts that follow.</summary>
public class MatchMembershipServiceTests
{
	/// <summary>Defaults to resolving any lookup to a valid beatmap — override per-test for missing-map scenarios.</summary>
	private readonly IBeatmapRepository _beatmapRepository = Substitute.For<IBeatmapRepository>();

	private readonly MultiplayerTestSupport.FakeChannelRegistry _channelRegistry = new();
	private readonly ISessionRegistry<GameSession> _gameRegistry = Substitute.For<ISessionRegistry<GameSession>>();
	private readonly ISessionRegistry<IrcSession> _ircRegistry = Substitute.For<ISessionRegistry<IrcSession>>();
	private readonly MultiplayerTestSupport.FakeMatchRegistry _matchRegistry;

	private readonly FakeMatchRepository _matchRepository = new();

	private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();

	public MatchMembershipServiceTests()
	{
		_matchRegistry = new MultiplayerTestSupport.FakeMatchRegistry(_channelRegistry, _matchRepository);

		_beatmapRepository.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
			Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(MultiplayerTestSupport.MakeBeatmap());
	}

	private MatchMembershipService MakeService()
	{
		return new MatchMembershipService(_matchRegistry, _channelRegistry, _gameRegistry, _ircRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			_matchRepository,
			Substitute.For<IMatchLiveEvents>(), _beatmapRepository, _userRepository,
			NullLogger<MatchMembershipService>.Instance);
	}

	/// <summary>
	///     The fake persistence repo completes synchronously, so blocking here is safe and keeps every test's synchronous
	///     shape.
	/// </summary>
	private static MatchSession? Create(MatchMembershipService service, UserSession host, MatchState data)
	{
		return service.CreateAsync(host, data).GetAwaiter().GetResult();
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

		var match = Create(MakeService(), host, MakeMatchData(host.Id));

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

		var match = Create(MakeService(), host, MakeMatchData(host.Id, password: "secret"));

		Assert.Equal("secret", match!.Password);
	}

	[Fact]
	public async Task CreateEmptyAsync_CreatesMatchWithNoHostAndNoOccupants()
	{
		var service = MakeService();

		var match = await service.CreateEmptyAsync(MakeMatchData(0));

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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id, password: "pw"))!;
		host.Dequeue();

		var joined = await service.JoinAsync(guest, match, "pw");

		Assert.Equal(MatchMembershipService.JoinResult.Ok, joined);
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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id, password: "pw"))!;

		var joined = await service.JoinAsync(guest, match, "wrong");

		Assert.Equal(MatchMembershipService.JoinResult.WrongPassword, joined);
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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id, password: "pw"))!;

		Assert.Equal(MatchMembershipService.JoinResult.Ok, await service.JoinAsync(staff, match, "wrong"));
	}

	[Fact]
	public async Task Join_AlreadyInAnotherMatch_Fails()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var service = MakeService();
		var matchA = Create(service, host, MakeMatchData(host.Id))!;
		var otherHost = MakePlayer(3, "other");
		RegisterAll(host, guest, otherHost);
		var matchB = Create(service, otherHost, MakeMatchData(otherHost.Id))!;
		await service.JoinAsync(guest, matchA, "");

		Assert.Equal(MatchMembershipService.JoinResult.AlreadyInMatch, await service.JoinAsync(guest, matchB, ""));
	}

	[Fact]
	public async Task Join_MatchFull_FailsAndSendsMatchJoinFail()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		for (var i = 1; i < 16; i++)
		{
			match.Slots[i].Status = SlotStatus.NotReady;
			match.Slots[i].PlayerId = 100 + i;
		}

		var overflow = MakePlayer(2, "overflow");
		RegisterAll(host, overflow);

		Assert.Equal(MatchMembershipService.JoinResult.NoFreeSlot, await service.JoinAsync(overflow, match, ""));
		Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(overflow.Dequeue()));
	}

	[Fact]
	public async Task Join_TeamVsMode_AssignsRedTeamToJoiningPlayer()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		match.TeamType = MatchTeamType.TeamVs;

		await service.JoinAsync(guest, match, "");

		Assert.Equal(MatchTeam.Red, match.GetSlot(guest.Id)!.Team);
	}

	[Fact]
	public async Task Leave_LastPlayer_StartsEmptyRoomTimerInsteadOfImmediateTeardown()
	{
		var host = MakePlayer(1, "host");
		var lobbyMember = MakePlayer(2, "lobbyguy");
		RegisterAll(host, lobbyMember);
		_channelRegistry.Add(new ChannelSession(1, "#lobby", 0, 0, true));
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		var lobby = _channelRegistry.GetByName("#lobby")!;
		var membership = new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
			Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));
		membership.Join(lobbyMember, lobby);
		lobbyMember.Dequeue();

		await service.LeaveAsync(host, match);

		// The room no longer tears down the instant it's empty — it starts a 5-minute auto-close
		// timer instead (see MatchMembershipService.SyncEmptyRoomTimer), so nothing is disposed yet.
		Assert.NotNull(_matchRegistry.GetById(match.Id));
		Assert.NotNull(_channelRegistry.GetByName(match.ChatChannelName));
		Assert.Null(host.Match);
		Assert.NotNull(match.EmptyRoomTimer);
		lobbyMember.Dequeue(); // drain the lobby's UpdateMatch broadcast from the slot becoming empty

		await service.CloseAsync(match);

		Assert.Null(_matchRegistry.GetById(match.Id));
		Assert.Null(_channelRegistry.GetByName(match.ChatChannelName));
		Assert.Contains(ServerPacketWriter.DisposeMatch(match.Id), Chunk(lobbyMember.Dequeue()));
		Assert.Contains(match.DbId, _matchRepository.EndedMatchIds);
	}

	[Fact]
	public async Task Leave_HostLeaves_TransfersHostToFirstOccupiedSlot()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		await service.JoinAsync(guest, match, "");
		guest.Dequeue();

		await service.LeaveAsync(host, match);

		Assert.Equal(guest.Id, match.HostId);
		Assert.Contains(ServerPacketWriter.MatchTransferHost(), Chunk(guest.Dequeue()));
	}

	[Fact]
	public async Task Leave_SlotWasLocked_StaysLockedAfterReset()
	{
		var host = MakePlayer(1, "host");
		var guest = MakePlayer(2, "guest");
		RegisterAll(host, guest);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		await service.JoinAsync(guest, match, "");
		match.GetSlot(guest.Id)!.Status = SlotStatus.Locked;

		await service.LeaveAsync(guest, match);

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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		host.Dequeue();

		await service.EnqueueStateAsync(match);
		Assert.Empty(lobbyMember.Dequeue()); // nobody in #lobby yet — no broadcast

		var lobby = _channelRegistry.GetByName("#lobby")!;
		new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry, Substitute.For<IMatchRegistry>(),
			Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())).Join(lobbyMember, lobby);
		lobbyMember.Dequeue();

		await service.EnqueueStateAsync(match);
		Assert.NotEmpty(lobbyMember.Dequeue());
	}

	/// <summary>
	///     <see cref="MatchMembershipService.CreateAsync" /> already calls <see cref="MatchMembershipService.JoinAsync" />
	///     (which itself calls <see cref="MatchMembershipService.EnqueueStateAsync" />) for the host, so
	///     <see cref="MatchSession.MainSnapshot" /> already holds a full snapshot by the time
	///     <c>Create</c> returns — <see cref="Services.Multiplayer.SnapshotChannelTests" /> covers that
	///     "first publish is full" behavior standalone. This test covers what happens after that: a
	///     call with no changes publishes an empty patch, and a call after an actual change publishes
	///     only the changed field.
	/// </summary>
	[Fact]
	public async Task EnqueueState_CalledAgainAfterAChange_PublishesDeltaOnly()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var events = Substitute.For<IMatchLiveEvents>();
		var service = new MatchMembershipService(_matchRegistry, _channelRegistry, _gameRegistry, _ircRegistry,
			new ChannelMembershipService(_gameRegistry, _ircRegistry, _channelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions())),
			_matchRepository, events,
			_beatmapRepository, _userRepository, NullLogger<MatchMembershipService>.Instance);
		var match = Create(service, host, MakeMatchData(host.Id))!;

		var payloads = new List<byte[]>();
		events.When(e => e.PublishMain(Arg.Any<int>(), Arg.Any<byte[]>()))
			.Do(call => payloads.Add(call.ArgAt<byte[]>(1)));

		await service.EnqueueStateAsync(match);
		match.Name = "Renamed";
		await service.EnqueueStateAsync(match);

		Assert.Equal(2, payloads.Count);
		Assert.Equal("{}", Encoding.UTF8.GetString(payloads[0]));

		var secondJson = Encoding.UTF8.GetString(payloads[1]);
		Assert.Contains("\"name\":\"Renamed\"", secondJson);
		Assert.DoesNotContain("\"referees\"", secondJson);
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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		_gameRegistry.GetByUserId(host.Id).Returns(host);
		host.Dequeue();

		service.EnqueueChat(match, "BasilBot", BotBootstrapService.BotId, "Match starting soon");

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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		var cts = new CancellationTokenSource();
		match.PendingTimer = cts;
		match.PendingTimerIsAutoStart = true;
		host.Dequeue();

		service.CancelQueuedAutoStart(match);

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
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		var cts = new CancellationTokenSource();
		match.PendingTimer = cts;
		match.PendingTimerIsAutoStart = false;

		service.CancelQueuedAutoStart(match);

		Assert.Same(cts, match.PendingTimer);
		Assert.False(cts.IsCancellationRequested);
	}

	[Fact]
	public void CancelQueuedAutoStart_NoPendingTimer_NoOp()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;

		service.CancelQueuedAutoStart(match);

		Assert.Null(match.PendingTimer);
	}

	[Fact]
	public async Task StartAsync_BeatmapExists_StartsMatch()
	{
		var host = MakePlayer(1, "host");
		RegisterAll(host);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;

		var started = await service.StartAsync(match);

		Assert.True(started);
		Assert.True(match.InProgress);
		Assert.NotNull(match.CurrentRoundId);
	}

	[Fact]
	public async Task StartAsync_BeatmapMissingFromDb_DoesNotStartAndAnnouncesError()
	{
		var host = MakePlayer(1, "host");
		var bot = MakePlayer(BotBootstrapService.BotId, "BasilBot");
		RegisterAll(host, bot);
		var service = MakeService();
		var match = Create(service, host, MakeMatchData(host.Id))!;
		_beatmapRepository.FetchOneAsync(100, cancellationToken: Arg.Any<CancellationToken>())
			.Returns((Beatmap?)null);
		host.Dequeue();

		var started = await service.StartAsync(match);

		Assert.False(started);
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