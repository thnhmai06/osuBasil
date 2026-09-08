using Basil.Server.Host;
using Basil.Server.Shared.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.Json;

namespace Basil.Server.Tests.Host;

/// <summary>
///     Application settings come from Data/appsettings.json, and nothing else may quietly
///     override them.
/// </summary>
/// <remarks>
///     <see cref="WebApplication.CreateBuilder(string[])" /> inherits a dozen configuration sources
///     of its own (environment variables, host-config probes for <c>appsettings.json</c> and
///     <c>testhost.settings.json</c>, memory sources, a chained host-config source). Before this was
///     fixed, an environment variable happened to lose to <c>Data/appsettings.json</c> only because
///     that file was added <em>after</em> the inherited list -- an ordering accident, not a
///     guarantee. Clearing the inherited sources and re-adding only the intended three makes "one
///     source of truth" a property of the source list itself rather than of append order. Both
///     tests mutate process-global environment variables, so they live in one class: xunit does not
///     parallelize test methods within the same class by default, and a concurrently-running test
///     that constructs its own <see cref="WebApplicationBuilder" /> or reads
///     <c>ASPNETCORE_ENVIRONMENT</c> while the other test's variable is set would be a race.
/// </remarks>
public class ConfigurationSourceTests
{
	[Fact]
	public void Configure_ReplacesTheInheritedSourcesWithExactlyTheIntendedThree()
	{
		var builder = WebApplication.CreateBuilder([]);
		ConfigurationSetup.Configure(builder, []);

		Assert.Collection(builder.Configuration.Sources,
			source => Assert.Equal(Path.Combine("Data", "appsettings.json"),
				Assert.IsType<JsonConfigurationSource>(source).Path),
			source => Assert.Equal(Path.Combine("Data", $"appsettings.{builder.Environment.EnvironmentName}.json"),
				Assert.IsType<JsonConfigurationSource>(source).Path),
			source => Assert.IsType<CommandLineConfigurationSource>(source));
	}

	[Fact]
	public void EnvironmentVariablesDoNotOverrideApplicationSettings()
	{
		Environment.SetEnvironmentVariable("Basil__Server__Port", "59999");
		try
		{
			var builder = WebApplication.CreateBuilder([]);
			ConfigurationSetup.Configure(builder, []);

			var port = builder.Configuration.GetSection(ServerOptions.SectionName).GetValue<int?>("Port");

			Assert.NotEqual(59999, port);
		}
		finally
		{
			Environment.SetEnvironmentVariable("Basil__Server__Port", null);
		}
	}

	[Fact]
	public void EnvironmentNameStillResolvesAfterTheSourceListIsCleared()
	{
		Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Staging");
		try
		{
			var builder = WebApplication.CreateBuilder([]);
			ConfigurationSetup.Configure(builder, []);

			Assert.Equal("Staging", builder.Environment.EnvironmentName);
			Assert.Contains(builder.Configuration.Sources.OfType<JsonConfigurationSource>(),
				source => source.Path != null &&
				          source.Path.EndsWith("appsettings.Staging.json", StringComparison.Ordinal));
		}
		finally
		{
			Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
		}
	}
}