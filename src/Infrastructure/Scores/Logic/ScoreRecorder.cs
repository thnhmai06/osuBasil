using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Ports;
using Basil.Application.Scores.Storage;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Infrastructure.Scores.Logic;

internal sealed class ScoreRecorder(
	IScoreRepository scores,
	IReplayStorage replays) : IScoreRecorder
{
	internal const int MinReplayLength = 24;

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

		var previousBest = submission.Score.IsPassed ? await PreviousBestAsync(player, submission.Score) : null;

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
			return new ScoreOutcome(SubmissionRejectionReason.Duplicate, null, null);
		}

		if (submission.Score.IsPassed && replay is { Length: >= MinReplayLength })
			await replays.SaveAsync(score, loginClient.Version, replay);

		return new ScoreOutcome(null, score, current, previousBest);
	}

	private async Task<ScoreRecord?> PreviousBestAsync(UserRecord player, ScoreData played)
	{
		if (played.BeatmapHash is not { } hash) return null;

		var best = await scores.ListAsync(
			new ScoreQuery(player, hash, BestPerPlayer: true, PassedOnly: true, Mode: played.Mode),
			new PageRequest(0, 1));
		return best.Items.FirstOrDefault();
	}

	private static SubmissionRejectionReason? Validate(
		Submission submission,
		OsuClient osuClient,
		BeatmapChecksums beatmap,
		string playerName,
		SubmittedClient submittedClient)
	{
		var md5ByServer = ComputeSubmissionMd5();

		if (submittedClient.VersionDate != osuClient.Version.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture))
			return SubmissionRejectionReason.VersionMismatch;
		if (submittedClient.Fingerprint != osuClient.Fingerprint) return SubmissionRejectionReason.ClientHashMismatch;
		if (new Md5Hash(Encoding.UTF8.GetBytes(submittedClient.UninstallId)) != osuClient.Fingerprint.UninstallHash)
			return SubmissionRejectionReason.UninstallerHashMismatch;
		if (new Md5Hash(Encoding.UTF8.GetBytes(submittedClient.DiskSignature)) !=
		    osuClient.Fingerprint.DiskSignatureHash)
			return SubmissionRejectionReason.DiskSignatureHashMismatch;
		if (submission.HashByClient != md5ByServer) return SubmissionRejectionReason.SubmissionHashMismatch;
		if (submittedClient.BeatmapHash != beatmap.Hash) return SubmissionRejectionReason.BeatmapHashMismatch;

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