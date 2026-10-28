using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Common;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Bancho;
using Basil.Domain.Common;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Capabilities;

/// <summary>
///     Validates and stores submitted scores, their replays, and the player's updated statistics.
/// </summary>
internal sealed class ScoreRecorder(
	IScoreRepository scores,
	IReplayStorage replays,
	IUserStatsRepository stats)
{
	/// <summary>The shortest replay, in bytes, that is kept.</summary>
	internal const int MinReplayLength = 24;

	/// <summary>Validates and records a score with its optional replay and updated statistics.</summary>
	/// <param name="player">The account that submitted the score.</param>
	/// <param name="loginClient">The osu! client the player logged in with.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="checkedBeatmap">The checksums of the beatmap the score is checked against.</param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">
	///     The seekable stream of the replay, positioned at its start and left open, or <see langword="null" /> when
	///     none is kept.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The rejection, or the stored score and updated statistics.</returns>
	public async Task<ScoreOutcome> ValidateAndRecordAsync(
		UserRecord player,
		BanchoClient loginClient,
		Submission submission,
		BeatmapChecksums checkedBeatmap,
		SubmittedClient client,
		Stream? replay,
		CancellationToken cancellationToken = default)
	{
		if (Validate(submission, loginClient, checkedBeatmap, player.Value.Name, client) is { } rejection)
			return new ScoreOutcome(rejection, null, null);

		ScoreRecord score;
		try
		{
			score = await scores.CreateAsync(
				submission.Score with { UserId = player.Id, Checksum = submission.HashByClient }, cancellationToken);
		}
		catch (AlreadyExistsException)
		{
			return new ScoreOutcome(ScoreRejection.Duplicate, null, null);
		}

		if (submission.Score.IsPassed && replay is { Length: >= MinReplayLength })
			await replays.SaveAsync(score, replay, cancellationToken);

		// Every passed score counts toward ranked score, whatever status the beatmapset is reported with.
		var current = await stats.AddPlayAsync(player, submission.Score.Mode, submission.Score.TotalScore,
			submission.Score.IsPassed ? submission.Score.TotalScore : 0, cancellationToken);

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
	/// <param name="banchoClient">The osu! client the player logged in with.</param>
	/// <param name="beatmap">The MD5 and storyboard MD5 of the beatmap the score is checked against.</param>
	/// <param name="playerName">The player's name as the server knows it.</param>
	/// <param name="submittedClient">The osu! client information sent with the submission.</param>
	/// <returns><see langword="null" /> if the submission is authentic; otherwise, the first reason it is rejected.</returns>
	private static ScoreRejection? Validate(
		Submission submission,
		BanchoClient banchoClient,
		BeatmapChecksums beatmap,
		string playerName,
		SubmittedClient submittedClient)
	{
		var md5ByServer = ComputeSubmissionMd5();

		if (submittedClient.VersionDate != banchoClient.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			return ScoreRejection.VersionMismatch;
		if (submittedClient.Fingerprint != banchoClient.Fingerprint) return ScoreRejection.ClientHashMismatch;
		if (new Md5(Encoding.UTF8.GetBytes(submittedClient.UninstallId)) != banchoClient.Fingerprint.UninstallHash)
			return ScoreRejection.UninstallerHashMismatch;
		if (new Md5(Encoding.UTF8.GetBytes(submittedClient.DiskSignature)) !=
		    banchoClient.Fingerprint.DiskSignatureHash)
			return ScoreRejection.DiskSignatureHashMismatch;
		if (submission.HashByClient != md5ByServer) return ScoreRejection.SubmissionHashMismatch;
		if (submittedClient.BeatmapHash != beatmap.Hash) return ScoreRejection.BeatmapHashMismatch;

		return null;

		#region DON'T CHANGE THESE FORMULA!!!

		string ComputeSubmissionMd5()
		{
			var hitCounts = submission.Score.HitCounts;

			var raw =
				$"chickenmcnuggets{hitCounts.Num100 + hitCounts.Num300}o15{hitCounts.Num50}{hitCounts.NumGeki}" +
				$"smustard{hitCounts.NumKatu}{hitCounts.NumMiss}uu{beatmap.Hash}{submission.Score.MaxCombo}" +
				$"{submission.Score.IsFullCombo}{playerName}{submission.Score.TotalScore}{submission.Score.Grade.ToString().ToUpperInvariant()}" +
				$"{(int)submission.Score.Mods}Q{submission.Score.IsPassed}{(int)submission.Score.Mode}" +
				$"{submittedClient.VersionDate}{submission.Score.Timestamp:yyMMddHHmmss}{submittedClient.ClientHash}{beatmap.StoryboardHash?.ToString() ?? string.Empty}";
			var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
			return Convert.ToHexStringLower(hash);
		}

		#endregion
	}
}