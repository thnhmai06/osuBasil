namespace Basil.Host.Cli.Dashboard.Tabs;

internal static class SettingsPages
{
	public const string Configuration = "Configuration";

	public const string Dashboard = "Dashboard";

	public static IReadOnlyList<(string Name, string[] Categories)> All { get; } =
	[
		("Server", ["general", "cors", "uploads"]),
		("Accounts", ["accounts", "locks", "sessions", "rateLimits"]),
		("Gameplay", ["beatmaps", "multiplayer", "bot"]),
		(Dashboard, ["dashboard"]),
		(Configuration, [])
	];
}