using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Domain.Multiplayer.Records;

/// <summary>
///     Represents the settings of a multiplayer match.
/// </summary>
/// <remarks>
///     A match behaves like a room that always has a beatmap assigned, so this class narrows the
///     inherited <see cref="RoomSettings.Beatmap" /> to a non-nullable <see cref="Beatmap" />.
/// </remarks>
public sealed class MatchSettings : RoomSettings
{
	/// <summary>
	///     Gets or sets the beatmap the match is played on.
	/// </summary>
	/// <remarks>
	///     Unlike the base <see cref="RoomSettings.Beatmap" />, this property is never
	///     <see langword="null" /> because a match always has a beatmap assigned.
	/// </remarks>
	public new required Beatmap Beatmap
	{
		get => base.Beatmap!;
		init => base.Beatmap = value;
	}
}