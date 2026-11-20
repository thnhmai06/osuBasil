namespace Basil.Domain.Users;

/// <summary>What a user is doing, numbered as the osu! (stable) client numbers its activities.</summary>
public enum UserActivity : byte
{
	/// <summary>The user is doing nothing in particular.</summary>
	Idle = 0,

	/// <summary>The user is away from the keyboard.</summary>
	Afk = 1,

	/// <summary>The user is playing a beatmap.</summary>
	Playing = 2,

	/// <summary>The user is editing a beatmap.</summary>
	Editing = 3,

	/// <summary>The user is reviewing a beatmap as a modder.</summary>
	Modding = 4,

	/// <summary>The user is in a multiplayer room without playing.</summary>
	Multiplayer = 5,

	/// <summary>The user is watching another user play.</summary>
	Watching = 6,

	/// <summary>The activity is not known.</summary>
	Unknown = 7,

	/// <summary>The user is testing a beatmap.</summary>
	Testing = 8,

	/// <summary>The user is submitting a beatmap.</summary>
	Submitting = 9,

	/// <summary>The user has paused a play.</summary>
	Paused = 10,

	/// <summary>The user is in the multiplayer lobby.</summary>
	Lobby = 11,

	/// <summary>The user is playing a multiplayer match.</summary>
	Multiplaying = 12,

	/// <summary>The user is browsing beatmaps to download.</summary>
	OsuDirect = 13
}