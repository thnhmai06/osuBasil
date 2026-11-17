using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>Lists the configuration of the server for the read-only page: the application configuration, then the environment variables.</summary>
internal static partial class ConfigurationRows
{
	/// <summary>The name of the group that holds the environment variables.</summary>
	public const string Environment = "Environment variables";

	private const string Root = "General";
	private const string Hidden = "••••";

	private static readonly string[] EnvironmentPrefixes = ["DOTNET_", "ASPNETCORE_"];

	/// <summary>Lists the configuration, with the values of secrets hidden.</summary>
	/// <param name="configuration">The configuration of the server.</param>
	/// <param name="environment">The environment variables of the process.</param>
	/// <returns>
	///     The application configuration grouped by section, then the environment variables grouped by prefix. A key the
	///     configuration took from an environment variable is listed with the variables, not with the configuration.
	/// </returns>
	public static IReadOnlyList<SettingsRow> Of(IConfiguration configuration, IReadOnlyDictionary<string, string> environment)
	{
		var fromEnvironment = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var name in environment.Keys)
		{
			fromEnvironment.Add(name.Replace("__", ":"));
			foreach (var prefix in EnvironmentPrefixes)
				if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					fromEnvironment.Add(name[prefix.Length..].Replace("__", ":"));
		}

		var rows = new List<SettingsRow>();
		foreach (var (key, value) in configuration.AsEnumerable()
			         .Where(pair => pair.Value is not null && !fromEnvironment.Contains(pair.Key))
			         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
		{
			var parts = key.Split(':', 3);
			var (category, group, label) = parts.Length switch
			{
				1 => (Root, null, key),
				2 => (parts[0], null, parts[1]),
				_ => (parts[0], parts[1], parts[2])
			};
			rows.Add(new SettingsRow(category, group, key, label, Hide(key, value!), SettingsFieldKind.Text));
		}

		foreach (var (name, value) in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
		{
			var prefix = EnvironmentPrefixes.FirstOrDefault(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
			rows.Add(new SettingsRow(Environment, prefix, name, prefix is null ? name : name[prefix.Length..],
				Hide(name, value), SettingsFieldKind.Text, true));
		}

		return rows;
	}

	private static string Hide(string key, string value)
	{
		return Secret().IsMatch(key) ? Hidden : value;
	}

	[GeneratedRegex("password|secret|token|key|connectionstring|cert", RegexOptions.IgnoreCase)]
	private static partial Regex Secret();
}
