namespace Basil.Domain.Spectating;

/// <summary>One update of a watched play: what happened and the frames that go with it.</summary>
/// <param name="Action">What happened in the play.</param>
/// <param name="ReplayFrames">The input samples since the previous update.</param>
/// <param name="ScoreFrame">The standing of the play at this update.</param>
/// <param name="Sequence">The position of the update in the stream of the play, to keep updates in order.</param>
public sealed record SpectatorBundle(
	SpectatorAction Action,
	IReadOnlyList<ReplayFrame> ReplayFrames,
	ScoreFrame ScoreFrame,
	int Sequence);

/// <summary>What happened in a watched play; the values carry the game numbering.</summary>
public enum SpectatorAction : byte
{
	/// <summary>The play goes on as usual.</summary>
	Standard = 0,

	/// <summary>The player started a new song.</summary>
	NewSong = 1,

	/// <summary>The player skipped the intro.</summary>
	Skip = 2,

	/// <summary>The player finished the play.</summary>
	Completion = 3,

	/// <summary>The player failed.</summary>
	Fail = 4,

	/// <summary>The player paused.</summary>
	Pause = 5,

	/// <summary>The player resumed after a pause.</summary>
	Unpause = 6,

	/// <summary>The player went back to song selection.</summary>
	SongSelect = 7,

	/// <summary>The player started watching someone else.</summary>
	WatchingOther = 8
}