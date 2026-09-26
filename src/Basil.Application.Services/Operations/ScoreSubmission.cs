using Basil.Application.Contracts.Repositories;
using Basil.Application.Contracts.Storages;
using Basil.Application.Models.Notifications;
using Basil.Application.Models.Sessions;
using Basil.Domain.Scores;

namespace Basil.Application.Services.Operations;

/// <summary>Validates and stores a submitted score, its replay, and the submitter's updated statistics.</summary>
public sealed class ScoreSubmission(
	IRepository<int, Score> scores,
	IBlobStorage<int> replays,
	IUserStatsRepository stats)
{
	/// <summary>Validates and records a score submission.</summary>
	/// <param name="session">The session that submitted the score.</param>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="scoreId">The id to store the score under.</param>
	/// <param name="beatmap">The beatmap the server knows for the submission's claimed MD5, and its storyboard MD5, if any.</param>
	/// <param name="playerName">The submitting player's username, as known to the server.</param>
	/// <param name="clientFingerprint">The fingerprint MD5 and serial the client sent with the submission.</param>
	/// <param name="clientVersionDate">The client version date the client sent with the submission.</param>
	/// <param name="clientBeatmapMd5">The beatmap MD5 the client claims to have played.</param>
	/// <param name="replay">The submission's replay bytes, or <see langword="null" /> for a failed play.</param>
	/// <param name="cancellationToken">A token that cancels the persistence.</param>
	/// <returns>
	///     <see langword="null" /> when the submission was validated and stored; otherwise, the reason
	///     it was rejected.
	/// </returns>
	public async Task<string?> SubmitAsync(
		GameSession session,
		Submission submission,
		int scoreId,
		(string Md5, string? StoryboardMd5) beatmap,
		string playerName,
		(string Md5, string Serial) clientFingerprint,
		string clientVersionDate,
		string clientBeatmapMd5,
		byte[]? replay,
		CancellationToken cancellationToken = default)
	{
		if (!submission.Validate(
			    (session.ClientFingerprint, session.ClientVersion, beatmap, playerName),
			    (clientFingerprint, clientVersionDate, clientBeatmapMd5),
			    out var error))
			return error;

		await scores.SaveAsync(submission.Score with { UserId = session.UserId }, cancellationToken);

		if (replay is not null)
		{
			await using var content = new MemoryStream(replay, writable: false);
			await replays.SaveAsync(scoreId, content, cancellationToken);
		}

		if (submission.Score.IsPassed)
		{
			var current = await stats.LoadAsync(session.UserId, submission.Score.Mode, cancellationToken);
			// Every beatmap reports as Approved (see Beatmapset.Status), so every passed score counts
			// toward ranked score too.
			var updated = current.WithSubmittedScore(submission.Score.TotalScore, submission.Score.TotalScore);
			await stats.SaveAsync(updated, cancellationToken);
		}

		session.Notify(new PresenceChanged(session));
		return null;
	}
}