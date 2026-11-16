namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The settings of an open room, as the detail shows and edits them.</summary>
/// <param name="Private">Whether its match history is private.</param>
/// <param name="HasPassword">Whether joining needs a password.</param>
/// <param name="Map">The name of the selected beatmap, or empty.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Mods">The mods, as text.</param>
/// <param name="Freemods">Whether players pick their own mods.</param>
/// <param name="TeamType">The team type.</param>
/// <param name="WinCondition">The win condition.</param>
internal sealed record RoomRules(bool Private, bool HasPassword, string Map, string Mode, string Mods, bool Freemods,
	string TeamType, string WinCondition);
