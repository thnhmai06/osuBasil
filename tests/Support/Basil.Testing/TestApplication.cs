using System.Net;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Anticheat.Objects.Events;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Beatmaps.Ports;
using Basil.Application.Beatmaps.Services;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Chat.Persistence;
using Basil.Application.Chat.Registry;
using Basil.Application.Content.Storage;
using Basil.Application.Multiplayer.Objects.Events;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Objects.Events;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Services;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Settings;
using Basil.Application.Shared.Storage;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Game.Client;
using Basil.Domain.Game.Users;
using Basil.Domain.Moderation;
using Basil.Infrastructure.Anticheat;
using Basil.Infrastructure.Beatmaps;
using Basil.Infrastructure.Bot;
using Basil.Infrastructure.Diagnostics;
using Basil.Infrastructure.Scores;
using Basil.Infrastructure.Shared.Persistence;
using Basil.Infrastructure.Shared.Storage;
using Basil.Infrastructure.Users;
using Basil.Infrastructure.Users.Handlers;
using Basil.Shared.Structures.Cryptography;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Basil.Testing;

public sealed class RecordingPublisher<T> : IEventPublisher<T> where T : Event
{
	private readonly List<T> _events = [];

	public IReadOnlyList<T> Events => _events;

	public void Publish(T @event)
	{
		_events.Add(@event);
		Recorded?.Invoke(@event);
	}

	public event Action<T>? Recorded;
}

public sealed class TestApplication : IDisposable
{
	private static readonly DateTimeOffset Start =
		new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private static long _sessionIds = 1_000_000;
	private readonly InMemoryConnectionRepository _connections;

	private readonly InMemoryCredentialRepository _credentials;
	private readonly InMemoryModerationLogRepository _moderationLog;
	private readonly InMemorySessionRepository _sessions;
	private readonly InMemorySettingsRepository _settings;

	private readonly InMemoryUserRepository _users;

	public TestApplication(Action<IServiceCollection>? configure = null)
	{
		Clock = new FakeTimeProvider(Start);
		_users = new InMemoryUserRepository();
		_credentials = new InMemoryCredentialRepository();
		_sessions = new InMemorySessionRepository();
		_connections = new InMemoryConnectionRepository();
		var relationships = new InMemoryRelationshipRepository();
		_moderationLog = new InMemoryModerationLogRepository();
		_settings = new InMemorySettingsRepository();

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<TimeProvider>(Clock);
		RegisterStoragePorts(services);
		UploadsRoot = Path.Combine(Path.GetTempPath(), "basil-test-" + Guid.NewGuid().ToString("N"));
		services.AddSingleton<IUploadSpool>(new FileUploadSpool(
			new DataPaths(Options.Create(new StorageOptions { Location = UploadsRoot })), Clock));
		services.AddSingleton<IUserRepository>(_users);
		services.AddSingleton<ICredentialRepository>(_credentials);
		services.AddSingleton<ISessionRepository>(_sessions);
		services.AddSingleton<IConnectionRepository>(_connections);
		services.AddSingleton<IRelationshipRepository>(relationships);
		services.AddSingleton<IModerationLogRepository>(_moderationLog);
		services.AddSingleton<ISettingsRepository>(_settings);
		// The diagnostics handler of the shared infrastructure reads the database, which this fixture never connects to.
		var databaseProvider = Substitute.For<IDatabaseProvider>();
		databaseProvider.CreateSource(Arg.Any<string>(), Arg.Any<int>()).Returns(Substitute.For<IDatabaseSource>());
		services.AddSingleton(new Database(new DatabaseProviders(databaseProvider, databaseProvider),
			Options.Create(new DatabaseOptions { WriteConnectionString = "unused" })));
		var matchEvents = new InMemoryMatchEventRepository();
		services.AddSingleton<IMatchRepository>(new InMemoryMatchRepository(matchEvents));
		services.AddSingleton<IRoundRepository>(new InMemoryRoundRepository());
		services.AddSingleton<IMatchEventRepository>(matchEvents);
		services.AddSingleton<IRulesetUpdater, NoRulesets>();
		services.AddSingleton<IBeatmapReloader, NoReload>();
		services.AddSingleton<IScorePerformanceRecalculator, NoRecalculation>();
		configure?.Invoke(services);
		services.AddUsersLogic();
		services.AddScoresLogic();
		services.AddBeatmapsLogic();
		services.AddAnticheatInfrastructure();
		services.AddDiagnosticsInfrastructure();
		services.AddBasilApplication();
		// The host registers the bot's handlers with the others, so a container built like the host needs the bot's services too.
		services.AddBotInfrastructure();
		services.AddSingleton<IEventPublisher<UserEvent>>(UserEvents);
		services.AddSingleton<IEventPublisher<RoomEvent>>(RoomEvents);
		services.AddSingleton<IEventPublisher<LobbyEvent>>(LobbyEvents);
		services.AddSingleton<IEventPublisher<ChannelEvent>>(ChannelEvents);
		services.AddSingleton<IEventPublisher<BeatmapsetEvent>>(BeatmapsetEvents);
		services.AddSingleton<IEventPublisher<ScoreEvent>>(ScoreEvents);
		services.AddSingleton<IEventPublisher<AnticheatEvent>>(AnticheatEvents);

		Provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		// The host delivers events to the handlers; this fixture runs the one that stores a closed session.
		var sessionEnd = new SessionEndHandler(_sessions, NullLogger<SessionEndHandler>.Instance);
		var connectionClosed = new ConnectionClosedHandler(_connections, Clock,
			NullLogger<ConnectionClosedHandler>.Instance);
		UserEvents.Recorded += @event =>
		{
			if (@event is not UserConnectionClosed closed) return;

			_ = sessionEnd.Handle(closed, CancellationToken.None);
			_ = connectionClosed.Handle(closed, CancellationToken.None);
		};
		Users = Provider.GetRequiredService<UserRegistry>();
		Lobby = Provider.GetRequiredService<Lobby>();
		Channels = Provider.GetRequiredService<GeneralChannelRegistry>();
		RoomChannels = Provider.GetRequiredService<RoomChannels>();
		PmChannels = Provider.GetRequiredService<PmChannels>();
		Server = Provider.GetRequiredService<Server>();
		Beatmapsets = Provider.GetRequiredService<Beatmapsets>();
		var forwarding = new ServiceCollection().AddLogging().AddSingleton<EventQueue>();
		foreach (var descriptor in services.Where(d => !d.ServiceType.IsGenericTypeDefinition && !d.IsKeyedService))
		{
			var type = descriptor.ServiceType;
			forwarding.AddSingleton(type, _ => Provider.GetRequiredService(type));
		}

		Sender = forwarding.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Singleton)
			.BuildServiceProvider().GetRequiredService<ISender>();
	}

	public ServiceProvider Provider { get; }

	public FakeTimeProvider Clock { get; }

	public UserRegistry Users { get; }

	public Lobby Lobby { get; }

	public ISender Sender { get; }

	public GeneralChannelRegistry Channels { get; }

	public RoomChannels RoomChannels { get; }

	public PmChannels PmChannels { get; }

	public Server Server { get; }

	public Beatmapsets Beatmapsets { get; }

	public RecordingPublisher<UserEvent> UserEvents { get; } = new();

	public RecordingPublisher<RoomEvent> RoomEvents { get; } = new();

	public RecordingPublisher<LobbyEvent> LobbyEvents { get; } = new();

	public RecordingPublisher<ChannelEvent> ChannelEvents { get; } = new();

	public RecordingPublisher<BeatmapsetEvent> BeatmapsetEvents { get; } = new();

	public RecordingPublisher<ScoreEvent> ScoreEvents { get; } = new();

	public RecordingPublisher<AnticheatEvent> AnticheatEvents { get; } = new();

	public IReadOnlyList<SessionRecord> StoredSessions => _sessions.Entries;

	public IReadOnlyList<ConnectionRecord> StoredConnections => _connections.Entries;

	public Exception? SessionRecordFailure
	{
		get => _sessions.RecordFailure;
		set => _sessions.RecordFailure = value;
	}

	public Exception? UserUpdateFailure
	{
		get => _users.UpdateFailure;
		set => _users.UpdateFailure = value;
	}

	public Exception? ModerationLogFailure
	{
		get => _moderationLog.AppendFailure;
		set => _moderationLog.AppendFailure = value;
	}

	public IReadOnlyList<ModerationData> ModerationEntries => _moderationLog.Entries;

	public string UploadsRoot { get; }

	public void Dispose()
	{
		Provider.Dispose();
		if (Directory.Exists(UploadsRoot)) Directory.Delete(UploadsRoot, true);
	}

	public void ForgetStoredSession(long id)
	{
		_sessions.Forget(id);
	}

	public DateTimeOffset? TokenExpiryOf(long id)
	{
		return _sessions.ExpiryOf(id);
	}

	public async Task<UserRecord> CreateUserAsync(string name, string password,
		UserPrivileges? privileges = null)
	{
		var data = new UserData { Name = name, Privileges = privileges ?? UserPrivilegesExtensions.Default };
		var user = await _users.CreateAsync(data);
		await _credentials.CreateOrUpdateAsync(new Credentials(user, PasswordHash(password)));
		return user;
	}

	public async Task<UserRecord> SeedAccountAsync(int id, string name, string password,
		UserPrivileges? privileges = null, DateTimeOffset? deletedAt = null)
	{
		var user = _users.Seed(new UserData
		{
			Name = name,
			Privileges = privileges ?? UserPrivilegesExtensions.Default,
			DeletedAt = deletedAt
		}, id);
		await _credentials.CreateOrUpdateAsync(new Credentials(user, PasswordHash(password)));
		return user;
	}

	public Task<bool> VerifiesPasswordAsync(UserRecord user, string password)
	{
		return _credentials.VerifyAsync(new Credentials(user, PasswordHash(password)));
	}

	public async Task<UserRecord> SeedBotAsync()
	{
		var bot = _users.Seed(new UserData { Name = "BasilBot", Privileges = UserPrivileges.None }, BasilBot.Id);
		await _credentials.CreateOrUpdateAsync(new Credentials(bot, PasswordHash("basil-bot")));
		return bot;
	}

	public static OsuClient CreateClient()
	{
		var hash = new Md5Hash("0123456789abcdef0123456789abcdef");
		return new OsuClient(
			new OsuVersion(new DateOnly(2026, 1, 1), null, OsuVersionStream.Stable),
			new OsuFingerprint(hash, hash,
				hash, new NetworkAdapters { Adapters = "runningunderwine", Hash = hash }));
	}

	public Task<LoginResult> AttemptLoginAsync(string username, string password,
		SessionType type = SessionType.Api)
	{
		if (type is SessionType.Irc) return OpenIrcAsync(username);

		var client = type is SessionType.Stable or SessionType.StableTourney ? CreateClient() : null;
		return Users.LoginAsync(new LoginAttempt(username, PasswordHash(password)), type, IPAddress.Loopback,
			client, 0);
	}

	public async Task<Connection> LoginAsync(UserRecord user, SessionType type = SessionType.Api,
		string password = "password")
	{
		var result = await AttemptLoginAsync(user.Value.Name, password, type);
		return result.Connection ?? throw new InvalidOperationException($"Login failed: {result.Failure}.");
	}

	public void Silence(UserRecord user, DateTimeOffset until)
	{
		user.Value.SilencedUntil = until;
		_users.UpdateAsync(user).GetAwaiter().GetResult();
	}

	public Task SetSettingsAsync(ServerSettings settings)
	{
		return _settings.CreateOrUpdateAsync(settings);
	}

	private async Task<LoginResult> OpenIrcAsync(string username)
	{
		if (await _users.GetByNameAsync(username) is not { } record) return LoginResult.Fail(LoginFailure.UnknownUser);

		var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
		var now = Clock.GetUtcNow();
		await _sessions.CreateOrUpdateAsync(new SessionRecord
		{
			Id = Interlocked.Increment(ref _sessionIds),
			Value = new SessionData(record, SessionType.Irc, now, new SessionFingerprint(IPAddress.Loopback),
				new Sha256Hash(Encoding.UTF8.GetBytes(key)), null, now)
		});
		return await Users.LoginWithTokenAsync(SessionType.Irc, key, IPAddress.Loopback);
	}

	private static Md5Hash PasswordHash(string password)
	{
		return new Md5Hash(Encoding.UTF8.GetBytes(password));
	}

	private static void RegisterStoragePorts(IServiceCollection services)
	{
		var inMemoryPortTypes = new HashSet<Type>
		{
			typeof(IUserRepository), typeof(ICredentialRepository),
			typeof(ISessionRepository), typeof(IConnectionRepository), typeof(IRelationshipRepository),
			typeof(IModerationLogRepository),
			typeof(ISettingsRepository)
		};
		var portAssemblies = new[]
		{
			typeof(IUserRepository).Assembly, typeof(IBeatmapsetReader).Assembly,
			typeof(ICredentialRepository).Assembly,
			typeof(ISettingsRepository).Assembly, typeof(IFaqStorage).Assembly, typeof(IScoreRepository).Assembly,
			typeof(IMatchRepository).Assembly, typeof(IChannelRepository).Assembly
		}.Distinct();
		foreach (var port in portAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(type =>
			         type is { IsInterface: true, ContainsGenericParameters: false, Namespace: not null } &&
			         (type.Namespace.EndsWith(".Persistence", StringComparison.Ordinal) ||
			          type.Namespace.EndsWith(".Storage", StringComparison.Ordinal) ||
			          type.Namespace.EndsWith(".Ports", StringComparison.Ordinal)) &&
			         !inMemoryPortTypes.Contains(type)))
			services.AddSingleton(port, _ => Substitute.For([port], []));
	}

	private sealed class NoRulesets : IRulesetUpdater
	{
		public RulesetVersions Version()
		{
			return new RulesetVersions("test", null);
		}

		public Task<RulesetCheck> CheckAsync(CancellationToken ct = default)
		{
			return Task.FromResult(new RulesetCheck(Version(), "test"));
		}

		public Task<RulesetStageResult> StageAsync(CancellationToken ct = default)
		{
			return Task.FromResult(new RulesetStageResult("test", false));
		}
	}

	private sealed class NoReload : IBeatmapReloader
	{
		public bool Running => false;

		public bool TryStart()
		{
			return true;
		}
	}

	private sealed class NoRecalculation : IScorePerformanceRecalculator
	{
		public bool Running => false;

		public bool TryStart(ScorePerformanceScope scope)
		{
			return true;
		}
	}
}