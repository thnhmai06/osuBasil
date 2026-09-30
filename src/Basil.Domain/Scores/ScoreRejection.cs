namespace Basil.Domain.Scores;

/// <summary>The reasons a score submission is rejected.</summary>
public enum ScoreRejection : byte
{
	/// <summary>The client version date does not match the version the client logged in with.</summary>
	VersionMismatch,

	/// <summary>The client hash does not match the one reported at login.</summary>
	ClientHashMismatch,

	/// <summary>The uninstaller id does not match the one reported at login.</summary>
	UninstallerHashMismatch,

	/// <summary>The disk signature does not match the one reported at login.</summary>
	DiskSignatureHashMismatch,

	/// <summary>The submission checksum does not match the submitted score.</summary>
	SubmissionHashMismatch,

	/// <summary>The beatmap the client claims to have played is not the one the score was checked against.</summary>
	BeatmapHashMismatch,

	/// <summary>The server does not know the beatmap and it is not the beatmap of the player's current round.</summary>
	UnknownBeatmap
}
