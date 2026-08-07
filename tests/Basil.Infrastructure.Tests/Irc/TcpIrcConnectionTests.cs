using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Multiplayer;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Content;
using Basil.Application.Configurations;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Irc;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Services.Spectating;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Channels;
using Basil.Domain.Login;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;
using Basil.Domain.Social;
using Basil.Domain.Users;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Security;
using Basil.Infrastructure.Sessions;
using Basil.Protocol.Multiplayer;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Irc;

/// <summary>
///     Real loopback TCP round trip through <see cref="TcpIrcConnection" /> — proves the IRC core (auth,
///     JOIN, PRIVMSG broadcast) works over an actual socket, not just via in-memory fakes. No SQLite
///     involved (<see cref="FakeUserRepository" /> stands in for the bcrypt password lookup) since this
///     is testing the wire/session layer, not persistence.
/// </summary>
public class TcpIrcConnectionTests
{
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

	private readonly IOptions<IrcOptions> _fakeIrcOptions = new OptionsWrapper<IrcOptions>(new IrcOptions());

	[Fact]
	public async Task TwoIrcClients_LoginAndPrivmsgInAutoJoinChannel_MessageArrivesAtTheOtherClient()
	{
		var tokenGenerator = new GuidTokenGenerator();
		var hasher = new BCryptPasswordHasher();
		var users = new FakeUserRepository();
		users.Add(new User(1, "alice", Country.Xx, 0, default),
			HashPassword(hasher, "alice-key"));
		users.Add(new User(2, "bob", Country.Xx, 0, default),
			HashPassword(hasher, "bob-key"));

		var gameRegistry = new GameSessionRegistry();
		var ircRegistry = new IrcSessionRegistry();
		var channelRegistry = new InMemoryChannelRegistry();
		channelRegistry.Seed([new Channel(1, "#osu", "General", 0, 0, true)]);

		var matchRegistry = new InMemoryMatchRegistry(channelRegistry, new NotSupportedMatchRepository());
		var channelMembership = new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry,
			matchRegistry, new NoOpMatchLiveEvents(), _fakeIrcOptions);
		var chatDispatch = new ChatDispatchService(channelRegistry, gameRegistry, channelMembership, users,
			new NotSupportedRelationshipRepository(), new NullCommandDispatcher(),
			matchRegistry, NullLogger<ChatDispatchService>.Instance);
		var ircQueries = MakeQueryService(gameRegistry, ircRegistry, channelRegistry, channelMembership);
		var authService = new IrcAuthenticationService(users, ircRegistry, channelRegistry, channelMembership,
			ircQueries, _fakeIrcOptions, hasher, tokenGenerator);
		var playerLogout = MakePlayerLogoutService(gameRegistry, ircRegistry, channelRegistry, channelMembership);

		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var acceptTask = Task.Run(async () =>
		{
			for (var i = 0; i < 2; i++)
			{
				var client = await listener.AcceptTcpClientAsync(cts.Token);
				var connection = new TcpIrcConnection(client, authService, chatDispatch, channelMembership,
					ircQueries, channelRegistry, playerLogout, _fakeIrcOptions,
					NullLogger<TcpIrcConnection>.Instance, i);
				_ = connection.RunAsync(cts.Token);
			}
		}, cts.Token);

		using var aliceClient = new TcpClient();
		await aliceClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);
		using var bobClient = new TcpClient();
		await bobClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);

		await acceptTask;

		await using var aliceStream = aliceClient.GetStream();
		using var aliceReader = new StreamReader(aliceStream, Encoding.UTF8);
		await using var bobStream = bobClient.GetStream();
		using var bobReader = new StreamReader(bobStream, Encoding.UTF8);

		await LoginAsync(aliceStream, aliceReader, "alice", "alice-key");
		await LoginAsync(bobStream, bobReader, "bob", "bob-key");

		await WriteLineAsync(aliceStream, "PRIVMSG #osu :hello bob");

		var received = await ReadUntilAsync(bobReader, line => line.Contains("PRIVMSG"));

		Assert.Contains("#osu", received);
		Assert.Contains("hello bob", received);
		Assert.StartsWith(":alice!", received);

		// A one-parameter TOPIC is a read, not an under-parameterized change: it must reach the topic
		// reply rather than the "not enough parameters" numeric.
		await WriteLineAsync(aliceStream, "TOPIC #osu");
		var topic = await ReadUntilAsync(aliceReader, line => line.Contains(" 332 ") || line.Contains(" 461 "));
		Assert.Contains(" 332 ", topic);
		Assert.Contains("General", topic);

		await WriteLineAsync(aliceStream, "FOO bar");
		var unknown = await ReadUntilAsync(aliceReader, line => line.Contains(" 421 "));
		Assert.Contains("FOO", unknown);

		// A PONG answers the server's own keepalive; a numeric back would break that round trip, so
		// the next reply must be the VERSION sent after it.
		await WriteLineAsync(aliceStream, "PONG :basil");
		await WriteLineAsync(aliceStream, "VERSION");
		var afterPong = await ReadUntilAsync(aliceReader, line => line.Contains(" 351 ") || line.Contains(" 421 "));
		Assert.Contains(" 351 ", afterPong);

		listener.Stop();
	}

	/// <summary>
	///     A modern IRC client opens with CAP before it has registered. Answering that with "you have
	///     not registered" reads to the client as a server that does not speak IRC capabilities at all,
	///     so the negotiation is answered (empty) while everything genuinely out of order is refused.
	/// </summary>
	[Fact]
	public async Task UnregisteredClient_CapIsAnsweredEmptyWhileAnyOtherCommandIsRefused()
	{
		var hasher = new BCryptPasswordHasher();
		var users = new FakeUserRepository();
		var gameRegistry = new GameSessionRegistry();
		var ircRegistry = new IrcSessionRegistry();
		var channelRegistry = new InMemoryChannelRegistry();
		channelRegistry.Seed([]);

		var matchRegistry = new InMemoryMatchRegistry(channelRegistry, new NotSupportedMatchRepository());
		var channelMembership =
			new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry, matchRegistry,
				new NoOpMatchLiveEvents(), _fakeIrcOptions);
		var chatDispatch = new ChatDispatchService(channelRegistry, gameRegistry, channelMembership, users,
			new NotSupportedRelationshipRepository(), new NullCommandDispatcher(),
			matchRegistry, NullLogger<ChatDispatchService>.Instance);
		var ircQueries = MakeQueryService(gameRegistry, ircRegistry, channelRegistry, channelMembership);
		var authService = new IrcAuthenticationService(users, ircRegistry, channelRegistry, channelMembership,
			ircQueries, _fakeIrcOptions, hasher, new GuidTokenGenerator());
		var playerLogout = MakePlayerLogoutService(gameRegistry, ircRegistry, channelRegistry, channelMembership);

		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var acceptTask = Task.Run(async () =>
		{
			var accepted = await listener.AcceptTcpClientAsync(cts.Token);
			var connection = new TcpIrcConnection(accepted, authService, chatDispatch, channelMembership,
				ircQueries, channelRegistry, playerLogout, _fakeIrcOptions,
				NullLogger<TcpIrcConnection>.Instance, 1);
			_ = connection.RunAsync(cts.Token);
		}, cts.Token);

		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
		await acceptTask;

		await using var stream = client.GetStream();
		using var reader = new StreamReader(stream, Encoding.UTF8);

		await WriteLineAsync(stream, "CAP LS 302");
		var cap = await ReadUntilAsync(reader, line => line.Contains("CAP") || line.Contains(" 451 "));
		Assert.Equal(":Basil CAP * LS :", cap);

		await WriteLineAsync(stream, "WHOIS someone");
		var refused = await ReadUntilAsync(reader, line => line.Contains(" 451 "));
		Assert.Contains(IrcReplies.YouHaveNotRegistered, refused);

		listener.Stop();
	}

	/// <summary>
	///     A match room's channel is off-limits from IRC to anyone who is neither a referee nor
	///     currently seated in it — the denial and a genuinely missing channel must be indistinguishable
	///     on the wire, and becoming a referee must open the door without ever having been seated.
	/// </summary>
	[Fact]
	public async Task JoiningAMatchRoomChannel_RequiresRefereeOrSeatedStanding()
	{
		var hasher = new BCryptPasswordHasher();
		var users = new FakeUserRepository();
		users.Add(new User(1, "alice", Country.Xx, 0, default), HashPassword(hasher, "alice-key"));

		var gameRegistry = new GameSessionRegistry();
		var ircRegistry = new IrcSessionRegistry();
		var channelRegistry = new InMemoryChannelRegistry();
		channelRegistry.Seed([]);
		channelRegistry.Add(new ChannelSession(0, "#mp_5", 0, 0, false, "#multiplayer", true));

		var match = new MatchSession(0, "Grand Finals", "", "map", 42, "md5", 9, GameMode.Standard,
			Mods.NoMod, MatchWinCondition.Score, MatchTeamType.HeadToHead, false, 0, "#mp_5");
		var matchRegistry = new FakeMatchRegistry(match);

		var channelMembership = new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry,
			matchRegistry, new NoOpMatchLiveEvents(), _fakeIrcOptions);
		var chatDispatch = new ChatDispatchService(channelRegistry, gameRegistry, channelMembership, users,
			new NotSupportedRelationshipRepository(), new NullCommandDispatcher(), matchRegistry,
			NullLogger<ChatDispatchService>.Instance);
		var ircQueries = MakeQueryService(gameRegistry, ircRegistry, channelRegistry, channelMembership);
		var authService = new IrcAuthenticationService(users, ircRegistry, channelRegistry, channelMembership,
			ircQueries, _fakeIrcOptions, hasher, new GuidTokenGenerator());
		var playerLogout = MakePlayerLogoutService(gameRegistry, ircRegistry, channelRegistry, channelMembership);

		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var acceptTask = Task.Run(async () =>
		{
			var accepted = await listener.AcceptTcpClientAsync(cts.Token);
			var connection = new TcpIrcConnection(accepted, authService, chatDispatch, channelMembership,
				ircQueries, channelRegistry, playerLogout, _fakeIrcOptions,
				NullLogger<TcpIrcConnection>.Instance, 1);
			_ = connection.RunAsync(cts.Token);
		}, cts.Token);

		using var aliceClient = new TcpClient();
		await aliceClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);
		await acceptTask;

		await using var aliceStream = aliceClient.GetStream();
		using var aliceReader = new StreamReader(aliceStream, Encoding.UTF8);
		await LoginAsync(aliceStream, aliceReader, "alice", "alice-key");

		await WriteLineAsync(aliceStream, "JOIN #mp_5");
		var denied = await ReadUntilAsync(aliceReader,
			line => line.Contains(" 473 ") || line.Contains("JOIN #mp_5"));
		Assert.Contains(" 473 ", denied);
		Assert.Contains(IrcReplies.CannotJoinChannel, denied);

		match.AddReferee(1);
		await WriteLineAsync(aliceStream, "JOIN #mp_5");
		var allowed = await ReadUntilAsync(aliceReader,
			line => line.Contains(" 473 ") || line.Contains("JOIN #mp_5"));
		Assert.Contains("JOIN #mp_5", allowed);
		Assert.StartsWith(":alice!", allowed);

		await WriteLineAsync(aliceStream, "JOIN #doesnotexist");
		var missing = await ReadUntilAsync(aliceReader, line => line.Contains(" 403 "));
		Assert.Contains(" 403 ", missing);
		Assert.Contains(IrcReplies.NoSuchChannel, missing);

		listener.Stop();
	}

	/// <summary>Backs <see cref="JoiningAMatchRoomChannel_RequiresRefereeOrSeatedStanding" /> with a single fixed match.</summary>
	private sealed class FakeMatchRegistry(MatchSession match) : IMatchRegistry
	{
		public IReadOnlyCollection<MatchSession> All => [match];

		public MatchSession? GetById(int id)
		{
			return id == match.Id ? match : null;
		}

		public MatchSession? GetByDbId(int dbId)
		{
			return dbId == match.DbId ? match : null;
		}

		public Task<MatchSession> CreateAsync(MatchState data, int hostId,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public void Remove(int id)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>
	///     Proves the cross-world seam: a "bancho" <see cref="UserSession" /> (no socket behind it,
	///     exactly like a real one would look from the chat core's perspective) and a real IRC TCP
	///     client share the same channel through <see cref="ChannelMembershipService" />/
	///     <see cref="ChatDispatchService" /> — a message from either side reaches the other.
	/// </summary>
	[Fact]
	public async Task BanchoSessionAndRealIrcClient_ShareAChannel_MessagesCrossBothWays()
	{
		var hasher = new BCryptPasswordHasher();
		var users = new FakeUserRepository();
		users.Add(new User(1, "alice", Country.Xx, 0, default),
			HashPassword(hasher, "alice-key"));

		var tokenGenerator = new GuidTokenGenerator();
var gameRegistry = new GameSessionRegistry();
		var ircRegistry = new IrcSessionRegistry();
		var channelRegistry = new InMemoryChannelRegistry();
		channelRegistry.Seed([new Channel(1, "#osu", "General", 0, 0, true)]);

		var matchRegistry = new InMemoryMatchRegistry(channelRegistry, new NotSupportedMatchRepository());
		var channelMembership = new ChannelMembershipService(gameRegistry, ircRegistry, channelRegistry,
			matchRegistry, new NoOpMatchLiveEvents(), _fakeIrcOptions);
		var chatDispatch = new ChatDispatchService(channelRegistry, gameRegistry, channelMembership, users,
			new NotSupportedRelationshipRepository(), new NullCommandDispatcher(),
			matchRegistry, NullLogger<ChatDispatchService>.Instance);
		var ircQueries = MakeQueryService(gameRegistry, ircRegistry, channelRegistry, channelMembership);
		var authService = new IrcAuthenticationService(users, ircRegistry, channelRegistry, channelMembership,
			ircQueries, _fakeIrcOptions, hasher, tokenGenerator);
		var playerLogout = MakePlayerLogoutService(gameRegistry, ircRegistry, channelRegistry, channelMembership);

		// Stands in for a real bancho client: same GameSession/IrcConnection shape the chat core sees
		// once LoginService logs one-in — no TCP socket, IrcConnection defaults to the bancho bridge.
		var banchoPlayer = new GameSession(99, "bob", "bancho-token", UserPrivileges.Unrestricted,
			DateTimeOffset.UnixEpoch);
		gameRegistry.TryAdd(banchoPlayer);
		channelMembership.Join(banchoPlayer, channelRegistry.GetByName("#osu")!);

		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;

		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var acceptTask = Task.Run(async () =>
		{
			var client = await listener.AcceptTcpClientAsync(cts.Token);
			var connection = new TcpIrcConnection(client, authService, chatDispatch, channelMembership,
				ircQueries, channelRegistry, playerLogout, _fakeIrcOptions,
				NullLogger<TcpIrcConnection>.Instance, 1);
			_ = connection.RunAsync(cts.Token);
		}, cts.Token);

		using var aliceClient = new TcpClient();
		await aliceClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);
		await acceptTask;

		await using var aliceStream = aliceClient.GetStream();
		using var aliceReader = new StreamReader(aliceStream, Encoding.UTF8);
		await LoginAsync(aliceStream, aliceReader, "alice", "alice-key");
		banchoPlayer.Dequeue(); // drain ChannelJoin/ChannelInfo from both banchoPlayer's and alice's joins

		// IRC -> bancho: alice (real TCP client) PRIVMSGs the channel; the bancho session's Dequeue()
		// should hold the exact bancho SendMessage packet a real osu! client's next poll would drain.
		await WriteLineAsync(aliceStream, "PRIVMSG #osu :hello from irc");
		var banchoPacket = await WaitForNonEmptyDequeueAsync(banchoPlayer);
		Assert.Equal(ServerPacketWriter.SendMessage("alice", "hello from irc", "#osu", 1), banchoPacket);

		// bancho -> IRC: simulate SendPublicMessageHandler's own call into the chat core directly.
		await chatDispatch.SendPrivmsgAsync(banchoPlayer, "#osu", "hello from bancho", cts.Token);
		var received = await ReadUntilAsync(aliceReader, line => line.Contains("PRIVMSG"));
		Assert.Contains("hello from bancho", received);
		Assert.StartsWith(":bob!", received);

		listener.Stop();
	}

	private IrcQueryService MakeQueryService(ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry, IChannelRegistry channelRegistry,
		ChannelMembershipService channelMembership)
	{
		return new IrcQueryService(channelRegistry, gameRegistry, ircRegistry, channelMembership,
			new MotdService(new EmptySettingsRepository()), _fakeIrcOptions);
	}

	private static PlayerLogoutService MakePlayerLogoutService(ISessionRegistry<GameSession> gameRegistry,
		ISessionRegistry<IrcSession> ircRegistry, IChannelRegistry channelRegistry,
		ChannelMembershipService channelMembership)
	{
		var spectatorService = new SpectatorService(channelRegistry, channelMembership,
			NullLogger<SpectatorService>.Instance);
		var matchMembership = new MatchMembershipService(
			new InMemoryMatchRegistry(channelRegistry, new NotSupportedMatchRepository()), channelRegistry,
			gameRegistry, ircRegistry, channelMembership, new NotSupportedMatchRepository(),
			new NoOpMatchLiveEvents(), new NotSupportedBeatmapRepository(),
			new FakeUserRepository(), NullLogger<MatchMembershipService>.Instance);
		return new PlayerLogoutService(gameRegistry, ircRegistry, channelMembership, spectatorService, matchMembership,
			NullLogger<PlayerLogoutService>.Instance);
	}

	private static async Task<byte[]> WaitForNonEmptyDequeueAsync(GameSession session)
	{
		using var cts = new CancellationTokenSource(ReadTimeout);
		while (!cts.IsCancellationRequested)
		{
			var data = session.Dequeue();
			if (data.Length > 0) return data;

			await Task.Delay(10);
		}

		throw new TimeoutException("No packet arrived in time.");
	}

	private static async Task LoginAsync(NetworkStream stream, StreamReader reader, string nick, string password)
	{
		await WriteLineAsync(stream, $"PASS {password}");
		await WriteLineAsync(stream, $"NICK {nick}");
		await WriteLineAsync(stream, "USER guest 0 * :Real Name");

		await ReadUntilAsync(reader, line => line.Contains(" 001 "));
	}

	private static string HashPassword(IPasswordHasher hasher, string plaintext)
	{
		var md5Hex = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(plaintext)));
		return hasher.Hash(Encoding.UTF8.GetBytes(md5Hex));
	}

	private static async Task WriteLineAsync(NetworkStream stream, string line)
	{
		var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
		await stream.WriteAsync(bytes);
	}

	private static async Task<string> ReadUntilAsync(StreamReader reader, Func<string, bool> predicate)
	{
		using var cts = new CancellationTokenSource(ReadTimeout);
		while (true)
		{
			var line = await reader.ReadLineAsync(cts.Token);
			if (line is null) throw new IOException("Connection closed before the expected line arrived.");

			if (predicate(line)) return line;
		}
	}

	private sealed class FakeUserRepository : IUserRepository
	{
		private readonly Dictionary<string, User> _byName = new();
		private readonly Dictionary<int, string> _passwordHashes = new();

		public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_byName.Values.FirstOrDefault(u => u.Id == id));
		}

		public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_byName.GetValueOrDefault(User.MakeSafeName(name)));
		}

		public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_passwordHashes.GetValueOrDefault(id));
		}

		public Task UpdateCountryAsync(int id, Country country, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task UpdatePrivilegesAsync(int id, UserPrivileges privilege,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<User?> CreateAsync(string name, string pwBcrypt, Country country, UserPrivileges? privilege = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public void Add(User user, string? pwBcrypt = null)
		{
			_byName[User.MakeSafeName(user.Name)] = user;
			if (pwBcrypt is not null)
				_passwordHashes[user.Id] = pwBcrypt;
		}
	}

	/// <summary>No MOTD is ever configured in these tests, so every key reads back unset.</summary>
	private sealed class EmptySettingsRepository : ISettingsRepository
	{
		public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
		}

		public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Unused by this test's channel-PRIVMSG path — only DM delivery touches relationships.</summary>
	private sealed class NotSupportedRelationshipRepository : IRelationshipRepository
	{
		public Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Never recognises a command — this test's "hello bob" text has no `!` prefix anyway.</summary>
	private sealed class NullCommandDispatcher : ICommandDispatcher
	{
		public Task<bool> DispatchAsync(UserSession sender, string rawMessage, MatchSession? matchScope,
			string? channelName, ICommandReplySink sink, bool prefixOptional = false,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(false);
		}
	}

	/// <summary>Unused by this test — no match is ever created, just chat login/privmsg.</summary>
	private sealed class NotSupportedMatchRepository : IMatchRepository
	{
		public Task<int> CreateMatchAsync(string name, DateTime createdAt,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task SetMatchEndedAsync(int matchId, DateTime endedAt, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<int> CreateRoundAsync(int matchId, int roundIndex, string mapMd5, GameMode mode,
			MatchWinCondition winCondition, MatchTeamType teamType, Mods mods, DateTime startedAt,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task SetRoundEndedAsync(int roundId, DateTime endedAt, bool aborted,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Match?> FetchMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Round>> FetchRoundsAsync(int matchId, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Match>> FetchAllMatchesAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task DeleteMatchAsync(int matchId, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task CreateEventAsync(MatchEvent row, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<MatchEvent>> FetchEventsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Match>> FetchUnrecoveredMatchesAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Round>> FetchUnrecoveredRoundsAsync(int matchId,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>Unused by these tests — no beatmap lookup ever happens, just chat login/privmsg.</summary>
	private sealed class NotSupportedBeatmapRepository : IBeatmapRepository
	{
		public Task<Beatmap?> FetchOneAsync(int? id = null, string? md5 = null, string? filename = null,
			int? setId = null, bool includePrivate = false, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<Beatmap> UpsertAsync(Beatmap beatmap, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task DeleteByMd5Async(string md5, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<IReadOnlyList<Beatmap>>> SearchAsync(string? query, GameMode? mode, int offset,
			int amount, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<int> FetchMaxIdAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task UpdateDiffAsync(int id, double diff, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<IReadOnlyList<Beatmap>> FetchAllBySetIdAsync(int setId, bool includePrivate = false,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}

	/// <summary>No-op event bus — these tests never inspect the SSE live layer.</summary>
	private sealed class NoOpMatchLiveEvents : IMatchLiveEvents
	{
		public bool HasPlayerScoreSubscribers => false;

		public event Action<int, byte[]>? MainPublished;
		public event Action<int, string, byte[]>? PlayerScorePublished;
		public event Action<int, byte[]>? SettingsPublished;
		public event Action<int, int, byte[]>? SlotPublished;
		public event Action<int, byte[]>? HostPublished;
		public event Action<int, byte[]>? RefsPublished;
		public event Action<int, byte[]>? BansPublished;
		public event Action<int, byte[]>? TimerPublished;
		public event Action<int, byte[]>? SlotsPublished;
		public event Action<int, byte[]>? ChatPublished;

		public void PublishMain(int matchDbId, byte[] payload)
		{
			MainPublished?.Invoke(matchDbId, payload);
		}

		public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
		{
			PlayerScorePublished?.Invoke(matchDbId, playerName, payload);
		}

		public void PublishSettings(int matchDbId, byte[] payload)
		{
			SettingsPublished?.Invoke(matchDbId, payload);
		}

		public void PublishSlot(int matchDbId, int slotIndex, byte[] payload)
		{
			SlotPublished?.Invoke(matchDbId, slotIndex, payload);
		}

		public void PublishHost(int matchDbId, byte[] payload)
		{
			HostPublished?.Invoke(matchDbId, payload);
		}

		public void PublishRefs(int matchDbId, byte[] payload)
		{
			RefsPublished?.Invoke(matchDbId, payload);
		}

		public void PublishBans(int matchDbId, byte[] payload)
		{
			BansPublished?.Invoke(matchDbId, payload);
		}

		public void PublishTimer(int matchDbId, byte[] payload)
		{
			TimerPublished?.Invoke(matchDbId, payload);
		}

		public void PublishSlots(int matchDbId, byte[] payload)
		{
			SlotsPublished?.Invoke(matchDbId, payload);
		}

		public void PublishChat(int matchDbId, byte[] payload)
		{
			ChatPublished?.Invoke(matchDbId, payload);
		}
	}
}