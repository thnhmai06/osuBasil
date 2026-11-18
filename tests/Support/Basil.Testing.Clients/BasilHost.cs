using System.Net;
using System.Text;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Events;
using Basil.Application.Shared.Paging;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Chat;
using Basil.Domain.Game.Users;
using Basil.Host.Irc.Session;
using Basil.Lifetime;
using Basil.Testing.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Testing.Clients;

public sealed class BasilHost : IAsyncDisposable
{
	public const string DomainName = "basil.test";

	private readonly PersistenceFixture _database;
	private readonly string _databaseName = "basil-host-" + Guid.NewGuid().ToString("N");
	private readonly Dictionary<string, long> _keySessions = [];
	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-host-" + Guid.NewGuid().ToString("N"));
	private WebApplication? _app;
	private bool _stopped;

	private BasilHost(PersistenceFixture database)
	{
		_database = database;
	}

	public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

	public Uri Url { get; private set; } = null!;

	public string Domain => DomainName;

	public int IrcPort => Services.GetRequiredService<IrcState>().Port;

	public IServiceProvider Services => _app!.Services;

	public async ValueTask DisposeAsync()
	{
		if (_app is not null)
		{
			await StopAsync();
			await _app.DisposeAsync();
		}

		await _database.DropDatabaseAsync(_databaseName);
		if (Directory.Exists(_root)) Directory.Delete(_root, true);
	}

	public static async Task<BasilHost> StartAsync(PersistenceFixture database)
	{
		var host = new BasilHost(database);
		await host.StartCoreAsync();
		return host;
	}

	private async Task StartCoreAsync()
	{
		var connection = await _database.CreateDatabaseAsync(_databaseName);
		Directory.CreateDirectory(_root);
		_app = HostApplication.Create([], builder =>
		{
			builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["ConnectionStrings:Write"] = connection,
				["ConnectionStrings:Read"] = "",
				["Basil:Database:Write:Provider"] = TestServer.ProviderName,
				["Basil:Storage:Location"] = Path.Combine(_root, "Data"),
				["Basil:Server:Domain"] = DomainName,
				["Basil:Server:Irc:Port"] = "0",
				// The shipped settings listen with TLS on port 443; a test listens on any free port, without TLS.
				["Kestrel:Endpoints:Https:Url"] = "http://127.0.0.1:0",
				["Kestrel:Certificates:Default:Path"] = "",
				["Basil:Bot:Replies:Path"] = Path.Combine(_root, "Configurations", "Templates", "replies.json"),
				["Basil:Server:Multicast:Enabled"] = "false",
				["Basil:Updates:CheckOnStartup"] = "false",
				["Basil:Server:TrustedProxies:0"] = "127.0.0.1",
				["Basil:Server:TrustedProxies:1"] = "::1"
			});
			builder.Logging.ClearProviders();
			if (Environment.GetEnvironmentVariable("BASIL_TEST_LOG") is not null)
				builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning);
			builder.Services.AddSingleton<TimeProvider>(Time);
		});
		await _app.StartAsync();
		// Every test client connects from the loopback address; the per-address limit is tested on its own.
		var settings = _app.Services.GetRequiredService<ISettingsRepository>();
		var current = await settings.GetAsync();
		await settings.CreateOrUpdateAsync(current with
		{
			RateLimits = current.RateLimits with
			{
				Connections = new ServerSettings.ConnectionLimitSettings { PerIp = 10_000 }
			}
		});
		Url = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses
			.First());
	}

	public Task DrainEventsAsync()
	{
		return Services.GetRequiredService<EventQueue>().DrainAsync();
	}

	public async Task<UserRecord> CreateUserAsync(string name, string password = "password",
		UserPrivileges? privileges = null)
	{
		var user = await Services.GetRequiredService<IUserRepository>()
			.CreateAsync(new UserData { Name = name, Privileges = privileges ?? UserPrivilegesExtensions.Default });
		await Services.GetRequiredService<ICredentialRepository>()
			.CreateOrUpdateAsync(new Credentials(user, new Md5(Encoding.UTF8.GetBytes(password))));
		return user;
	}

	public async Task<string> CreateIrcKeyAsync(string name, string label = "test-device")
	{
		var registry = Services.GetRequiredService<UserRegistry>();
		var attempt = new LoginAttempt(name, new Md5(Encoding.UTF8.GetBytes("password")));
		var login = await registry.LoginAsync(attempt, SessionType.Api, IPAddress.Loopback, null, 0);
		var api = login.Connection ?? throw new InvalidOperationException($"Login failed: {login.Failure}.");
		var (result, key, session) = await api.User.ScopeTo(api).Sessions.OpenIrcAsync(label);
		registry.Close(api, SessionEndReason.LoggedOut);
		if (result != UserOutcome.Ok) throw new InvalidOperationException($"No key: {result}.");
		_keySessions[key!] = session!.Id;
		return key!;
	}

	public async Task RevokeIrcKeyAsync(string name, string key)
	{
		var registry = Services.GetRequiredService<UserRegistry>();
		var attempt = new LoginAttempt(name, new Md5(Encoding.UTF8.GetBytes("password")));
		var api = (await registry.LoginAsync(attempt, SessionType.Api, IPAddress.Loopback, null, 0)).Connection!;
		await api.User.ScopeTo(api).Sessions.CloseAsync(_keySessions[key]);
		registry.Close(api, SessionEndReason.LoggedOut);
	}

	public async Task<(IrcClient Client, string Key)> NewIrcClientAsync(string name,
		UserPrivileges? privileges = null)
	{
		await CreateUserAsync(name, "password", privileges);
		var key = await CreateIrcKeyAsync(name);
		return (await IrcClient.ConnectAsync(this), key);
	}

	public async Task<OsuClient> NewClientAsync(string name, string password = "password",
		UserPrivileges? privileges = null, bool tourney = false)
	{
		await CreateUserAsync(name, password, privileges);
		return OsuClient.Create(this, name, password, tourney);
	}

	public void OpenChannel(string name)
	{
		Services.GetRequiredService<Server>().OpenChannel(new GeneralChannelRecord
		{
			Name = name, Value = new GeneralChannelData { AutoJoin = true, Topic = "test" }
		});
	}

	public async Task<IReadOnlyList<BeatmapRecord>> ImportSampleAsync(string sample)
	{
		await using var archive = new MemoryStream(OszArchive.Sample(sample));
		var result = await Services.GetRequiredService<Beatmapsets>().ImportAsync(archive);
		if (result.Set is not null) return result.Beatmaps;
		var existing = await Services.GetRequiredService<IBeatmapRepository>().ListAsync(
			new BeatmapQuery(), new PageRequest(0, 1000));
		return existing.Items;
	}

	public async Task StopAsync()
	{
		if (_app is null || _stopped) return;

		_stopped = true;
		var stopping = _app.StopAsync();
		while (!stopping.IsCompleted)
		{
			Time.Advance(TimeSpan.FromSeconds(1));
			await Task.WhenAny(stopping, Task.Delay(5));
		}

		await stopping;
	}
}