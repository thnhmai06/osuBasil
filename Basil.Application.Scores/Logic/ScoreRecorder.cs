using Basil.Application.Scores.Persistence;
using Basil.Application.Scores.Storage;
using Basil.Application.Shared.Exceptions;
using Basil.Application.Shared.Paging;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Logic;

/// <summary>Stores an accepted score with its replay and reports what the play changed for its player.</summary>
internal sealed class ScoreRecorder(
	IScoreRepository scores,
	IReplayStorage replays)
{
	/// <summary>Stores the score of a submission, and its replay when the play keeps one.</summary>
	/// <returns>The stored score and the player's new stats, or the reason the score was refused.</returns>
	public async Task<ScoreOutcome> RecordAsync(
		UserRecord player,
		ClientBuild loginClient,
		Submission submission,
		double? pp,
		Stream? replay,
		CancellationToken ct = default)
	{
		var previousBest = submission.Score.IsPassed ? await PreviousBestAsync(player, submission.Score) : null;

		// Every passed score counts toward ranked score, whatever status the beatmapset is reported with.
		ScoreRecord score;
		UserStats current;
		try
		{
			(score, current) = await scores.CreateWithPlayAsync(
				submission.Score with { UserId = player.Id, Checksum = submission.HashByClient, PerformancePoints = pp },
				submission.Score.IsPassed ? submission.Score.LastState.TotalScore : 0);
		}
		catch (AlreadyExistsException)
		{
			return new ScoreOutcome(SubmissionRejectionReason.Duplicate, null, null);
		}

		if (replay is not null && submission.KeepsReplay(replay.Length))
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
}