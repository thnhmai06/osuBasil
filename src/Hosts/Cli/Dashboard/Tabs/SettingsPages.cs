namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The pages of the settings tab and the categories of the server settings that each one folds.</summary>
internal static class SettingsPages
{
	/// <summary>The name of the page that lists the configuration of the server, which is read-only.</summary>
	public const string Configuration = "Configuration";

	/// <summary>Gets the pages in the order of the strip, each with its categories in the order they are shown.</summary>
	public static IReadOnlyList<(string Name, string[] Categories)> All { get; } =
	[
		("Server", ["general", "cors", "uploads"]),
		("Accounts", ["accounts", "locks", "sessions", "rateLimits"]),
		("Gameplay", ["beatmaps", "multiplayer", "bot"]),
		(Configuration, [])
	];
}
