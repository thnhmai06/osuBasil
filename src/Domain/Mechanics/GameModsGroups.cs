namespace Basil.Domain.Mechanics;

/// <summary>The combinations of <see cref="GameMods" /> the server reasons about.</summary>
public static class GameModsGroups
{
	/// <summary>The mods that change the playback rate of the beatmap.</summary>
	public const GameMods SpeedChanging = GameMods.DoubleTime | GameMods.Nightcore | GameMods.HalfTime;

	/// <summary>The mods that restrict the play to a specific key count.</summary>
	public const GameMods Key = GameMods.Key1 | GameMods.Key2 | GameMods.Key3 | GameMods.Key4 | GameMods.Key5 |
	                            GameMods.Key6 | GameMods.Key7 | GameMods.Key8 | GameMods.Key9;

	/// <summary>The mods that only apply to osu!standard.</summary>
	public const GameMods OsuSpecific = GameMods.Autopilot | GameMods.SpunOut | GameMods.Target;

	/// <summary>The mods that only apply to osu!mania.</summary>
	public const GameMods ManiaSpecific = GameMods.Mirror | GameMods.Random | GameMods.FadeIn | Key;
}
