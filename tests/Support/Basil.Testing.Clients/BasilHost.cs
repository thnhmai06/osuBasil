using System.Net;
using System.Text;
using Basil.Application.Auth.Persistence;
using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Common.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Beatmaps;
using Basil.Domain.Chat;
using Basil.Application.Users.Persistence;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Host;
using Basil.Host.Irc;
using Basil.Testing.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Testing.Clients;

/// <summary>
///     Runs the real server in the test process: Kestrel on a random port, a fake clock, a PostgreSQL database of its
///     own and temporary data directories. Clients talk to it over the network.
/// </summary>
public sealed class BasilHost : IAsyncDisposable
{
	/// <summary>The domain the clients address; sub-domains such as <c>c.</c> and <c>osu.</c> go in the Host header.</summary>
	public const string DomainName = "basil.test";

	private readonly PersistenceFixture _database;
	private readonly string _databaseName = "basil-host-" + Guid.NewGuid().ToString("N");
	private readonly string _root = Path.Combine(Path.GetTempPath(), "basil-host-" + Guid.NewGuid().ToString("N"));
	private readonly Dictionary<string, Guid> _keySessions = [];
	private WebApplication? _app;
	private bool _stopped;

	private BasilHost(PersistenceFixture database)
	{
		_database = database;
	}

	/// <summary>Gets the clock the server uses; tests advance it instead of sleeping.</summary>
	public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

	public Uri Url { get; private set; } = null!;

	public string Domain => DomainName;

	/// <summary>Gets the port the IRC host listens on.</summary>
	public int IrcPort => Services.GetRequiredService<IrcState>().Port;

	public IServiceProvider Services => _app!.Services;

	/// <summary>Starts a server with an empty database.</summary>
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
				["Basil:Database:ConnectionString"] = connection,
				["Basil:Storage:DataDirectory"] = Path.Combine(_root, "Data"),
				["Basil:Server:Domain"] = DomainName,
				["Basil:Server:Ports:Irc"] = "0",
				["Basil:Bot:Replies:Path"] = Path.Combine(_root, "Configurations", "Templates", "replies.json"),
				["Basil:Server:LanAdvertise"] = "false",
				["Basil:Updates:CheckOnStartup"] = "false",
				["Basil:Server:TrustedProxies:0"] = "127.0.0.1",
				["Basil:Server:TrustedProxies:1"] = "::1"
			});
			builder.WebHost.UseUrls("http://127.0.0.1:0");
			builder.Logging.ClearProviders();
			if (Environment.GetEnvironmentVariable("BASIL_TEST_LOG") is not null) builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning);
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
		Url = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
	}

	/// <summary>Waits until every event published so far has been delivered to its handlers.</summary>
	public Task DrainEventsAsync()
	{
		return Services.GetRequiredService<Basil.Application.Common.Events.EventQueue>().DrainAsync();
	}

	/// <summary>Stores an account with a password.</summary>
	public async Task<UserRecord> CreateUserAsync(string name, string password = "password",
		UserPermissions? permissions = null)
	{
		var user = await Services.GetRequiredService<IUserRepository>()
			.CreateAsync(new UserData { Name = name, Permissions = permissions ?? UserPermissionsGroups.Default });
		await Services.GetRequiredService<ICredentialRepository>()
			.CreateOrUpdateAsync(new Credentials(user, new Md5(Encoding.UTF8.GetBytes(password))));
		return user;
	}

	/// <summary>Issues an IRC key for an account, the way the account's owner would through the API.</summary>
	/// <param name="name">The username.</param>
	/// <param name="label">The label of the device.</param>
	/// <returns>The key, shown only once.</returns>
	public async Task<string> CreateIrcKeyAsync(string name, string label = "test-device")
	{
		var registry = Services.GetRequiredService<UserRegistry>();
		var attempt = new LoginAttempt(name, new Md5(Encoding.UTF8.GetBytes("password")));
		var login = await registry.LoginAsync(attempt, SessionType.Api, IPAddress.Loopback, null, 0);
		var api = login.Connection ?? throw new InvalidOperationException($"Login failed: {login.Failure}.");
		var (result, key, session) = await api.User.ScopeTo(api).Sessions.OpenIrcAsync(label);
		registry.Close(api, SessionEndReason.LoggedOut);
		if (!result.IsOk) throw new InvalidOperationException($"No key: {result.Outcome}.");
		_keySessions[key!] = session!.Id;
		return key!;
	}

	/// <summary>Revokes an IRC key, the way the account's owner would through the API.</summary>
	/// <param name="name">The username.</param>
	/// <param name="key">The key to revoke.</param>
	public async Task RevokeIrcKeyAsync(string name, string key)
	{
		var registry = Services.GetRequiredService<UserRegistry>();
		var attempt = new LoginAttempt(name, new Md5(Encoding.UTF8.GetBytes("password")));
		var api = (await registry.LoginAsync(attempt, SessionType.Api, IPAddress.Loopback, null, 0)).Connection!;
		await api.User.ScopeTo(api).Sessions.CloseAsync(_keySessions[key]);
		registry.Close(api, SessionEndReason.LoggedOut);
	}

	/// <summary>Stores an account with an IRC key and returns an IRC client for it, connected but not registered.</summary>
	/// <param name="name">The username.</param>
	/// <param name="permissions">The account's permissions, or the default ones.</param>
	/// <returns>The client and the key.</returns>
	public async Task<(IrcClient Client, string Key)> NewIrcClientAsync(string name,
		UserPermissions? permissions = null)
	{
		await CreateUserAsync(name, "password", permissions);
		var key = await CreateIrcKeyAsync(name);
		return (await IrcClient.ConnectAsync(this), key);
	}

	/// <summary>Stores an account and returns an osu! client for it, not yet logged in.</summary>
	public async Task<OsuClient> NewClientAsync(string name, string password = "password",
		UserPermissions? permissions = null, bool tourney = false)
	{
		await CreateUserAsync(name, password, permissions);
		return OsuClient.Create(this, name, password, tourney);
	}

	/// <summary>Opens a general channel that every login joins.</summary>
	public void OpenChannel(string name)
	{
		Services.GetRequiredService<Server>().OpenChannel(new GeneralChannelRecord
		{
			Name = name, Value = new GeneralChannelData { AutoJoin = true, Topic = "test" }
		});
	}

	/// <summary>Imports one of the sample beatmapsets of tests/Assets.</summary>
	/// <param name="sample">The path under Assets, for example <c>Sets/padoru-standard.osz</c>.</param>
	/// <returns>The imported beatmaps.</returns>
	public async Task<IReadOnlyList<BeatmapRecord>> ImportSampleAsync(string sample)
	{
		await using var archive = new MemoryStream(OszArchive.Sample(sample));
		var result = await Services.GetRequiredService<Beatmapsets>().ImportAsync(archive);
		if (result.Set is not null) return result.Beatmaps;
		var existing = await Services.GetRequiredService<IBeatmapRepository>().ListAsync(
			new BeatmapQuery(), new PageRequest(0, 1000));
		return existing.Items;
	}

	/// <summary>
	///     Stops the server the way a shutdown does, so that the graceful steps run. The clock of the server is fake, so it
	///     is advanced while the server stops; the steps' time limits then pass without waiting.
	/// </summary>
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
}
