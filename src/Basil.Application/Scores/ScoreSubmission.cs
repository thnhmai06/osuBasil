using Basil.Domain.Scores;
using Basil.Domain.Utilities;
using Basil.Application.Sessions;

namespace Basil.Application.Scores;

/// <summary>Validates and stores a submitted score, its replay, and the submitter's updated statistics.</summary>
public sealed class ScoreSubmission(
	IScoreRepository scores,
	IReplayStorage replays,
	IUserStatsRepository stats)
{
	/// <summary>Validates and records a score submission.</summary>
	/// <remarks>A play on the beatmap of the submitter's room's latest round is recorded against that round.</remarks>
	/// <param name="session">The session that submitted the score.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="beatmap">The beatmap the server knows for the submission's claimed MD5, and its storyboard MD5, if any.</param>
	/// <param name="playerName">The submitting player's username, as known to the server.</param>
	/// <param name="clientFingerprint">The client hash and unique ids the client sent with the submission.</param>
	/// <param name="clientVersionDate">The client version date the client sent with the submission.</param>
	/// <param name="clientBeatmapHash">The beatmap MD5 the client claims to have played.</param>
	/// <param name="replay">The submission's replay bytes, or <see langword="null" /> for a failed play.</param>
	/// <param name="cancellationToken">A token that cancels the persistence.</param>
	/// <returns>
	///     <see langword="null" /> when the submission was validated and stored; otherwise, the reason
	///     it was rejected.
	/// </returns>
	public async Task<string?> SubmitAsync(
		GameSession session,
		Submission submission,
		(Md5 Hash, Md5? StoryboardHash) beatmap,
		string playerName,
		(string Hash, string Serial) clientFingerprint,
		string clientVersionDate,
		Md5 clientBeatmapHash,
		byte[]? replay,
		CancellationToken cancellationToken = default)
	{
		if (!submission.Validate(
			    (session.ClientFingerprint, session.ClientVersion, beatmap, playerName),
			    (clientFingerprint, clientVersionDate, clientBeatmapHash),
			    out var error))
			return error;

		var round = session.Room?.LastRound is { } last && last.BeatmapHash == submission.Score.BeatmapHash
			? last
			: null;
		var score = await scores.AddAsync(submission.Score with { UserId = session.User.Id, Round = round },
			cancellationToken);

		if (replay is not null)
		{
			await using var content = new MemoryStream(replay, false);
			await replays.SaveAsync(score, content, cancellationToken);
		}

		if (submission.Score.IsPassed)
		{
			var current = await stats.LoadAsync(session.User, submission.Score.Mode, cancellationToken);
			// Every beatmap reports as Approved (see Beatmapset.Status), so every passed score counts
			// toward ranked score too.
			current.TotalScore += submission.Score.TotalScore;
			current.RankedScore += submission.Score.TotalScore;
			current.PlayCount++;
			await stats.SaveAsync(current, cancellationToken);
		}

		// TODO(phase 9): StatsChanged
		return null;
	}
}