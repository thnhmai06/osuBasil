using Basil.Host.Configuration;
using Basil.Host.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Host.Tests.Logging;

/// <summary>The default logging writes lifecycle events of Basil and nothing of the frameworks below Warning.</summary>
public sealed class LogLevelPolicyTests
{
	private static LogLevelPolicy Policy(Action<LoggingOptions>? change = null)
	{
		var services = new ServiceCollection();
		var options = services.AddOptions<LoggingOptions>();
		if (change is not null) options.Configure(change);
		return new LogLevelPolicy(services.BuildServiceProvider()
			.GetRequiredService<IOptionsMonitor<LoggingOptions>>());
	}

	[Fact]
	public void TheDefaultMinimumLevelIsInformation()
	{
		var policy = Policy();

		Assert.True(policy.Enabled("Basil.Host.Logging.LoginLog", LogLevel.Information));
		Assert.False(policy.Enabled("Basil.Host.Logging.LoginLog", LogLevel.Debug));
	}

	[Theory]
	[InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
	[InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
	[InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
	public void TheFrameworksDoNotWritePerRequestInformation(string category)
	{
		var policy = Policy();

		Assert.False(policy.Enabled(category, LogLevel.Information));
		Assert.True(policy.Enabled(category, LogLevel.Warning));
	}

	[Fact]
	public void TheLifetimeOfTheHostIsWrittenAtInformation()
	{
		Assert.True(Policy().Enabled("Microsoft.Hosting.Lifetime", LogLevel.Information));
	}

	[Fact]
	public void TheLongestMatchingOverrideWins()
	{
		var policy = Policy(options =>
		{
			options.Overrides["Basil.Host.Logging"] = LogLevel.Error;
			options.Overrides["Basil"] = LogLevel.Debug;
		});

		Assert.False(policy.Enabled("Basil.Host.Logging.LoginLog", LogLevel.Warning));
		Assert.True(policy.Enabled("Basil.Bot.Chat", LogLevel.Debug));
	}

	[Fact]
	public void AnOverrideNamesWholeSegmentsOnly()
	{
		var policy = Policy(options => options.Overrides["Basil.Host"] = LogLevel.Error);

		Assert.True(policy.Enabled("Basil.HostExtras.Thing", LogLevel.Information));
	}

	[Fact]
	public void NoneWritesNothing()
	{
		Assert.False(Policy(options => options.MinimumLevel = LogLevel.None).Enabled("Basil", LogLevel.Critical));
	}
}