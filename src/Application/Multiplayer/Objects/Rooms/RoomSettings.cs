using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

/// <summary>The settings a room plays with: those stored on its match and those that live only while it is open.</summary>
public sealed class RoomSettings
{
	private readonly MatchRecord _match;

	/// <summary>Creates the settings of a room.</summary>
	/// <param name="match">The match the room plays.</param>
	/// <param name="matchSettings">The room's initial stored settings.</param>
	internal RoomSettings(MatchRecord match, MatchSettings matchSettings)
	{
		_match = match;
		Editable = matchSettings;
		MatchSettings = new MatchSettingsView(matchSettings);
	}

	/// <summary>Gets the stored settings of the room's match, which only the room changes.</summary>
	internal MatchSettings Editable { get; }

	/// <summary>Gets the stored settings of the room's match as a reader sees them.</summary>
	public IMatchSettings MatchSettings { get; }

	/// <summary>Gets the name broadcast to clients.</summary>
	public string Name => _match.Value.Name;

	/// <summary>Gets a value that indicates whether the room's match history is private.</summary>
	public bool IsPrivate => _match.Value.IsPrivate;

	/// <summary>Gets the room's password, or an empty string for none.</summary>
	public string Password { get; internal set; } = string.Empty;

	/// <summary>Gets the currently selected beatmap.</summary>
	public BeatmapReference? Beatmap { get; internal set; }

	/// <summary>A live, read-only view of the room's settings; it cannot be turned back into the editable settings.</summary>
	private sealed class MatchSettingsView(MatchSettings settings) : IMatchSettings
	{
		/// <summary>The game mode.</summary>
		public GameMode Mode => settings.Mode;

		/// <summary>The mods.</summary>
		public GameMods Mods => settings.Mods;

		/// <summary>Whether players pick their own mods.</summary>
		public bool Freemods => settings.Freemods;

		/// <summary>The team type.</summary>
		public GameTeamType TeamType => settings.TeamType;

		/// <summary>The win condition.</summary>
		public GameWinCondition WinCondition => settings.WinCondition;

		/// <summary>The random seed.</summary>
		public int Seed => settings.Seed;
	}
}