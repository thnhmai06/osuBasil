using System.Net;
using Basil.Application.Capabilities.Beatmaps;
using System.Text;
using Basil.Application.Runtime;
using Basil.Application.Runtime.Anticheat;
using Basil.Application.Runtime.Beatmaps;
using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Multiplayer.Events;
using Basil.Application.Runtime.Scores;
using Basil.Application.Runtime.Servers;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Application.Persistence.Auth;
using Basil.Application.Persistence.Content;
using Basil.Application.Persistence.Multiplayer;
using Basil.Application.Persistence.Users;
using Basil.Domain;
using Basil.Domain.Auth;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Basil.Testing;

/// <summary>Collects events of one category without changing their observable values.</summary>
/// <typeparam name="T">The event category.</typeparam>
public sealed class RecordingPublisher<T> : IEventPublisher<T> where T : Event
{
	private readonly List<T> _events = [];

	/// <summary>Raised after an event has been recorded.</summary>
	public event Action<T>? Recorded;

	/// <summary>Gets every event published through this recorder, in order.</summary>
	public IReadOnlyList<T> Events => _events;

	/// <summary>Records an event.</summary>
	/// <param name="event">The event to record.</param>
	public void Publish(T @event)
	{
		_events.Add(@event);
		Recorded?.Invoke(@event);
	}
}

/// <summary>Builds the real Application.Objects composition with deterministic clocks and test storage.</summary>
public sealed class TestApplication : IDisposable
{
	private static readonly DateTimeOffset Start =
		new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Gets the service provider for the composed application and its test ports.</summary>
	public ServiceProvider Provider { get; }

	/// <summary>Gets the controllable UTC clock shared by application objects.</summary>
	public FakeTimeProvider Clock { get; }

	/// <summary>Gets the online users and their connections.</summary>
	public UserRegistry Users { get; }

	/// <summary>Gets the multiplayer lobby.</summary>
	public Lobby Lobby { get; }

	/// <summary>Gets the configured general chat channels.</summary>
	public GeneralChannelRegistry Channels { get; }

	/// <summary>Gets the server root object.</summary>
	public Server Server { get; }

	/// <summary>Gets the beatmapset root object.</summary>
	public Beatmapsets Beatmapsets { get; }

	/// <summary>Gets the events published about users and connections.</summary>
	public RecordingPublisher<UserEvent> UserEvents { get; } = new();

	/// <summary>Gets the events published about rooms.</summary>
	public RecordingPublisher<RoomEvent> RoomEvents { get; } = new();

	/// <summary>Gets the events published about the lobby.</summary>
	public RecordingPublisher<LobbyEvent> LobbyEvents { get; } = new();

	/// <summary>Gets the events published about chat channels.</summary>
	public RecordingPublisher<ChannelEvent> ChannelEvents { get; } = new();

	/// <summary>Gets the events published about beatmapsets.</summary>
	public RecordingPublisher<BeatmapsetEvent> BeatmapsetEvents { get; } = new();

	/// <summary>Gets the events published about scores.</summary>
	public RecordingPublisher<ScoreEvent> ScoreEvents { get; } = new();

	/// <summary>Gets the events published about anticheat reports.</summary>
	public RecordingPublisher<AnticheatEvent> AnticheatEvents { get; } = new();

	/// <summary>Gets the login history written by the application.</summary>
	public IReadOnlyList<LoginEntry> LoginEntries => _logins.Entries;

	/// <summary>Gets the moderation history written by the application.</summary>
	public IReadOnlyList<ModerationData> ModerationEntries => _moderationLog.Entries;

	private readonly InMemoryUserRepository _users;
	private readonly InMemoryCredentialRepository _credentials;
	private readonly InMemoryRestrictionRepository _restrictions;
	private readonly InMemoryLoginRepository _logins;
	private readonly InMemoryModerationLogRepository _moderationLog;
	private readonly InMemorySettingsRepository _settings;

	/// <summary>Creates the application and resolves its public roots.</summary>
	/// <param name="configure">Replaces ports of the application, such as storage, before it is composed; or <see langword="null" />.</param>
	public TestApplication(Action<IServiceCollection>? configure = null)
	{
		Clock = new FakeTimeProvider(Start);
		_users = new InMemoryUserRepository();
		_credentials = new InMemoryCredentialRepository();
		_restrictions = new InMemoryRestrictionRepository();
		_logins = new InMemoryLoginRepository();
		var relationships = new InMemoryRelationshipRepository();
		_moderationLog = new InMemoryModerationLogRepository();
		_settings = new InMemorySettingsRepository();

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<TimeProvider>(Clock);
		RegisterStoragePorts(services);
		services.AddSingleton<IUserRepository>(_users);
		services.AddSingleton<ICredentialRepository>(_credentials);
		services.AddSingleton<IRestrictionRepository>(_restrictions);
		services.AddSingleton<ILoginRepository>(_logins);
		services.AddSingleton<IRelationshipRepository>(relationships);
		services.AddSingleton<IModerationLogRepository>(_moderationLog);
		services.AddSingleton<ISettingsRepository>(_settings);
		var matchEvents = new InMemoryMatchEventRepository();
		services.AddSingleton<IMatchRepository>(new InMemoryMatchRepository(matchEvents));
		services.AddSingleton<IRoundRepository>(new InMemoryRoundRepository());
		services.AddSingleton<IMatchEventRepository>(matchEvents);
		services.AddSingleton<IEventPublisher<UserEvent>>(UserEvents);
		services.AddSingleton<IEventPublisher<RoomEvent>>(RoomEvents);
		services.AddSingleton<IEventPublisher<LobbyEvent>>(LobbyEvents);
		services.AddSingleton<IEventPublisher<ChannelEvent>>(ChannelEvents);
		services.AddSingleton<IEventPublisher<BeatmapsetEvent>>(BeatmapsetEvents);
		services.AddSingleton<IEventPublisher<ScoreEvent>>(ScoreEvents);
		services.AddSingleton<IEventPublisher<AnticheatEvent>>(AnticheatEvents);
		configure?.Invoke(services);
		services.AddApplication();

		Provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		Users = Provider.GetRequiredService<UserRegistry>();
		Lobby = Provider.GetRequiredService<Lobby>();
		Channels = Provider.GetRequiredService<GeneralChannelRegistry>();
		Server = Provider.GetRequiredService<Server>();
		Beatmapsets = Provider.GetRequiredService<Beatmapsets>();
	}

	/// <summary>Creates and stores an account and its password.</summary>
	/// <param name="name">The account name.</param>
	/// <param name="password">The deterministic password used by login attempts.</param>
	/// <param name="permissions">The permissions to grant, or the normal new-account permissions.</param>
	/// <returns>The stored account.</returns>
	public async Task<UserRecord> CreateUserAsync(string name, string password,
		UserPermissions? permissions = null)
	{
		var data = new UserData { Name = name, Permissions = permissions ?? UserPermissionsGroups.Default };
		var user = await _users.CreateAsync(data);
		await _credentials.CreateOrUpdateAsync(new Credentials(user, PasswordHash(password)));
		return user;
	}

	/// <summary>Stores an account under a chosen id, with its password.</summary>
	/// <param name="id">The id of the account.</param>
	/// <param name="name">The account name.</param>
	/// <param name="password">The account password.</param>
	/// <param name="permissions">The permissions to grant, or the normal new-account permissions.</param>
	/// <param name="deletedAt">When the account was deleted, or <see langword="null" /> for a live account.</param>
	/// <returns>The stored account.</returns>
	public async Task<UserRecord> SeedAccountAsync(int id, string name, string password,
		UserPermissions? permissions = null, DateTimeOffset? deletedAt = null)
	{
		var user = _users.Seed(new UserData
		{
			Name = name,
			Permissions = permissions ?? UserPermissionsGroups.Default,
			DeletedAt = deletedAt
		}, id);
		await _credentials.CreateOrUpdateAsync(new Credentials(user, PasswordHash(password)));
		return user;
	}

	/// <summary>Determines whether the stored credential of an account accepts a password.</summary>
	/// <param name="user">The account.</param>
	/// <param name="password">The password to check.</param>
	/// <returns><see langword="true" /> when the password verifies for the account.</returns>
	public Task<bool> VerifiesPasswordAsync(UserRecord user, string password)
	{
		return _credentials.VerifyAsync(new Credentials(user, PasswordHash(password)));
	}

	/// <summary>Seeds the fixed BasilBot account for tests that exercise its server-owned connection.</summary>
	/// <returns>The stored BasilBot account.</returns>
	public async Task<UserRecord> SeedBotAsync()
	{
		var bot = _users.Seed(new UserData { Name = "BasilBot", Permissions = UserPermissions.None }, BasilBot.Id);
		await _credentials.CreateOrUpdateAsync(new Credentials(bot, PasswordHash("basil-bot")));
		return bot;
	}

	/// <summary>Creates a valid osu! client descriptor for Bancho and Tourney logins.</summary>
	/// <returns>A stable client descriptor with valid fingerprint values.</returns>
	public static BanchoClient CreateClient()
	{
		var hash = new Md5("0123456789abcdef0123456789abcdef");
		return new BanchoClient(
			new BanchoVersion(new DateOnly(2026, 1, 1), null, BanchoVersionStream.Stable),
			new BanchoFingerprint(hash, new NetworkAdapters { Adapters = "runningunderwine", Hash = hash }, hash,
				hash));
	}

	/// <summary>Attempts login using a stored account name and deterministic password hash.</summary>
	/// <param name="username">The claimed account name.</param>
	/// <param name="password">The claimed password.</param>
	/// <param name="type">The requested connection kind.</param>
	/// <returns>The public login result.</returns>
	public Task<LoginResult> AttemptLoginAsync(string username, string password,
		ConnectionType type = ConnectionType.Api)
	{
		var client = type is ConnectionType.Bancho or ConnectionType.Tourney ? CreateClient() : null;
		return Users.LoginAsync(new LoginAttempt(username, PasswordHash(password)), type, IPAddress.Loopback,
			client, 0);
	}

	/// <summary>Logs in a stored account and returns its open connection.</summary>
	/// <param name="user">The account to log in.</param>
	/// <param name="type">The requested connection kind.</param>
	/// <param name="password">The account password.</param>
	/// <returns>The opened connection.</returns>
	/// <exception cref="InvalidOperationException">The login did not succeed.</exception>
	public async Task<Connection> LoginAsync(UserRecord user, ConnectionType type = ConnectionType.Api,
		string password = "password")
	{
		var result = await AttemptLoginAsync(user.Value.Name, password, type);
		return result.Connection ?? throw new InvalidOperationException($"Login failed: {result.Failure}.");
	}

	/// <summary>Adds a previously stored restriction to the fixture's repository.</summary>
	/// <param name="user">The restricted account.</param>
	/// <param name="permissions">The permissions to suspend.</param>
	/// <param name="endsAt">When it ends, or <see langword="null" /> for an open-ended restriction.</param>
	/// <param name="startsAt">When it starts, or the fixture's current time minus one second.</param>
	/// <returns>The stored restriction.</returns>
	public UserRestriction AddRestriction(UserRecord user, UserPermissions permissions, DateTimeOffset? endsAt = null,
		DateTimeOffset? startsAt = null)
	{
		return _restrictions.Seed(user, permissions, startsAt ?? Clock.GetUtcNow().AddSeconds(-1), endsAt);
	}

	/// <summary>Stores the server settings the application reads.</summary>
	/// <param name="settings">The settings to store.</param>
	public Task SetSettingsAsync(ServerSettings settings)
	{
		return _settings.CreateOrUpdateAsync(settings);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		Provider.Dispose();
	}

	private static Md5 PasswordHash(string password)
	{
		return new Md5(Encoding.UTF8.GetBytes(password));
	}

	private static void RegisterStoragePorts(IServiceCollection services)
	{
		var inMemoryPortTypes = new HashSet<Type>
			{
				typeof(IUserRepository), typeof(ICredentialRepository), typeof(IRestrictionRepository),
				typeof(ILoginRepository), typeof(IRelationshipRepository), typeof(IModerationLogRepository),
				typeof(ISettingsRepository)
			};
		var portAssemblies = new[] { typeof(IUserRepository).Assembly, typeof(IBeatmapsetReader).Assembly };
		foreach (var port in portAssemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => type.IsInterface &&
																	  !type.ContainsGenericParameters &&
																	  !inMemoryPortTypes.Contains(type)))
			services.AddSingleton(port, _ => Substitute.For([port], Array.Empty<object>()));
	}
}