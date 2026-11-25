using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Client;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Scores;

/// <summary>A play handed in for recording, together with what is needed to check it.</summary>
public sealed record Submission
{
	/// <summary>The play that was handed in.</summary>
	public required ScoreData Score { get; init; }

	/// <summary>The flags the submitting client sent along with the play.</summary>
	public required ClientFlags Flags { get; init; }

	/// <summary>The hash the client computed for the submission, to compare with the one the server derives.</summary>
	public required Md5Hash HashByClient { get; init; }

	private const int MinReplayLength = 24;

	/// <summary>Checks the submission against the client the player signed in with and the beatmap it was played on.</summary>
	/// <param name="loginClient">The client build the player signed in with.</param>
	/// <param name="beatmap">The hashes of the beatmap, as the server knows them.</param>
	/// <param name="playerName">The name of the player.</param>
	/// <param name="client">What the submitting client reported about itself.</param>
	/// <returns>The first reason the play is refused, or <see langword="null" /> when it is accepted.</returns>
	public SubmissionRejectionReason? Validate(ClientBuild loginClient, BeatmapChecksums beatmap, string playerName,
		SubmittedClient client)
	{
		var md5ByServer = ComputeSubmissionMd5(beatmap, playerName, client);

		if (client.VersionDate != loginClient.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			return SubmissionRejectionReason.VersionMismatch;
		if (client.Fingerprint != loginClient.Fingerprint) return SubmissionRejectionReason.ClientHashMismatch;
		if (new Md5Hash(Encoding.UTF8.GetBytes(client.UninstallId)) != loginClient.Fingerprint.UninstallHash)
			return SubmissionRejectionReason.UninstallerHashMismatch;
		if (new Md5Hash(Encoding.UTF8.GetBytes(client.DiskSignature)) != loginClient.Fingerprint.DiskSignatureHash)
			return SubmissionRejectionReason.DiskSignatureHashMismatch;
		if (HashByClient != md5ByServer) return SubmissionRejectionReason.SubmissionHashMismatch;
		if (client.BeatmapHash != beatmap.Hash) return SubmissionRejectionReason.BeatmapHashMismatch;

		return null;
	}

	/// <summary>Tells whether the replay of this play is kept: only a passed play keeps one, and only when it is not trivially short.</summary>
	/// <param name="replayLength">The length of the replay in bytes.</param>
	public bool KeepsReplay(long replayLength)
	{
		return Score.IsPassed && replayLength >= MinReplayLength;
	}

	#region DON'T CHANGE THESE FORMULA!!!

	private string ComputeSubmissionMd5(BeatmapChecksums beatmap, string playerName, SubmittedClient client)
	{
		var hitCounts = Score.LastState.HitCounts;

		var raw =
			$"chickenmcnuggets{hitCounts.Num100 + hitCounts.Num300}o15{hitCounts.Num50}{hitCounts.NumGeki}" +
			$"smustard{hitCounts.NumKatu}{hitCounts.NumMiss}uu{beatmap.Hash}{Score.LastState.MaxCombo}" +
			$"{Score.LastState.IsFullCombo}{playerName}{Score.LastState.TotalScore}{Score.Grade.ToString().ToUpperInvariant()}" +
			$"{(int)Score.Mods}Q{Score.IsPassed}{(int)Score.Mode}" +
			$"{client.VersionDate}{Score.Timestamp:yyMMddHHmmss}{client.ClientHash}{beatmap.StoryboardHash?.ToString() ?? string.Empty}";
		var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
		return Convert.ToHexStringLower(hash);
	}

	#endregion
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