namespace Basil.Protocol.Bancho.Models.Wire;

/// <summary>The game mode numbers the osu! client uses.</summary>
public enum WireGameMode : byte
{
	/// <summary>The osu!standard mode.</summary>
	Standard = 0,

	/// <summary>The osu!taiko mode.</summary>
	Taiko = 1,

	/// <summary>The osu!catch mode.</summary>
	Catch = 2,

	/// <summary>The osu!mania mode.</summary>
	Mania = 3
}
