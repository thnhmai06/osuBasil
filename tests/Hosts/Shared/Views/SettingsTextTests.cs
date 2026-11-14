using System.Text.Json;
using System.Text.Json.Nodes;
using Basil.Application.Shared.Settings;
using Basil.Host.Shared.Json;
using Basil.Host.Shared.Views;

namespace Basil.Host.Shared.Tests.Views;

/// <summary>The names and descriptions the operator reads for every server setting.</summary>
public sealed class SettingsTextTests
{
	private static readonly string[] Paths = FlattenedPaths();

	[Fact]
	public void EverySetting_HasLabel()
	{
		Assert.DoesNotContain(Paths, path => string.IsNullOrWhiteSpace(SettingsLabels.Of(path)));
	}

	[Fact]
	public void EveryLabel_FitsTheLabelColumn()
	{
		Assert.DoesNotContain(SettingsLabels.All.Values, label => label.Length > SettingsLabels.MaxLength);
	}

	[Fact]
	public void EverySetting_HasDescription()
	{
		Assert.DoesNotContain(Paths, path => string.IsNullOrWhiteSpace(SettingsDescriptions.Of(path)));
	}

	[Fact]
	public void EveryDescription_StatesTheDefault()
	{
		Assert.DoesNotContain(Paths, path => !SettingsDescriptions.Of(path)!.Contains("Default:", StringComparison.Ordinal));
	}

	[Fact]
	public void NoLabelOrDescription_NamesAMissingSetting()
	{
		Assert.Empty(SettingsLabels.All.Keys.Except(Paths));
		Assert.Empty(SettingsDescriptions.All.Keys.Except(Paths));
	}

	// Every leaf of every category, plus the main menu icon, which has no category of its own.
	private static string[] FlattenedPaths()
	{
		var settings = new ServerSettings();
		var paths = new List<string>();
		foreach (var (name, value) in SettingsCategories.GetAll(settings).Append(new("menuIcon", settings.MenuIcon)))
			Flatten(paths, name, JsonSerializer.SerializeToNode(value, ApiJson.Options));
		return [.. paths];
	}

	private static void Flatten(List<string> paths, string prefix, JsonNode? node)
	{
		if (node is JsonObject obj)
			foreach (var (key, value) in obj)
				Flatten(paths, $"{prefix}.{key}", value);
		else
			paths.Add(prefix);
	}
}