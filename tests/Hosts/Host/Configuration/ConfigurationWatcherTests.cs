using Basil.Host.Configuration;
using Basil.Host.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Basil.Host.Tests.Configuration;

/// <summary>
///     The configuration files are read again when they change: <c>Logging</c> and <c>Updates</c> apply at once, while
///     <c>Server</c>, <c>Database</c>, <c>Storage</c> and <c>Telemetry</c> are read when the server starts and wait for
///     the
///     next start.
/// </summary>
public sealed class ConfigurationWatcherTests
{
	private readonly MemoryConfigurationProvider _file;
	private readonly IConfigurationRoot _root;
	private readonly ServiceProvider _services;
	private readonly ConfigurationWatcher _watcher;

	public ConfigurationWatcherTests()
	{
		_root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Basil:Server:Domain"] = "first.test",
			["Basil:Server:Irc:Port"] = "6667",
			["Logging:LogLevel:Default"] = "Information",
			["Basil:Updates:CheckOnStartup"] = "true"
		}).Build();
		_file = _root.Providers.OfType<MemoryConfigurationProvider>().Single();
		var services = new ServiceCollection();
		ConfigurationSetup.AddAtStart<ServerOptions>(services, _root.GetSection("Basil:Server"));
		_services = services
			.Configure<UpdateOptions>(_root.GetSection("Basil:Updates"))
			.BuildServiceProvider();
		_watcher = new ConfigurationWatcher(_root, NullLogger<ConfigurationWatcher>.Instance);
		_watcher.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
	}

	private void Edit(string key, string value)
	{
		_file.Set(key, value);
		_root.Reload();
	}

	[Fact]
	public void ALoggingChangeAppliesAtOnce()
	{
		var logger = LoggerFactory.Create(logging => logging.AddConfiguration(_root.GetSection("Logging")))
			.CreateLogger("Basil.Host.Anything");
		Assert.False(logger.IsEnabled(LogLevel.Debug));

		Edit("Logging:LogLevel:Default", "Debug");

		Assert.True(logger.IsEnabled(LogLevel.Debug));
		Assert.Empty(_watcher.PendingRestart);
	}

	[Fact]
	public void AnUpdatesChangeAppliesAtOnce()
	{
		var monitor = _services.GetRequiredService<IOptionsMonitor<UpdateOptions>>();
		Assert.True(monitor.CurrentValue.CheckOnStartup);

		Edit("Basil:Updates:CheckOnStartup", "false");

		Assert.False(monitor.CurrentValue.CheckOnStartup);
		Assert.Empty(_watcher.PendingRestart);
	}

	[Fact]
	public void AServerChangeIsNotAppliedBeforeTheNextStartAndIsReportedAsPending()
	{
		Edit("Basil:Server:Irc:Port", "7000");

		Assert.Equal(6667, _services.GetRequiredService<IOptions<ServerOptions>>().Value.Irc.Port);
		Assert.Equal(["Basil:Server"], _watcher.PendingRestart);
	}

	[Fact]
	public void ChangingAServerValueBackClearsThePendingRestart()
	{
		Edit("Basil:Server:Irc:Port", "7000");

		Edit("Basil:Server:Irc:Port", "6667");

		Assert.Empty(_watcher.PendingRestart);
	}

	[Fact]
	public void ASectionThatWasAbsentAtStartAndIsAddedLaterWaitsForTheNextStart()
	{
		Edit("ConnectionStrings:Write", "Host=elsewhere");

		Assert.Equal(["ConnectionStrings"], _watcher.PendingRestart);
	}
}