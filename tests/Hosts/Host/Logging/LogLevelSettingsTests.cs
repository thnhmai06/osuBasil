using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Basil.Host.Tests.Logging;

/// <summary>The standard <c>Logging:LogLevel</c> settings decide which events are written, per source.</summary>
public sealed class LogLevelSettingsTests
{
	private static ILogger Logger(string category, Dictionary<string, string?> levels)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(
			levels.ToDictionary(pair => "Logging:LogLevel:" + pair.Key, pair => pair.Value)).Build();
		return LoggerFactory.Create(logging => logging.AddConfiguration(configuration.GetSection("Logging")))
			.CreateLogger(category);
	}

	private static readonly Dictionary<string, string?> Shipped = new()
	{
		["Default"] = "Information",
		["Microsoft"] = "Warning",
		["Microsoft.Hosting.Lifetime"] = "Information",
		["System"] = "Warning"
	};

	[Fact]
	public void TheDefaultLevelAppliesToASourceWithoutAnEntry()
	{
		var logger = Logger("Basil.Host.Logging.LoginLog", Shipped);

		Assert.True(logger.IsEnabled(LogLevel.Information));
		Assert.False(logger.IsEnabled(LogLevel.Debug));
	}

	[Theory]
	[InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
	[InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
	[InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
	public void TheFrameworksDoNotWritePerRequestInformation(string category)
	{
		var logger = Logger(category, Shipped);

		Assert.False(logger.IsEnabled(LogLevel.Information));
		Assert.True(logger.IsEnabled(LogLevel.Warning));
	}

	[Fact]
	public void TheLifetimeOfTheHostIsWrittenAtInformation()
	{
		Assert.True(Logger("Microsoft.Hosting.Lifetime", Shipped).IsEnabled(LogLevel.Information));
	}

	[Fact]
	public void TheLongestMatchingEntryWins()
	{
		var levels = new Dictionary<string, string?>
			{ ["Default"] = "Information", ["Basil"] = "Debug", ["Basil.Host.Logging"] = "Error" };

		Assert.False(Logger("Basil.Host.Logging.LoginLog", levels).IsEnabled(LogLevel.Warning));
		Assert.True(Logger("Basil.Bot.Chat", levels).IsEnabled(LogLevel.Debug));
	}

	[Fact]
	public void NoneWritesNothing()
	{
		Assert.False(Logger("Basil", new Dictionary<string, string?> { ["Default"] = "None" })
			.IsEnabled(LogLevel.Critical));
	}
}
