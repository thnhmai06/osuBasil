namespace Basil.Protocol.Multiplayer;

/// <summary>Describes the action a spectator stream takes after a bundle of replay frames.</summary>
public enum ReplayAction : byte
{
	/// <summary>The bundle carries ordinary replay frames.</summary>
	Standard = 0,

	/// <summary>The spectated player started playing a new song.</summary>
	NewSong = 1,

	/// <summary>The spectated player skipped the intro of the song.</summary>
	Skip = 2,

	/// <summary>The spectated player finished the play.</summary>
	Completion = 3,

	/// <summary>The spectated player failed the play.</summary>
	Fail = 4,

	/// <summary>The spectated player paused the play.</summary>
	Pause = 5,

	/// <summary>The spectated player resumed the play after a pause.</summary>
	Unpause = 6,

	/// <summary>The spectated player returned to song select.</summary>
	SongSelect = 7,

	/// <summary>The spectated player is now watching another player.</summary>
	WatchingOther = 8
}

/// <summary>The decoded shape of a SpectateFrames packet: replay frames plus a score frame, action, and sequence number.</summary>
/// <param name="Frames">The replay frames received since the previous bundle.</param>
/// <param name="ScoreFrame">The latest score frame of the spectated player.</param>
/// <param name="Action">One of the enumeration values that describes the action following these frames.</param>
/// <param name="ExtraByte">The leading extra value of the bundle, whose meaning depends on <see cref="Action" />.</param>
/// <param name="Sequence">The sequence number of the bundle.</param>
public sealed record SpectateFrameBundle(
	IReadOnlyList<ReplayFrame> Frames,
	ScoreFrame ScoreFrame,
	ReplayAction Action,
	int ExtraByte,
	int Sequence);