using Basil.Host.Cli;
using Basil.Host.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Tests.Logging;

/// <summary>The server's logger follows the <c>Logging:LogLevel</c> settings, and follows them again when they change.</summary>
public sealed class ServerLoggingTests : IDisposable
{
	private readonly WebApplication _app;
	private readonly MemoryConfigurationProvider _settings;

	public ServerLoggingTests()
	{
		var builder = WebApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Logging:LogLevel:Default"] = "Information",
			["Logging:LogLevel:Microsoft"] = "Warning",
			["Logging:File:Location"] =
				Path.Combine(Path.GetTempPath(), "basil-logging-test-" + Guid.NewGuid().ToString("N"))
		});
		_settings = ((IConfigurationRoot)builder.Configuration).Providers.OfType<MemoryConfigurationProvider>().Last();
		ServerLogging.Add(builder, CliMode.Full);
		_app = builder.Build();
	}

	public void Dispose()
	{
		_app.DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	private ILogger Logger(string category)
	{
		return _app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
	}

	[Fact]
	public void TheDefaultLevelAppliesToASourceWithoutAnEntry()
	{
		var logger = Logger("Basil.Host.Logging.LoginLog");

		Assert.True(logger.IsEnabled(LogLevel.Information));
		Assert.False(logger.IsEnabled(LogLevel.Debug));
	}

	[Fact]
	public void ASourceWithAnEntryUsesItsOwnLevel()
	{
		var logger = Logger("Microsoft.AspNetCore.Hosting.Diagnostics");

		Assert.False(logger.IsEnabled(LogLevel.Information));
		Assert.True(logger.IsEnabled(LogLevel.Warning));
	}

	[Fact]
	public void AChangeToTheLevelsAppliesToLoggersThatAlreadyExist()
	{
		var logger = Logger("Basil.Host.Logging.LoginLog");
		Assert.False(logger.IsEnabled(LogLevel.Debug));

		_settings.Set("Logging:LogLevel:Default", "Debug");
		((IConfigurationRoot)_app.Configuration).Reload();

		Assert.True(logger.IsEnabled(LogLevel.Debug));
	}
}