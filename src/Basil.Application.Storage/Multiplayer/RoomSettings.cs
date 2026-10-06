using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer;

namespace Basil.Application.Storage.Multiplayer;

/// <summary>The settings a room plays with: those stored on its match and those that live only while it is open.</summary>
public sealed class RoomSettings
{
	private readonly Match _match;

	/// <summary>Creates the settings of a room.</summary>
	/// <param name="match">The match the room plays.</param>
	/// <param name="stored">The room's initial stored settings.</param>
	internal RoomSettings(Match match, MatchSettings stored)
	{
		_match = match;
		Stored = stored;
	}

	/// <summary>The stored settings the forwarding properties read.</summary>
	internal MatchSettings Stored { get; }

	/// <summary>Gets the name broadcast to clients.</summary>
	public string Name => _match.Value.Name;

	/// <summary>Gets a value that indicates whether the room's match history is private.</summary>
	public bool IsPrivate => _match.Value.IsPrivate;

	/// <summary>Gets the room's password, or an empty string for none.</summary>
	public string Password { get; internal set; } = string.Empty;

	/// <summary>Gets the currently selected beatmap.</summary>
	public BeatmapReference? Beatmap { get; internal set; }

	/// <summary>Gets the game mode played in the room.</summary>
	public GameMode Mode => Stored.Mode;

	/// <summary>Gets the mods applied to the whole room.</summary>
	public GameMods Mods => Stored.Mods;

	/// <summary>Gets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods => Stored.Freemods;

	/// <summary>Gets the team arrangement used for the room.</summary>
	public GameTeamType TeamType => Stored.TeamType;

	/// <summary>Gets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition => Stored.WinCondition;
}
