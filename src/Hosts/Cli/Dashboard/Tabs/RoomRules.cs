namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed record RoomRules(
	bool Private,
	bool HasPassword,
	string Map,
	string Mode,
	string Mods,
	bool Freemods,
	string TeamType,
	string WinCondition);