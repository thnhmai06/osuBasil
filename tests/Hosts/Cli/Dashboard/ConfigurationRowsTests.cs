using Basil.Host.Cli.Dashboard.Tabs;
using Basil.Host.Shared.Views;
using Microsoft.Extensions.Configuration;

namespace Basil.Host.Cli.Tests.Dashboard;

public sealed class ConfigurationRowsTests
{
	private static IReadOnlyList<SettingsRow> Rows(Dictionary<string, string?> configuration, Dictionary<string, string> environment)
	{
		return ConfigurationRows.Of(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build(), environment);
	}

	[Fact]
	public void EnvironmentVariables_AreTheLastGroup()
	{
		var rows = Rows(new() { ["Basil:Server:Name"] = "Basil", ["Zeta:Key"] = "v" }, new() { ["HOME"] = "/home/x" });

		Assert.Equal(ConfigurationRows.Environment, rows[^1].Category);
		Assert.DoesNotContain(rows.Take(rows.Count - 1), row => row.Category == ConfigurationRows.Environment);
	}

	[Fact]
	public void EnvironmentVariable_WithKnownPrefix_GoesUnderItsPrefixWithARelativeName()
	{
		var rows = Rows([], new() { ["DOTNET_ROOT"] = "/dotnet", ["ASPNETCORE_URLS"] = "http://*", ["PATH"] = "/bin" });

		Assert.Contains(rows, row => row is { Group: "DOTNET_", Label: "ROOT" });
		Assert.Contains(rows, row => row is { Group: "ASPNETCORE_", Label: "URLS" });
		Assert.Contains(rows, row => row is { Group: null, Label: "PATH" });
	}

	[Fact]
	public void ConfigurationKey_TakenFromTheEnvironment_IsNotListedAsApplicationConfiguration()
	{
		var rows = Rows(new() { ["Basil:Server:Name"] = "from env", ["Kestrel:Port"] = "1" },
			new() { ["Basil__Server__Name"] = "from env" });

		Assert.DoesNotContain(rows, row => row.Path == "Basil:Server:Name");
		Assert.Contains(rows, row => row.Path == "Kestrel:Port");
	}

	[Theory]
	[InlineData("Db:Password")]
	[InlineData("Cert:Path")]
	public void Secret_InTheApplicationConfiguration_IsHidden(string key)
	{
		var rows = Rows(new() { [key] = "hunter2" }, []);

		Assert.DoesNotContain(rows, row => row.Value.Contains("hunter2"));
	}

	[Fact]
	public void Secret_InTheEnvironment_IsHidden()
	{
		var rows = Rows([], new() { ["API_TOKEN"] = "hunter2" });

		Assert.DoesNotContain(rows, row => row.Value.Contains("hunter2"));
	}

	[Fact]
	public void SettingsPages_CoverEveryCategoryOnce()
	{
		var shown = SettingsPages.All.SelectMany(page => page.Categories).ToList();

		Assert.Equal(SettingsCategories.Names.Order(StringComparer.OrdinalIgnoreCase),
			shown.Order(StringComparer.OrdinalIgnoreCase));
	}

	[Fact]
	public void SettingsPages_AreFourWithConfigurationLast()
	{
		Assert.Equal(["Server", "Accounts", "Gameplay", "Configuration"], SettingsPages.All.Select(page => page.Name));
	}
}
