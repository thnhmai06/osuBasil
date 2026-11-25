using Basil.Domain.Beatmaps;
using Basil.Domain.Game;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>A requested change to a room's settings; every member left unset keeps its current value.</summary>
/// <param name="Name">The new room name.</param>
/// <param name="Beatmap">The new beatmap; cannot be combined with <paramref name="ClearBeatmap"/>.</param>
/// <param name="Mode">The new game mode.</param>
/// <param name="Mods">The new mods; under freemods the speed-changing ones apply to the room and the rest to the caller's slot.</param>
/// <param name="Freemods">Whether players choose their own mods.</param>
/// <param name="TeamType">The new team type.</param>
/// <param name="WinCondition">The new win condition.</param>
/// <param name="Password">The new password; empty removes it.</param>
/// <param name="Size">The number of usable slots, from <see cref="RoomRules.MinPlayers"/> to <see cref="RoomRules.MaxPlayers"/>.</param>
/// <param name="ClearBeatmap">Whether to remove the beatmap.</param>
/// <param name="IsPrivate">Whether the room is private.</param>
public sealed record RoomSettingsChange(
	string? Name = null,
	BeatmapReference? Beatmap = null,
	GameMode? Mode = null,
	GameMods? Mods = null,
	bool? Freemods = null,
	GameTeamType? TeamType = null,
	GameWinCondition? WinCondition = null,
	string? Password = null,
	int? Size = null,
	bool ClearBeatmap = false,
	bool? IsPrivate = null)
{
	/// <summary>Creates the change that sets the room's mods and freemods; a value not given means no mods and freemods off.</summary>
	/// <param name="mods">The mods to set, or <see langword="null"/> for none.</param>
	/// <param name="freemods">Whether players choose their own mods, or <see langword="null"/> for off.</param>
	public static RoomSettingsChange OfMods(GameMods? mods, bool? freemods)
	{
		return new RoomSettingsChange(Mods: mods ?? GameMods.NoMod, Freemods: freemods ?? false);
	}

	/// <summary>Gets whether the change touches something a round plays with: the beatmap, mode, mods, freemods, team type or win condition.</summary>
	public bool AltersTheRound => Beatmap is not null || ClearBeatmap || Mode is not null || Mods is not null ||
	                              Freemods is not null || TeamType is not null || WinCondition is not null;
}