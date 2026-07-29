namespace Basil.Protocol.Multiplayer;

/// <summary>Ported from ReplayAction (IntEnum) in app/packets.py.</summary>
public enum ReplayAction : byte
{
	Standard = 0,
	NewSong = 1,
	Skip = 2,
	Completion = 3,
	Fail = 4,
	Pause = 5,
	Unpause = 6,
	SongSelect = 7,
	WatchingOther = 8
}

/// <summary>Ported from ReplayFrameBundle (NamedTuple) in app/packets.py — the decoded shape of a SpectateFrames packet.</summary>
public sealed record SpectateFrameBundle(
	IReadOnlyList<ReplayFrameData> Frames,
	ScoreFrameData ScoreFrame,
	ReplayAction Action,
	int ExtraByte,
	int Sequence);