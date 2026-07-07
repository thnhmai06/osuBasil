using NSubstitute;
using OpenOsuTournament.Bancho.Application.Abstractions;
using OpenOsuTournament.Bancho.Application.Abstractions.Channels;
using OpenOsuTournament.Bancho.Application.Abstractions.Multiplayer;
using OpenOsuTournament.Bancho.Application.Sessions;
using OpenOsuTournament.Bancho.Application.Sessions.Channels;
using OpenOsuTournament.Bancho.Application.Sessions.Multiplayer;
using OpenOsuTournament.Bancho.Application.UseCases.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Multiplayer;
using OpenOsuTournament.Bancho.Domain.Users;
using OpenOsuTournament.Bancho.Protocol.Multiplayer;
using OpenOsuTournament.Bancho.Protocol.Packets;

namespace OpenOsuTournament.Bancho.Application.Tests.PacketHandlers;

/// <summary>
///     Shared fakes/helpers for the ~19 match packet handler test files, avoiding one copy of the same plumbing per
///     file.
/// </summary>
internal static class MultiplayerTestSupport
{
    public static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", Privileges.Unrestricted, 0.0);
    }

    public static ReadMatchResult MakeMatchData(
        int hostId, string name = "test match", string password = "", bool freeMods = false,
        int mapId = 100, string mapMd5 = "", MatchTeamTypes teamType = MatchTeamTypes.HeadToHead, int winCondition = 0)
    {
        return new ReadMatchResult(
            0, false, 0, 0, name, password,
            "Some Map", mapId, mapMd5.Length == 32 ? mapMd5 : new string('a', 32),
            [], [], [], hostId, 0,
            winCondition, (int)teamType, freeMods, [], 0);
    }

    /// <summary>
    ///     Builds a raw wire-format buffer BanchoPacketReader.ReadMatch can parse — the inverse of
    ///     that method, for handler tests that read a MultiplayerMatch packet body. All 16 slots are
    ///     left as status 0 (no player bits set), so no slot ids are written, matching what real
    ///     CREATE_MATCH/MATCH_CHANGE_SETTINGS/MATCH_CHANGE_PASSWORD bodies look like from a client
    ///     that hasn't populated slots client-side yet.
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

    public sealed class FakeChannelRegistry : IChannelRegistry
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

    public sealed class FakeMatchRegistry : IMatchRegistry
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

    /// <summary>In-memory stand-in for the Matches/Rounds tables — auto-incrementing ids, nothing persisted.</summary>
    public sealed class FakeMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private int _nextMatchId = 1;
        private int _nextRoundId = 1;

        public Task<int> CreateMatchAsync(string name, int mode, int winCondition, int teamType, int hostId,
            bool hasPublicHistory, DateTime createdAt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_nextMatchId++);
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> CreateRoundAsync(int matchId, int roundIndex, int beatmapId, string mapMd5, int mods,
            DateTime startedAt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_nextRoundId++);
        }

        public Task SetRoundEndedAsync(int roundId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>Bundles the fakes a handler test needs, wired the same way DI wires the real MatchMembershipService.</summary>
    public sealed class Fixture
    {
        public Fixture()
        {
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);

            MatchMembership = new MatchMembershipService(MatchRegistry, ChannelRegistry, SessionRegistry,
                new ChannelMembershipService(SessionRegistry), MatchPersistence, clock);
        }

        public FakeChannelRegistry ChannelRegistry { get; } = new();
        public FakeMatchRegistry MatchRegistry { get; } = new();
        public FakeMatchPersistenceRepository MatchPersistence { get; } = new();
        public IPlayerSessionRegistry SessionRegistry { get; } = Substitute.For<IPlayerSessionRegistry>();
        public MatchMembershipService MatchMembership { get; }

        public void RegisterAll(params PlayerSession[] sessions)
        {
            SessionRegistry.All.Returns(sessions);
            foreach (var session in sessions)
            {
                SessionRegistry.GetById(session.Id).Returns(session);
                SessionRegistry.GetByName(session.Name).Returns(session);
            }
        }

        /// <summary>
        ///     Creates a match with `host` in slot 0, registered in <see cref="MatchRegistry" /> and its channel in
        ///     <see cref="ChannelRegistry" />. The fake persistence repo completes synchronously, so blocking on the
        ///     task here is safe and keeps every existing synchronous test call site unchanged.
        /// </summary>
        public MatchSession CreateMatch(PlayerSession host, MatchTeamTypes teamType = MatchTeamTypes.HeadToHead)
        {
            return MatchMembership.CreateAsync(host, MakeMatchData(host.Id, teamType: teamType))
                .GetAwaiter().GetResult()!;
        }
    }
}