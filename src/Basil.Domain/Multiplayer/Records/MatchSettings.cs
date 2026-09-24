using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     Represents the settings of a multiplayer match.
/// </summary>
/// <remarks>
///     A match behaves like a room that always has a beatmap assigned, so this class narrows the
///     inherited <see cref="RoomSettings.BeatmapMd5" /> to a non-nullable <see cref="Beatmap" />.
/// </remarks>
public sealed class MatchSettings() : RoomSettings
{
	public new required string BeatmapMd5
	{
		get => base.BeatmapMd5!;
		init =>
			base.BeatmapMd5 = value ?? throw new ArgumentNullException(nameof(value), "The BeatmapMd5 must be set.");
	}

	public MatchSettings(RoomSettings roomSettings) : this()
	{
		BeatmapMd5 = roomSettings.BeatmapMd5 ??
		             throw new ArgumentNullException(nameof(roomSettings.BeatmapMd5), "The BeatmapMd5 must be set.");
		Mode = roomSettings.Mode;
		Mods = roomSettings.Mods;
		Freemods = roomSettings.Freemods;
		TeamType = roomSettings.TeamType;
		WinCondition = roomSettings.WinCondition;
		Seed = roomSettings.Seed;
	}
}