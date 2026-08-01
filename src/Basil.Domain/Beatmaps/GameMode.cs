namespace Basil.Domain.Beatmaps;

/// <summary>
///     Represents the four game modes supported by osu!.
/// </summary>
public enum GameMode : byte
{
	/// <summary>The standard osu! mode.</summary>
	Standard = 0,
	/// <summary>The osu!taiko mode.</summary>
	Taiko = 1,
	/// <summary>The osu!catch mode.</summary>
	Catch = 2,
	/// <summary>The osu!mania mode.</summary>
	Mania = 3
}
