namespace Basil.Protocol.OsuStable.Models.Wire;

/// <summary>The activity numbers the osu! client uses in a user's status.</summary>
public enum WireUserAction : byte
{
	/// <summary>Idle in the menu.</summary>
	Idle = 0,

	/// <summary>Away from keyboard.</summary>
	Afk = 1,

	/// <summary>Playing a beatmap.</summary>
	Playing = 2,

	/// <summary>Editing a beatmap.</summary>
	Editing = 3,

	/// <summary>Modding a beatmap.</summary>
	Modding = 4,

	/// <summary>Selecting a beatmap in a multiplayer match.</summary>
	Multiplayer = 5,

	/// <summary>Watching another player.</summary>
	Watching = 6,

	/// <summary>Unknown.</summary>
	Unknown = 7,

	/// <summary>Testing a beatmap.</summary>
	Testing = 8,

	/// <summary>Submitting a score.</summary>
	Submitting = 9,

	/// <summary>Paused while playing.</summary>
	Paused = 10,

	/// <summary>In the multiplayer lobby.</summary>
	Lobby = 11,

	/// <summary>Playing in a multiplayer match.</summary>
	Multiplaying = 12,

	/// <summary>Browsing osu!direct.</summary>
	OsuDirect = 13
}
