namespace Basil.Domain.Spectating;

public sealed record SpectatorBundle(
	SpectatorAction Action,
	IReadOnlyList<ReplayFrame> ReplayFrames,
	ScoreFrame ScoreFrame,
	int Sequence);

public enum SpectatorAction : byte
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