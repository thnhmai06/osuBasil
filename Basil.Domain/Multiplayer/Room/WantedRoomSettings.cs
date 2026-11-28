using Basil.Domain.Beatmaps;
using Basil.Domain.Game;

namespace Basil.Domain.Multiplayer.Room;

/// <summary>The full settings a client wants a room to have; the room works out what differs from its own.</summary>
/// <param name="Name">The wanted room name.</param>
/// <param name="Mods">The wanted mods.</param>
/// <param name="Freemods">Whether players choose their own mods.</param>
/// <param name="Beatmap">The wanted beatmap, or <see langword="null"/> when the client names none.</param>
/// <param name="Mode">The wanted game mode, or <see langword="null"/> when the client names none.</param>
/// <param name="TeamType">The wanted team type, or <see langword="null"/> when the client names none.</param>
/// <param name="WinCondition">The wanted win condition, or <see langword="null"/> when the client names none.</param>
public sealed record WantedRoomSettings(
	string Name,
	GameMods Mods,
	bool Freemods,
	BeatmapReference? Beatmap = null,
	GameMode? Mode = null,
	GameTeamType? TeamType = null,
	GameWinCondition? WinCondition = null);
