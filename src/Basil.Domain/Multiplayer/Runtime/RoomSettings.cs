using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;

namespace Basil.Domain.Multiplayer.Runtime;

/// <summary>
///     Represents the settings of an osu! multiplayer room: the selected beatmap, game mode,
///     mods, team arrangement, and win condition.
/// </summary>
public class RoomSettings
{
	/// <summary>Gets or sets the currently selected beatmap.</summary>
	/// <remarks>
	///     A <see langword="null" /> value means that no beatmap has been selected yet — not that a
	///     selected beatmap could not be found.
	/// </remarks>
	public string? BeatmapMd5
	{
		get;
		internal set => field = value is null || Md5.IsValid(value)
			? value?.ToLowerInvariant() ?? null
			: throw new ArgumentException("The value must be a valid MD5 hash.", nameof(value));
	} // null = unset (not "Beatmap not found")

	/// <summary>Gets or sets the game mode the room plays in.</summary>
	public GameMode Mode
	{
		get;
		internal set => field = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "GameMode is not a defined value.");
	} = GameMode.Standard;

	/// <summary>Gets or sets the mods applied to the whole room.</summary>
	public GameMods Mods
	{
		get;
		internal set => field = value.RemoveInvalidMods(Mode);
	} = GameMods.NoMod;

	/// <summary>Gets or sets a value that indicates whether freemod mode is enabled.</summary>
	public bool Freemods { get; internal set; } = false;

	/// <summary>Gets or sets the team arrangement used for the room.</summary>
	public GameTeamType TeamType
	{
		get;
		internal set => field = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "GameTeamType is not a defined value.");
	} = GameTeamType.HeadToHead;

	/// <summary>Gets or sets the condition that decides the winner of a round.</summary>
	public GameWinCondition WinCondition
	{
		get;
		set => field = Enum.IsDefined(value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, "GameWinCondition is not a defined value.");
	} = GameWinCondition.Score;

	/// <summary>Gets the room's random seed, broadcast to clients as part of the match's data.</summary>
	public int Seed { get; init; }
}