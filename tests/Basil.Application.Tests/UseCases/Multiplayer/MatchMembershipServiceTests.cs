using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Services.Bot;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Multiplayer;

/// <summary>Ported from Player.join_match/leave_match plus Match.enqueue/enqueue_state.</summary>
public class MatchMembershipServiceTests
{
    private readonly FakeChannelRegistry _channelRegistry = new();
    private readonly FakeMatchPersistenceRepository _matchPersistence = new();
    private readonly FakeMatchRegistry _matchRegistry = new();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private MatchMembershipService MakeService()
    {
        return new MatchMembershipService(_matchRegistry, _channelRegistry, _sessionRegistry,
            new ChannelMembershipService(_sessionRegistry, _channelRegistry), _matchPersistence,
            Substitute.For<IMatchEventBus>(), Substitute.For<IMapRepository>());
    }

    /// <summary>
    ///     The fake persistence repo completes synchronously, so blocking here is safe and keeps every test's synchronous
    ///     shape.
    /// </summary>
    private static MatchSession? Create(MatchMembershipService service, PlayerSession host, ReadMatchResult data)
    {
        return service.CreateAsync(host, data).GetAwaiter().GetResult();
    }

    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
    }

    private void RegisterAll(params PlayerSession[] sessions)
    {
        _sessionRegistry.All.Returns(sessions);
        foreach (var session in sessions) _sessionRegistry.GetById(session.Id).Returns(session);
    }

    private static ReadMatchResult MakeMatchData(int hostId, string name = "test match", string password = "",
        bool freeMods = false)
    {
        return new ReadMatchResult(
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
        Assert.NotNull(_channelRegistry.GetByName("#multi_0"));
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
    public void Create_RegistryFull_ReturnsNull()
    {
        var service = MakeService();
        for (var i = 0; i < 64; i++)
        {
            var host = MakePlayer(i + 1, $"host{i}");
            RegisterAll(host);
            Assert.NotNull(Create(service, host, MakeMatchData(host.Id)));
        }

        var overflowHost = MakePlayer(1000, "overflow");
        RegisterAll(overflowHost);

        Assert.Null(Create(service, overflowHost, MakeMatchData(overflowHost.Id)));
    }

    [Fact]
    public void Join_CorrectPassword_OccupiesFreeSlotAndSendsMatchJoinSuccess()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id, password: "pw"))!;
        host.Dequeue();

        var joined = service.Join(guest, match, "pw");

        Assert.True(joined);
        Assert.Same(match, guest.Match);
        Assert.Equal(1, match.GetSlotId(guest.Id));
        Assert.Contains(ServerPacketWriter.MatchJoinSuccess(MatchPacketDataMapper.ToPacketData(match)),
            Chunk(guest.Dequeue()));
    }

    [Fact]
    public void Join_WrongPassword_FailsAndSendsMatchJoinFail()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id, password: "pw"))!;

        var joined = service.Join(guest, match, "wrong");

        Assert.False(joined);
        Assert.Null(guest.Match);
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(guest.Dequeue()));
    }

    [Fact]
    public void Join_StaffBypassesWrongPassword()
    {
        var host = MakePlayer(1, "host");
        var staff = MakePlayer(2, "mod");
        staff.Priv = UserPrivileges.Unrestricted | UserPrivileges.Moderator;
        RegisterAll(host, staff);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id, password: "pw"))!;

        Assert.True(service.Join(staff, match, "wrong"));
    }

    [Fact]
    public void Join_AlreadyInAnotherMatch_Fails()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var matchA = Create(service, host, MakeMatchData(host.Id))!;
        var otherHost = MakePlayer(3, "other");
        RegisterAll(host, guest, otherHost);
        var matchB = Create(service, otherHost, MakeMatchData(otherHost.Id))!;
        service.Join(guest, matchA, "");

        Assert.False(service.Join(guest, matchB, ""));
    }

    [Fact]
    public void Join_MatchFull_FailsAndSendsMatchJoinFail()
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

        Assert.False(service.Join(overflow, match, ""));
        Assert.Contains(ServerPacketWriter.MatchJoinFail(), Chunk(overflow.Dequeue()));
    }

    [Fact]
    public void Join_TeamVsMode_AssignsRedTeamToJoiningPlayer()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id))!;
        match.TeamType = MatchTeamType.TeamVs;

        service.Join(guest, match, "");

        Assert.Equal(MatchTeam.Red, match.GetSlot(guest.Id)!.Team);
    }

    [Fact]
    public void Leave_LastPlayer_RemovesMatchAndChannelAndDisposesToLobby()
    {
        var host = MakePlayer(1, "host");
        var lobbyMember = MakePlayer(2, "lobbyguy");
        RegisterAll(host, lobbyMember);
        _channelRegistry.Add(new ChannelSession(1, "#lobby", "t", 0, 0, true));
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id))!;
        var lobby = _channelRegistry.GetByName("#lobby")!;
        var membership = new ChannelMembershipService(_sessionRegistry, _channelRegistry);
        membership.Join(lobbyMember, lobby);
        lobbyMember.Dequeue();

        service.Leave(host, match);

        Assert.Null(_matchRegistry.GetById(match.Id));
        Assert.Null(_channelRegistry.GetByName("#multi_0"));
        Assert.Null(host.Match);
        Assert.Contains(ServerPacketWriter.DisposeMatch(match.Id), Chunk(lobbyMember.Dequeue()));
        Assert.Contains(match.DbId, _matchPersistence.EndedMatchIds);
    }

    [Fact]
    public void Leave_HostLeaves_TransfersHostToFirstOccupiedSlot()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id))!;
        service.Join(guest, match, "");
        guest.Dequeue();

        service.Leave(host, match);

        Assert.Equal(guest.Id, match.HostId);
        Assert.Contains(ServerPacketWriter.MatchTransferHost(), Chunk(guest.Dequeue()));
    }

    [Fact]
    public void Leave_SlotWasLocked_StaysLockedAfterReset()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id))!;
        service.Join(guest, match, "");
        match.GetSlot(guest.Id)!.Status = SlotStatus.Locked;

        service.Leave(guest, match);

        Assert.Equal(SlotStatus.Locked, match.Slots[1].Status);
        Assert.True(match.Slots[1].Empty);
    }

    [Fact]
    public void EnqueueState_BroadcastsToLobbyOnlyWhenLobbyHasMembers()
    {
        var host = MakePlayer(1, "host");
        var lobbyMember = MakePlayer(2, "lobbyguy");
        RegisterAll(host, lobbyMember);
        _channelRegistry.Add(new ChannelSession(1, "#lobby", "t", 0, 0, true));
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id))!;
        host.Dequeue();

        service.EnqueueState(match);
        Assert.Empty(lobbyMember.Dequeue()); // nobody in #lobby yet — no broadcast

        var lobby = _channelRegistry.GetByName("#lobby")!;
        new ChannelMembershipService(_sessionRegistry, _channelRegistry).Join(lobbyMember, lobby);
        lobbyMember.Dequeue();

        service.EnqueueState(match);
        Assert.NotEmpty(lobbyMember.Dequeue());
    }

    /// <summary>
    ///     `EnqueueChat` is `MpCommandService.Announce`'s transport — asserts it produces the exact same
    ///     bancho SendMessage bytes the old `Enqueue(..., lobby: false)` call did, since bancho recipients
    ///     go through <see cref="Basil.Application.Sessions.Irc.BanchoIrcBridgeConnection" /> now instead
    ///     of a direct packet build.
    /// </summary>
    [Fact]
    public void EnqueueChat_BroadcastsBanchoSendMessageToMatchChannelMembers()
    {
        var host = MakePlayer(1, "host");
        RegisterAll(host);
        var service = MakeService();
        var match = Create(service, host, MakeMatchData(host.Id))!;
        _sessionRegistry.GetById(host.Id).Returns(host);
        host.Dequeue();

        service.EnqueueChat(match, "BasilBot", BotBootstrapService.BotId, "Match starting soon");

        Assert.Equal(
            ServerPacketWriter.SendMessage("BasilBot", "Match starting soon", match.ChatChannelName,
                BotBootstrapService.BotId),
            host.Dequeue());
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

    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        private readonly Dictionary<string, ChannelSession> _byName = new();

        public void Seed(IReadOnlyList<Channel> channels)
        {
            throw new NotSupportedException();
        }

        public void Add(ChannelSession channel)
        {
            _byName[channel.Name] = channel;
        }

        public void Remove(string name)
        {
            _byName.Remove(name);
        }

        public ChannelSession? GetByName(string name)
        {
            return _byName.GetValueOrDefault(name);
        }

        public IReadOnlyList<ChannelSession> AutoJoinChannels => throw new NotSupportedException();

        public IReadOnlyList<ChannelSession> All => _byName.Values.ToList();
    }

    private sealed class FakeMatchRegistry : IMatchRegistry
    {
        private readonly Dictionary<int, MatchSession> _byId = new();
        private int _nextId;

        public MatchSession? GetById(int id)
        {
            return _byId.GetValueOrDefault(id);
        }

        public MatchSession? GetByDbId(int dbId)
        {
            return _byId.Values.FirstOrDefault(m => m.DbId == dbId);
        }

        public MatchSession? TryCreate(Func<int, MatchSession> factory)
        {
            if (_nextId >= 64) return null;

            var match = factory(_nextId);
            _byId[_nextId] = match;
            _nextId++;
            return match;
        }

        public void Remove(int id)
        {
            _byId.Remove(id);
        }

        public IReadOnlyList<MatchSession> All => _byId.Values.ToList();
    }

    private sealed class FakeMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private int _nextMatchId = 1;
        private int _nextRoundId = 1;

        public List<int> EndedMatchIds { get; } = [];

        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_nextMatchId++);
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            EndedMatchIds.Add(matchId);
            return Task.CompletedTask;
        }

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, int beatmapId, string mapMd5,
            GameMode mode, MatchWinCondition winCondition, MatchTeamType teamType,
            string beatmapArtist, string beatmapTitle, string beatmapVersion, string beatmapCreator,
            Mods mods, DateTime startedAt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_nextRoundId++);
        }

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<MatchRow?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<MatchRow?>(null);
        }

        public Task<IReadOnlyList<RoundRow>> FetchRoundsAsync(int matchId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RoundRow>>([]);
        }

        public Task<IReadOnlyList<MatchRow>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MatchRow>>([]);
        }

        public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task CreateEventAsync(MatchEventRow row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<MatchEventRow>> FetchEventsAsync(int matchId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MatchEventRow>>([]);
        public Task<IReadOnlyList<MatchRow>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MatchRow>>([]);
        public Task<IReadOnlyList<RoundRow>> FetchUnrecoveredRoundsAsync(int matchId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RoundRow>>([]);
    }
}