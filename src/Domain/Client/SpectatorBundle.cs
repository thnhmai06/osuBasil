namespace Basil.Domain.Client;

/// <summary>The frames a player sent in one packet while being spectated.</summary>
/// <param name="Action">What the player is doing.</param>
/// <param name="ReplayFrames">The cursor and button samples, in time order.</param>
/// <param name="ScoreFrame">The running score at the end of the bundle.</param>
/// <param name="Sequence">The position of the bundle in the stream the client sent.</param>
public sealed record SpectatorBundle(
	SpectatorAction Action,
	IReadOnlyList<ReplayFrame> ReplayFrames,
	ScoreFrame ScoreFrame,
	int Sequence);

/// <summary>What the player being spectated is doing.</summary>
public enum SpectatorAction : byte
{
	/// <summary>The player is playing.</summary>
	Standard = 0,

	/// <summary>The player started a new beatmap.</summary>
	NewSong = 1,

	/// <summary>The player skipped the intro.</summary>
	Skip = 2,

	/// <summary>The player completed the beatmap.</summary>
	Completion = 3,

	/// <summary>The player failed the beatmap.</summary>
	Fail = 4,

	/// <summary>The player paused.</summary>
	Pause = 5,

	/// <summary>The player resumed.</summary>
	Unpause = 6,

	/// <summary>The player went to song selection.</summary>
	SongSelect = 7,

	/// <summary>The player is spectating someone else.</summary>
	WatchingOther = 8
}