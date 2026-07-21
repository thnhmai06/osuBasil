using System.Threading.Channels;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
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
using Channel = Basil.Application.Abstractions.Channels.Channel;

namespace Basil.Application.Tests.PacketHandlers;

/// <summary>
///     Shared fakes/helpers for the ~19 match packet handler test files, avoiding one copy of the same plumbing per
///     file.
/// </summary>
internal static class MultiplayerTestSupport
{
    public static PlayerSession MakePlayer(int id, string name)
    {
        return new PlayerSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
    }

    public static ReadMatchResult MakeMatchData(
        int hostId, string name = "test match", string password = "", bool freeMods = false,
        int mapId = 100, string mapMd5 = "", MatchTeamType teamType = MatchTeamType.HeadToHead, int winCondition = 0)
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

    /// <summary>In-memory stand-in for the Matches/Rounds tables — auto-incrementing ids, nothing persisted.</summary>
    public sealed class FakeMatchPersistenceRepository : IMatchPersistenceRepository
    {
        private int _nextMatchId = 1;
        private int _nextRoundId = 1;

        public Task<int> CreateMatchAsync(string name, DateTime createdAt, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_nextMatchId++);
        }

        public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
        {
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

    /// <summary>Records what would have been pushed to WS subscribers, without any real channel/socket.</summary>
    public sealed class FakeMatchEventBus : IMatchEventBus
    {
        public List<(int MatchDbId, byte[] Payload)> MainPublishes { get; } = [];
        public List<(int MatchDbId, string PlayerName, byte[] Payload)> PlayerPublishes { get; } = [];
        public List<(int MatchDbId, byte[] Payload)> InputPublishes { get; } = [];

        public IDisposable SubscribeMain(int matchDbId, ChannelWriter<byte[]> writer)
        {
            return NullDisposable.Instance;
        }

        public IDisposable SubscribePlayer(int matchDbId, string playerName, ChannelWriter<byte[]> writer)
        {
            return NullDisposable.Instance;
        }

        public IDisposable SubscribeInput(int matchDbId, ChannelWriter<byte[]> writer)
        {
            return NullDisposable.Instance;
        }

        public void PublishMain(int matchDbId, byte[] payload)
        {
            MainPublishes.Add((matchDbId, payload));
        }

        public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
        {
            PlayerPublishes.Add((matchDbId, playerName, payload));
        }

        public void PublishInput(int matchDbId, byte[] payload)
        {
            InputPublishes.Add((matchDbId, payload));
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Bundles the fakes a handler test needs, wired the same way DI wires the real MatchMembershipService.</summary>
    public sealed class Fixture
    {
        public Fixture()
        {
            MatchMembership = new MatchMembershipService(MatchRegistry, ChannelRegistry, SessionRegistry,
                new ChannelMembershipService(SessionRegistry, ChannelRegistry), MatchPersistence, EventBus,
                Substitute.For<IMapRepository>());
        }

        public FakeChannelRegistry ChannelRegistry { get; } = new();
        public FakeMatchRegistry MatchRegistry { get; } = new();
        public FakeMatchPersistenceRepository MatchPersistence { get; } = new();
        public FakeMatchEventBus EventBus { get; } = new();
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
        ///     task here is safe and keeps every existing synchronous test call site unchanged. `hostIsReferee`
        ///     defaults to true purely for test convenience (most tests want the host able to issue `!mp` commands
        ///     right away) — MatchSession.IsReferee itself does NOT auto-include the host; pass false to exercise
        ///     that real host-is-not-a-referee behavior.
        /// </summary>
        public MatchSession CreateMatch(PlayerSession host, MatchTeamType teamType = MatchTeamType.HeadToHead,
            bool hostIsReferee = true)
        {
            var match = MatchMembership.CreateAsync(host, MakeMatchData(host.Id, teamType: teamType))
                .GetAwaiter().GetResult()!;
            if (hostIsReferee) match.AddReferee(host.Id);
            return match;
        }
    }
}