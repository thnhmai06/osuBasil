namespace Basil.Domain.Scores;

public enum ScoreRejectionReason : byte
{
	VersionMismatch,

	ClientHashMismatch,

	UninstallerHashMismatch,

	DiskSignatureHashMismatch,

	SubmissionHashMismatch,

	BeatmapHashMismatch,

	UnknownBeatmap,

	Duplicate
}