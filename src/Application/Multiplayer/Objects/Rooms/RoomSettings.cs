using Basil.Domain.Game.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed class RoomSettings
{
	private readonly MatchRecord _match;

	internal RoomSettings(MatchRecord match, MatchSettings matchSettings)
	{
		_match = match;
		Editable = matchSettings;
		MatchSettings = new MatchSettingsView(matchSettings);
	}

	internal MatchSettings Editable { get; }

	public IMatchSettings MatchSettings { get; }

	public string Name => _match.Value.Name;

	public bool IsPrivate => _match.Value.IsPrivate;

	public string Password { get; internal set; } = string.Empty;

	public BeatmapReference? Beatmap { get; internal set; }

	private sealed class MatchSettingsView(MatchSettings settings) : IMatchSettings
	{
		public GameMode Mode => settings.Mode;

		public GameMods Mods => settings.Mods;

		public bool Freemods => settings.Freemods;

		public GameTeamType TeamType => settings.TeamType;

		public GameWinCondition WinCondition => settings.WinCondition;

		public int Seed => settings.Seed;
	}
}