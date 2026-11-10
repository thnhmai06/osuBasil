using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Common;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Storage;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Scores.Logic;

/// <summary>
///     Validates and stores submitted scores, their replays, and the player's updated statistics.
/// </summary>
internal sealed class ScoreRecorder(
	IScoreRepository scores,
	IReplayStorage replays) : IScoreRecorder
{
	/// <summary>The shortest replay, in bytes, that is kept.</summary>
	internal const int MinReplayLength = 24;

	/// <inheritdoc />
	public async Task<ScoreOutcome> ValidateAndRecordAsync(
		UserRecord player,
		OsuClient loginClient,
		Submission submission,
		BeatmapChecksums checkedBeatmap,
		SubmittedClient client,
		Stream? replay,
		CancellationToken ct = default)
	{
		if (Validate(submission, loginClient, checkedBeatmap, player.Value.Name, client) is { } rejection)
			return new ScoreOutcome(rejection, null, null);

		// Every passed score counts toward ranked score, whatever status the beatmapset is reported with.
		ScoreRecord score;
		UserStats current;
		try
		{
			(score, current) = await scores.CreateWithPlayAsync(
				submission.Score with { UserId = player.Id, Checksum = submission.HashByClient },
				submission.Score.IsPassed ? submission.Score.LastState.TotalScore : 0);
		}
		catch (AlreadyExistsException)
		{
			return new ScoreOutcome(ScoreRejectionReason.Duplicate, null, null);
		}

		if (submission.Score.IsPassed && replay is { Length: >= MinReplayLength })
			await replays.SaveAsync(score, loginClient.Version, replay);

		return new ScoreOutcome(null, score, current);
	}

	/// <summary>
	///     Verifies that the submission is authentic, recomputing the legacy checksum formulas that
	///     mirror the osu! server and comparing them with the client-sent values.
	/// </summary>
	/// <remarks>
	///     Checks, in order: that the client's version date matches the version the client logged in
	///     with, that the client's fingerprint hash and its serial-derived hashes match the ones
	///     reported at login, that the submission MD5 matches the recomputed value, and that the
	///     beatmap MD5 the client claims matches the beatmap the score is checked against. On the
	///     first mismatch the reason is returned and validation stops.
	/// </remarks>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="osuClient">The osu! client the player logged in with.</param>
	/// <param name="beatmap">The MD5 and storyboard MD5 of the beatmap the score is checked against.</param>
	/// <param name="playerName">The player's name as the server knows it.</param>
	/// <param name="submittedClient">The osu! client information sent with the submission.</param>
	/// <returns><see langword="null" /> if the submission is authentic; otherwise, the first reason it is rejected.</returns>
	private static ScoreRejectionReason? Validate(
		Submission submission,
		OsuClient osuClient,
		BeatmapChecksums beatmap,
		string playerName,
		SubmittedClient submittedClient)
	{
		var md5ByServer = ComputeSubmissionMd5();

		if (submittedClient.VersionDate != osuClient.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			return ScoreRejectionReason.VersionMismatch;
		if (submittedClient.Fingerprint != osuClient.Fingerprint) return ScoreRejectionReason.ClientHashMismatch;
		if (new Md5(Encoding.UTF8.GetBytes(submittedClient.UninstallId)) != osuClient.Fingerprint.UninstallHash)
			return ScoreRejectionReason.UninstallerHashMismatch;
		if (new Md5(Encoding.UTF8.GetBytes(submittedClient.DiskSignature)) !=
		    osuClient.Fingerprint.DiskSignatureHash)
			return ScoreRejectionReason.DiskSignatureHashMismatch;
		if (submission.HashByClient != md5ByServer) return ScoreRejectionReason.SubmissionHashMismatch;
		if (submittedClient.BeatmapHash != beatmap.Hash) return ScoreRejectionReason.BeatmapHashMismatch;

		return null;

		#region DON'T CHANGE THESE FORMULA!!!

		string ComputeSubmissionMd5()
		{
			var hitCounts = submission.Score.LastState.HitCounts;

			var raw =
				$"chickenmcnuggets{hitCounts.Num100 + hitCounts.Num300}o15{hitCounts.Num50}{hitCounts.NumGeki}" +
				$"smustard{hitCounts.NumKatu}{hitCounts.NumMiss}uu{beatmap.Hash}{submission.Score.LastState.MaxCombo}" +
				$"{submission.Score.LastState.IsFullCombo}{playerName}{submission.Score.LastState.TotalScore}{submission.Score.Grade.ToString().ToUpperInvariant()}" +
				$"{(int)submission.Score.Mods}Q{submission.Score.IsPassed}{(int)submission.Score.Mode}" +
				$"{submittedClient.VersionDate}{submission.Score.Timestamp:yyMMddHHmmss}{submittedClient.ClientHash}{beatmap.StoryboardHash?.ToString() ?? string.Empty}";
			var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
			return Convert.ToHexStringLower(hash);
		}

		#endregion
	}
}