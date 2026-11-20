using Basil.Domain.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Scores;

/// <summary>A play handed in for recording, together with what is needed to check it.</summary>
public sealed record Submission
{
	/// <summary>The play that was handed in.</summary>
	public required ScoreData Score { get; init; }

	/// <summary>The flags the submitting client sent along with the play.</summary>
	public required OsuFlags Flags { get; init; }

	/// <summary>The hash the client computed for the submission, to compare with the one the server derives.</summary>
	public required Md5Hash HashByClient { get; init; }
}

/// <summary>Why a submitted play was refused.</summary>
public enum SubmissionRejectionReason : byte
{
	/// <summary>The client version is not accepted.</summary>
	VersionMismatch,

	/// <summary>The client hash does not match the one registered for the session.</summary>
	ClientHashMismatch,

	/// <summary>The uninstaller hash does not match the one registered for the session.</summary>
	UninstallerHashMismatch,

	/// <summary>The disk signature hash does not match the one registered for the session.</summary>
	DiskSignatureHashMismatch,

	/// <summary>The submission hash does not match the play.</summary>
	SubmissionHashMismatch,

	/// <summary>The beatmap hash does not match the beatmap the play claims.</summary>
	BeatmapHashMismatch,

	/// <summary>The beatmap of the play is not known.</summary>
	UnknownBeatmap,

	/// <summary>The same play was already submitted.</summary>
	Duplicate
}