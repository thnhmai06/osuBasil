using Basil.Domain.Game.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Game.Scores;

public sealed record Submission
{
	public required ScoreData Score { get; init; }

	public required OsuFlags Flags { get; init; }

	public required Md5Hash HashByClient { get; init; }
}

public enum SubmissionRejectionReason : byte
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