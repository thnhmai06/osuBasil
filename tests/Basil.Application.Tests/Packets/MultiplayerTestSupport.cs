using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Users;
using Basil.Application.Backgrounds;
using Basil.Application.Configurations;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Application.Sessions.Spectating;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;
using Channel = Basil.Domain.Channels.Channel;

namespace Basil.Application.Tests.Packets;

/// <summary>
///     Shared fakes/helpers for the ~19 match packet handler test files, avoiding one copy of the same plumbing per
///     file.
/// </summary>
internal static class MultiplayerTestSupport
{
	public static GameSession MakePlayer(int id, string name)
	{
		return new GameSession(id, name, "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
	}

	public static MatchState MakeMatchData(
		int hostId, string name = "test match", string password = "", bool freeMods = false,
		int mapId = 100, string mapMd5 = "", MatchTeamType teamType = MatchTeamType.HeadToHead, int winCondition = 0)
	{
		return new MatchState(
			0, false, 0, 0, name, password,
			"Some Map", mapId, mapMd5.Length == 32 ? mapMd5 : new string('a', 32),
			[], [], [], hostId, 0,
			winCondition, (int)teamType, freeMods, [], 0);
	}

	/// <summary>
	///     Builds a raw wire-format buffer PacketReader.ReadMatch can parse — the inverse of
	///     that method, for handler tests that read a MultiplayerMatch packet body. All 16 slots are
	///     left as status 0 (no userSession bits set), so no slot ids are written, matching what real
	///     CREATE_MATCH/MATCH_CHANGE_SETTINGS/MATCH_CHANGE_PASSWORD bodies look like from a client
	///     that hasn't populated slots client-side yet.
	/// </summary>
	public static PacketReader MatchRequestReader(
		int id, string name, string password, string mapName, int mapId, string mapMd5,
		int hostId, int mode = 0, int winCondition = 0, int teamType = 0, bool freeMods = false, int seed = 0)
	{
		var parts = new List<byte>();
		parts.AddRange(BitConverter.GetBytes((short)id));
		parts.Add(0); // in_progress
		parts.Add(0); // powerplay
		parts.AddRange(BinaryWriter.WriteInt32(0)); // mods
		parts.AddRange(BinaryWriter.WriteString(name));
		parts.AddRange(BinaryWriter.WriteString(password));
		parts.AddRange(BinaryWriter.WriteString(mapName));
		parts.AddRange(BinaryWriter.WriteInt32(mapId));
		parts.AddRange(BinaryWriter.WriteString(mapMd5));
		parts.AddRange(new byte[16]); // slot statuses — all 0 (no userSession)
		parts.AddRange(new byte[16]); // slot teams
		// no slot ids: no status has any userSession bits set
		parts.AddRange(BinaryWriter.WriteInt32(hostId));
		parts.Add((byte)mode);
		parts.Add((byte)winCondition);
		parts.Add((byte)teamType);
		parts.Add((byte)(freeMods ? 1 : 0));
		if (freeMods)
			for (var i = 0; i < 16; i++)
				parts.AddRange(BinaryWriter
					.WriteInt32(
						0)); // per-slot mods — PacketReader.ReadMatch always reads 16 when freeMods is set
		parts.AddRange(BinaryWriter.WriteInt32(seed));
		return new PacketReader(parts.ToArray());
	}

	/// <summary>Dummy valid beatmap for tests that need `beatmapRepository.FetchOneAsync` to resolve successfully.</summary>
	public static Beatmap MakeBeatmap(int id = 100, string md5 = "")
	{
		var actualMd5 = md5.Length == 32 ? md5 : new string('a', 32);
		var mapset = new Beatmapset(1, "Artist", "Title", "Creator", DateTime.UtcNow, DateTime.UtcNow);
		return new Beatmap(actualMd5, id, mapset, "Normal", "map.osu",
			new Difficulty(GameMode.Standard, 180, TimeSpan.FromMinutes(2), 4, 8, 8, 5, 5.0),
			new OsuBeatmapObjectCounts { MaxCombo = 500 });
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

		public IReadOnlyCollection<ChannelSession> All => _byName.Values;
	}

	public sealed class FakeMatchRegistry(IChannelRegistry channelRegistry, IMatchRepository matchRepository)
		: IMatchRegistry
	{
		private readonly Dictionary<int, MatchSession> _byId = new();
		private int _nextDbId = 1;

		public MatchSession? GetById(int id)
		{
			return _byId.GetValueOrDefault(id);
		}

		public MatchSession? GetByDbId(int dbId)
		{
			return _byId.Values.FirstOrDefault(m => m.DbId == dbId);
		}

		public Task<MatchSession> CreateAsync(MatchState data, int hostId,
			CancellationToken cancellationToken = default)
		{
			var id = 0;
			while (_byId.ContainsKey(id)) id++;
			var dbId = _nextDbId++;

			// Mirrors InMemoryMatchRegistry.BuildNew: data.MapId is the wire value (0/-1 both mean
			// "no map" at this boundary); MatchSession.MapId is null in that case, not a sentinel.
			var mapId = data.MapId <= 0 ? null : (int?)data.MapId;

			var match = new MatchSession(
				id, data.Name, data.Password, data.MapName, mapId, data.MapMd5,
				hostId, (GameMode)data.Mode, (Mods)data.Mods, (MatchWinCondition)data.WinCondition,
				(MatchTeamType)data.TeamType, data.FreeMods, data.Seed, $"#mp_{dbId}")
			{
				DbId = dbId
			};
			_byId[id] = match;
			channelRegistry.Add(new ChannelSession(
				0, match.ChatChannelName,
				0, 0, false, "#multiplayer", true)
			{
				Topic = match.Name
			});
			return Task.FromResult(match);
		}

		public void Remove(int id)
		{
			if (!_byId.Remove(id, out var match)) return;
			channelRegistry.Remove(match.ChatChannelName);
			_ = matchRepository.SetMatchEndedAsync(match.DbId, DateTimeOffset.UtcNow.UtcDateTime);
		}

		public IReadOnlyCollection<MatchSession> All => _byId.Values;
	}

	/// <summary>In-memory stand-in for the Matches/Rounds tables — auto-incrementing ids, nothing persisted.</summary>
	public sealed class FakeMatchRepository : IMatchRepository
	{
		private int _nextMatchId = 1;
		private int _nextRoundId = 1;

		public Task<int> CreateMatchAsync(string name, DateTime createdAt,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_nextMatchId++);
		}

		public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
		{
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

	/// <summary>
	///     In-memory stand-in for the round-end outbox: <see cref="Enqueue" /> completes synchronously
	///     (no background drain to wait for), matching how <see cref="FakeMatchRepository" /> itself
	///     completes synchronously — keeps every existing synchronous test call site unchanged.
	/// </summary>
	public sealed class FakeMatchRoundEndOutbox : IMatchRoundEndOutbox
	{
		public List<RoundEndWrite> Enqueued { get; } = [];
		public List<int> Drained { get; } = [];

		/// <summary>When set, <see cref="Enqueue" /> throws instead of recording, simulating a full queue.</summary>
		public bool ThrowFull { get; set; }

		public void Enqueue(RoundEndWrite write)
		{
			if (ThrowFull) throw new MatchRoundEndOutboxFullException(write.MatchId, write.RoundId);
			Enqueued.Add(write);
		}

		public Task DrainAsync(int matchId, CancellationToken cancellationToken = default)
		{
			Drained.Add(matchId);
			return Task.CompletedTask;
		}
	}

	/// <summary>Records what would have been pushed to SSE subscribers, without any real channel/connection.</summary>
	/// <remarks>
	///     Subscribe methods only record; no test currently subscribes through this fake and asserts
	///     on delivery, so they return a trivial no-op handle rather than modeling real per-match
	///     dispatch (see <see cref="Basil.Infrastructure.Sessions.MatchLiveEvents" /> for that).
	/// </remarks>
	public sealed class FakeMatchLiveEvents : IMatchLiveEvents
	{
		private sealed class NoOpSubscription : IDisposable
		{
			public void Dispose()
			{
			}
		}

		public List<(int MatchDbId, byte[] Payload)> MainPublishes { get; } = [];
		public List<(int MatchDbId, string PlayerName, byte[] Payload)> PlayerPublishes { get; } = [];
		public List<(int MatchDbId, byte[] Payload)> SettingsPublishes { get; } = [];
		public List<(int MatchDbId, int SlotIndex, byte[] Payload)> SlotPublishes { get; } = [];
		public List<(int MatchDbId, byte[] Payload)> HostPublishes { get; } = [];
		public List<(int MatchDbId, byte[] Payload)> RefsPublishes { get; } = [];
		public List<(int MatchDbId, byte[] Payload)> BansPublishes { get; } = [];
		public List<(int MatchDbId, byte[] Payload)> TimerPublishes { get; } = [];
		public List<(int MatchDbId, byte[] Payload)> SlotsPublishes { get; } = [];
		public List<int> Forgotten { get; } = [];

		/// <summary>Always true — this fake exists to record every publish call, not to model subscriber presence.</summary>
		public bool HasPlayerScoreSubscribers(int matchDbId)
		{
			return true;
		}

		public IDisposable SubscribeMain(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishMain(int matchDbId, byte[] payload)
		{
			MainPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribePlayerScore(int matchDbId, Action<string, byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
		{
			PlayerPublishes.Add((matchDbId, playerName, payload));
		}

		public IDisposable SubscribeSettings(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishSettings(int matchDbId, byte[] payload)
		{
			SettingsPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribeSlot(int matchDbId, Action<int, byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishSlot(int matchDbId, int slotIndex, byte[] payload)
		{
			SlotPublishes.Add((matchDbId, slotIndex, payload));
		}

		public IDisposable SubscribeHost(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishHost(int matchDbId, byte[] payload)
		{
			HostPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribeRefs(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishRefs(int matchDbId, byte[] payload)
		{
			RefsPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribeBans(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishBans(int matchDbId, byte[] payload)
		{
			BansPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribeTimer(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishTimer(int matchDbId, byte[] payload)
		{
			TimerPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribeSlots(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishSlots(int matchDbId, byte[] payload)
		{
			SlotsPublishes.Add((matchDbId, payload));
		}

		public IDisposable SubscribeChat(int matchDbId, Action<byte[]> handler)
		{
			return new NoOpSubscription();
		}

		public void PublishChat(int matchDbId, byte[] payload)
		{
		}

		public void Forget(int matchDbId)
		{
			Forgotten.Add(matchDbId);
		}
	}

	/// <summary>Records what would have been pushed to the /spec/{id} SSE subscribers.</summary>
	public sealed class FakePlayerInputEvents : IPlayerInputEvents
	{
		public List<(int PlayerId, byte[] Payload)> Publishes { get; } = [];

		public event Action<int, byte[]>? InputPublished;

		/// <summary>Always true — this fake exists to record every publish call, not to model subscriber presence.</summary>
		public bool HasSubscribers => true;

		public void PublishInput(int playerId, byte[] payload)
		{
			Publishes.Add((playerId, payload));
			InputPublished?.Invoke(playerId, payload);
		}
	}

	/// <summary>Bundles the fakes a handler test needs, wired the same way DI wires the real MatchMembershipService.</summary>
	public sealed class Fixture
	{
		public Fixture()
		{
			MatchRegistry = new FakeMatchRegistry(ChannelRegistry, MatchRepository);

			BeatmapRepository.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
				Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(MakeBeatmap());

			ChannelMembership = new ChannelMembershipService(SessionRegistry, IrcSessionRegistry, ChannelRegistry,
				Substitute.For<IMatchRegistry>(), Substitute.For<IMatchLiveEvents>(), Options.Create(new IrcOptions()));

			MatchMembership = new MatchMembershipService(MatchRegistry, ChannelRegistry, SessionRegistry,
				IrcSessionRegistry, ChannelMembership, MatchRepository, RoundEndOutbox, EventBus,
				BeatmapRepository, UserRepository, NullLogger<MatchMembershipService>.Instance);
		}

		public FakeChannelRegistry ChannelRegistry { get; } = new();
		public FakeMatchRegistry MatchRegistry { get; }
		public FakeMatchRepository MatchRepository { get; } = new();
		public FakeMatchRoundEndOutbox RoundEndOutbox { get; } = new();
		public FakeMatchLiveEvents EventBus { get; } = new();
		public ISessionRegistry<GameSession> SessionRegistry { get; } = Substitute.For<ISessionRegistry<GameSession>>();

		public ISessionRegistry<IrcSession> IrcSessionRegistry { get; } =
			Substitute.For<ISessionRegistry<IrcSession>>();

		public IUserRepository UserRepository { get; } = Substitute.For<IUserRepository>();

		/// <summary>Defaults to resolving any lookup to a valid beatmap — override per-test for missing-map scenarios.</summary>
		public IBeatmapRepository BeatmapRepository { get; } = Substitute.For<IBeatmapRepository>();

		public ChannelMembershipService ChannelMembership { get; }
		public MatchMembershipService MatchMembership { get; }

		public void RegisterAll(params GameSession[] sessions)
		{
			SessionRegistry.All.Returns(sessions);
			foreach (var session in sessions)
			{
				SessionRegistry.GetByUserId(session.Id).Returns(session);
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
		public MatchSession CreateMatch(UserSession host, MatchTeamType teamType = MatchTeamType.HeadToHead,
			bool hostIsReferee = true)
		{
			var match = MatchMembership.CreateAsync(host, MakeMatchData(host.Id, teamType: teamType))
				.GetAwaiter().GetResult()!;
			if (hostIsReferee) match.AddReferee(host.Id);
			return match;
		}
	}
}