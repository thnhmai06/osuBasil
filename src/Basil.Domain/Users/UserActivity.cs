namespace Basil.Domain.Users;

/// <summary>
///     Describes the current state of a player's osu! client.
/// </summary>
public enum UserActivity : byte
{
	/// <summary>The player is doing nothing.</summary>
	Idle = 0,
	/// <summary>The player is away from the keyboard.</summary>
	Afk = 1,
	/// <summary>The player is playing a beatmap.</summary>
	Playing = 2,
	/// <summary>The player is editing a beatmap.</summary>
	Editing = 3,
	/// <summary>The player is modding a beatmap.</summary>
	Modding = 4,
	/// <summary>The player is in a multiplayer lobby or match.</summary>
	Multiplayer = 5,
	/// <summary>The player is watching a replay or spectating another player.</summary>
	Watching = 6,
	/// <summary>The player's activity is unknown.</summary>
	Unknown = 7,
	/// <summary>The player is testing a beatmap.</summary>
	Testing = 8,
	/// <summary>The player is submitting a score.</summary>
	Submitting = 9,
	/// <summary>The player has paused play.</summary>
	Paused = 10,
	/// <summary>The player is browsing the multiplayer lobby.</summary>
	Lobby = 11,
	/// <summary>The player is playing in a multiplayer match.</summary>
	Multiplaying = 12,
	/// <summary>The player is browsing beatmaps through osu!direct.</summary>
	OsuDirect = 13
}
