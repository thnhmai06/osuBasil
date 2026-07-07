using Bancho.Application.Abstractions.Channels;
using Bancho.Application.Sessions;
using Bancho.Application.Sessions.Channels;
using Bancho.Application.Sessions.Multiplayer;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain.Multiplayer;
using Bancho.Domain.Users;
using Bancho.Protocol.Multiplayer;
using Bancho.Protocol.Packets;
using NSubstitute;

namespace Bancho.Application.Tests.UseCases.Multiplayer;

/// <summary>Ported from Player.join_match/leave_match plus Match.enqueue/enqueue_state.</summary>
public class MatchMembershipServiceTests
{
    private readonly FakeChannelRegistry _channelRegistry = new();
    private readonly FakeMatchRegistry _matchRegistry = new();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();

    private MatchMembershipService MakeService()
    {
        return new MatchMembershipService(_matchRegistry, _channelRegistry, _sessionRegistry,
            new ChannelMembershipService(_sessionRegistry));
    }

    private static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
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

        var match = MakeService().Create(host, MakeMatchData(host.Id));

        Assert.NotNull(match);
        Assert.Same(match, host.Match);
        Assert.Equal(host.Id, match!.Slots[0].PlayerId);
        Assert.NotNull(_channelRegistry.GetByName("#multi_0"));
    }

    [Fact]
    public void Create_StripsPrivateSuffixFromPasswordAndMarksHistoryNotPublic()
    {
        var host = MakePlayer(1, "host");
        RegisterAll(host);

        var match = MakeService().Create(host, MakeMatchData(host.Id, password: "secret//private"));

        Assert.Equal("secret", match!.Password);
        Assert.False(match.HasPublicHistory);
    }

    [Fact]
    public void Create_RegistryFull_ReturnsNull()
    {
        var service = MakeService();
        for (var i = 0; i < 64; i++)
        {
            var host = MakePlayer(i + 1, $"host{i}");
            RegisterAll(host);
            Assert.NotNull(service.Create(host, MakeMatchData(host.Id)));
        }

        var overflowHost = MakePlayer(1000, "overflow");
        RegisterAll(overflowHost);

        Assert.Null(service.Create(overflowHost, MakeMatchData(overflowHost.Id)));
    }

    [Fact]
    public void Join_CorrectPassword_OccupiesFreeSlotAndSendsMatchJoinSuccess()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = service.Create(host, MakeMatchData(host.Id, password: "pw"))!;
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
        var match = service.Create(host, MakeMatchData(host.Id, password: "pw"))!;

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
        staff.Priv = Privileges.Unrestricted | Privileges.Moderator;
        RegisterAll(host, staff);
        var service = MakeService();
        var match = service.Create(host, MakeMatchData(host.Id, password: "pw"))!;

        Assert.True(service.Join(staff, match, "wrong"));
    }

    [Fact]
    public void Join_AlreadyInAnotherMatch_Fails()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var matchA = service.Create(host, MakeMatchData(host.Id))!;
        var otherHost = MakePlayer(3, "other");
        RegisterAll(host, guest, otherHost);
        var matchB = service.Create(otherHost, MakeMatchData(otherHost.Id))!;
        service.Join(guest, matchA, "");

        Assert.False(service.Join(guest, matchB, ""));
    }

    [Fact]
    public void Join_MatchFull_FailsAndSendsMatchJoinFail()
    {
        var host = MakePlayer(1, "host");
        RegisterAll(host);
        var service = MakeService();
        var match = service.Create(host, MakeMatchData(host.Id))!;
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
        var match = service.Create(host, MakeMatchData(host.Id))!;
        match.TeamType = MatchTeamTypes.TeamVs;

        service.Join(guest, match, "");

        Assert.Equal(MatchTeams.Red, match.GetSlot(guest.Id)!.Team);
    }

    [Fact]
    public void Leave_LastPlayer_RemovesMatchAndChannelAndDisposesToLobby()
    {
        var host = MakePlayer(1, "host");
        var lobbyMember = MakePlayer(2, "lobbyguy");
        RegisterAll(host, lobbyMember);
        _channelRegistry.Add(new ChannelSession(1, "#lobby", "t", 0, 0, true));
        var service = MakeService();
        var match = service.Create(host, MakeMatchData(host.Id))!;
        var lobby = _channelRegistry.GetByName("#lobby")!;
        var membership = new ChannelMembershipService(_sessionRegistry);
        membership.Join(lobbyMember, lobby);
        lobbyMember.Dequeue();

        service.Leave(host, match);

        Assert.Null(_matchRegistry.GetById(match.Id));
        Assert.Null(_channelRegistry.GetByName("#multi_0"));
        Assert.Null(host.Match);
        Assert.Contains(ServerPacketWriter.DisposeMatch(match.Id), Chunk(lobbyMember.Dequeue()));
    }

    [Fact]
    public void Leave_HostLeaves_TransfersHostToFirstOccupiedSlot()
    {
        var host = MakePlayer(1, "host");
        var guest = MakePlayer(2, "guest");
        RegisterAll(host, guest);
        var service = MakeService();
        var match = service.Create(host, MakeMatchData(host.Id))!;
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
        var match = service.Create(host, MakeMatchData(host.Id))!;
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
        var match = service.Create(host, MakeMatchData(host.Id))!;
        host.Dequeue();

        service.EnqueueState(match);
        Assert.Empty(lobbyMember.Dequeue()); // nobody in #lobby yet — no broadcast

        var lobby = _channelRegistry.GetByName("#lobby")!;
        new ChannelMembershipService(_sessionRegistry).Join(lobbyMember, lobby);
        lobbyMember.Dequeue();

        service.EnqueueState(match);
        Assert.NotEmpty(lobbyMember.Dequeue());
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
}