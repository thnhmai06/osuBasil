using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal static partial class ConfigurationRows
{
	public const string Environment = "Environment variables";

	private const string Root = "General";
	private const string Hidden = "****";
	private const string FromEnvironment = "environment variable";

	private static readonly string[] EnvironmentPrefixes = ["DOTNET_", "ASPNETCORE_"];

	public static IReadOnlyList<SettingsRow> Of(IConfiguration configuration,
		IReadOnlyDictionary<string, string> environment)
	{
		// The last provider that holds a key is the one whose value the server runs with.
		var providers = (configuration as IConfigurationRoot)?.Providers.Reverse().ToList() ?? [];
		var rows = new List<SettingsRow>();
		foreach (var (key, value) in configuration.AsEnumerable()
			         .Where(pair => pair.Value is not null)
			         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
		{
			var source = SourceOf(providers, key);
			// A plain environment variable is listed with the others in the last group.
			if (source == FromEnvironment && !key.Contains(':')) continue;

			var parts = key.Split(':', 3);
			var (category, group, label) = parts.Length switch
			{
				1 => (Root, null, key),
				2 => (parts[0], null, parts[1]),
				_ => (parts[0], parts[1], parts[2])
			};
			rows.Add(new SettingsRow(category, group, key, label, $"{Hide(key, value!)}   ({source})", SettingsFieldKind.Text));
		}

		foreach (var (name, value) in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
		{
			var prefix =
				EnvironmentPrefixes.FirstOrDefault(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
			rows.Add(new SettingsRow(Environment, prefix, name, prefix is null ? name : name[prefix.Length..],
				Hide(name, value), SettingsFieldKind.Text, true));
		}

		return rows;
	}

	private static string SourceOf(IReadOnlyList<IConfigurationProvider> providers, string key)
	{
		var holder = providers.FirstOrDefault(provider => provider.TryGet(key, out _));
		return holder switch
		{
			FileConfigurationProvider file => $"file {Path.GetFileName(file.Source.Path)}",
			EnvironmentVariablesConfigurationProvider => FromEnvironment,
			CommandLineConfigurationProvider => "command line",
			_ => "default"
		};
	}

	private static string Hide(string key, string value)
	{
		return Secret().IsMatch(key) ? Hidden : value;
	}

	[GeneratedRegex("password|secret|token|key|connectionstring", RegexOptions.IgnoreCase)]
	private static partial Regex Secret();
}