using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Application.UseCases.Multiplayer;
using Bancho.Domain;
using Bancho.Protocol;
using NSubstitute;
using Bancho.Application.Abstractions.Channels;
using Bancho.Application.Sessions.Channels;
using Bancho.Application.Sessions.Multiplayer;
using Bancho.Domain.Multiplayer;
using Bancho.Domain.Users;
using Bancho.Protocol.Multiplayer;
using Bancho.Protocol.Packets;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>Shared fakes/helpers for the ~19 match packet handler test files, avoiding one copy of the same plumbing per file.</summary>
internal static class MultiplayerTestSupport
{
    public sealed class FakeChannelRegistry : IChannelRegistry
    {
        private readonly Dictionary<string, ChannelSession> _byName = new();

        public void Seed(IReadOnlyList<Channel> channels) => throw new NotSupportedException();

        public void Add(ChannelSession channel) => _byName[channel.Name] = channel;

        public void Remove(string name) => _byName.Remove(name);

        public ChannelSession? GetByName(string name) => _byName.GetValueOrDefault(name);

        public IReadOnlyList<ChannelSession> AutoJoinChannels => throw new NotSupportedException();

        public IReadOnlyList<ChannelSession> All => _byName.Values.ToList();
    }

    public sealed class FakeMatchRegistry : IMatchRegistry
    {
        private readonly Dictionary<int, MatchSession> _byId = new();
        private int _nextId;

        public MatchSession? GetById(int id) => _byId.GetValueOrDefault(id);

        public MatchSession? TryCreate(Func<int, MatchSession> factory)
        {
            if (_nextId >= 64)
            {
                return null;
            }

            var match = factory(_nextId);
            _byId[_nextId] = match;
            _nextId++;
            return match;
        }

        public void Remove(int id) => _byId.Remove(id);

        public IReadOnlyList<MatchSession> All => _byId.Values.ToList();
    }

    /// <summary>Bundles the fakes a handler test needs, wired the same way DI wires the real MatchMembershipService.</summary>
    public sealed class Fixture
    {
        public FakeChannelRegistry ChannelRegistry { get; } = new();
        public FakeMatchRegistry MatchRegistry { get; } = new();
        public IPlayerSessionRegistry SessionRegistry { get; } = Substitute.For<IPlayerSessionRegistry>();
        public MatchMembershipService MatchMembership { get; }

        public Fixture()
        {
            MatchMembership = new MatchMembershipService(MatchRegistry, ChannelRegistry, SessionRegistry, new ChannelMembershipService(SessionRegistry));
        }

        public void RegisterAll(params PlayerSession[] sessions)
        {
            SessionRegistry.All.Returns(sessions);
            foreach (var session in sessions)
            {
                SessionRegistry.GetById(session.Id).Returns(session);
                SessionRegistry.GetByName(session.Name).Returns(session);
            }
        }

        /// <summary>Creates a match with `host` in slot 0, registered in <see cref="MatchRegistry"/> and its channel in <see cref="ChannelRegistry"/>.</summary>
        public MatchSession CreateMatch(PlayerSession host, MatchTeamTypes teamType = MatchTeamTypes.HeadToHead) =>
            MatchMembership.Create(host, MakeMatchData(host.Id, teamType: teamType))!;
    }

    public static PlayerSession MakePlayer(int id, string name) => new(id, name, "token", Privileges.Unrestricted, 0.0);

    public static ReadMatchResult MakeMatchData(
        int hostId, string name = "test match", string password = "", bool freeMods = false,
        int mapId = 100, string mapMd5 = "", MatchTeamTypes teamType = MatchTeamTypes.HeadToHead, int winCondition = 0) => new(
        Id: 0, InProgress: false, Powerplay: 0, Mods: 0, Name: name, Password: password,
        MapName: "Some Map", MapId: mapId, MapMd5: mapMd5.Length == 32 ? mapMd5 : new string('a', 32),
        SlotStatuses: [], SlotTeams: [], SlotIds: [], HostId: hostId, Mode: 0,
        WinCondition: winCondition, TeamType: (int)teamType, FreeMods: freeMods, SlotMods: [], Seed: 0);

    /// <summary>
    /// Builds a raw wire-format buffer BanchoPacketReader.ReadMatch can parse — the inverse of
    /// that method, for handler tests that read a MultiplayerMatch packet body. All 16 slots are
    /// left as status 0 (no player bits set), so no slot ids are written, matching what real
    /// CREATE_MATCH/MATCH_CHANGE_SETTINGS/MATCH_CHANGE_PASSWORD bodies look like from a client
    /// that hasn't populated slots client-side yet.
    /// </summary>
    public static BanchoPacketReader MatchRequestReader(
        int id, string name, string password, string mapName, int mapId, string mapMd5,
        int hostId, int mode = 0, int winCondition = 0, int teamType = 0, bool freeMods = false, int seed = 0)
    {
        var parts = new List<byte>();
        parts.AddRange(BitConverter.GetBytes((short)id));
        parts.Add(0); // in_progress
        parts.Add(0); // powerplay
        parts.AddRange(PacketWriter.WriteInt32(0)); // mods
        parts.AddRange(PacketWriter.WriteString(name));
        parts.AddRange(PacketWriter.WriteString(password));
        parts.AddRange(PacketWriter.WriteString(mapName));
        parts.AddRange(PacketWriter.WriteInt32(mapId));
        parts.AddRange(PacketWriter.WriteString(mapMd5));
        parts.AddRange(new byte[16]); // slot statuses — all 0 (no player)
        parts.AddRange(new byte[16]); // slot teams
        // no slot ids: no status has any player bits set
        parts.AddRange(PacketWriter.WriteInt32(hostId));
        parts.Add((byte)mode);
        parts.Add((byte)winCondition);
        parts.Add((byte)teamType);
        parts.Add((byte)(freeMods ? 1 : 0));
        // freeMods false above -> no per-slot mods written
        parts.AddRange(PacketWriter.WriteInt32(seed));
        return new BanchoPacketReader(parts.ToArray());
    }

    public static List<byte[]> Chunk(byte[] data)
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
}
