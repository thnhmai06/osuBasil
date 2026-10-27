using Basil.Application.Persistence.Scores;
using Basil.Application.Runtime.Common;
using Basil.Application.Runtime.Connections;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Runtime.Scores;

/// <summary>The score submission as one game client may do it.</summary>
public sealed class ScopedScoreSubmitter : Scoped<UserPermissions, BanchoConnection>
{
	private readonly ScoreSubmitter _submitter;

	internal ScopedScoreSubmitter(ScoreSubmitter submitter, BanchoConnection by, UserPermissions permissions)
		: base(by, permissions)
	{
		_submitter = submitter;
	}

	/// <summary>Validates and records the score the game client submitted.</summary>
	/// <param name="submission">The parsed submission.</param>
	/// <param name="beatmap">
	///     The beatmap the server knows for the submission, or <see langword="null" /> when the server does
	///     not have it.
	/// </param>
	/// <param name="client">The osu! client information sent with the submission.</param>
	/// <param name="replay">
	///     The seekable stream of the replay, positioned at its start and left open, or <see langword="null" /> for a
	///     failed play.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the submission.</param>
	/// <returns>
	///     Ok when the score was stored; otherwise Rejected with the rejection, or the permissions the client lacks.
	/// </returns>
	/// <remarks>
	///     Needs <see cref="UserPermissions.ScoreSubmit" />. A score on a beatmap the server does not have is taken only
	///     when <see cref="IScoreBeatmapAcceptance" /> accepts it. Every accepted play adds to the play count and the total
	///     score; passed plays also add to the ranked score. The submission is checked against the player's latest
	///     recorded login. A submission whose checksum was already stored is refused. Only a passed play keeps its
	///     replay, and only when the replay is at least 24 bytes long.
	/// </remarks>
	public async Task<(Result<ScoreSubmissionOutcome> Result, ScoreRejection? Rejection)> SubmitAsync(
		Submission submission,
		BeatmapChecksums? beatmap,
		SubmittedClient client,
		Stream? replay,
		CancellationToken cancellationToken = default)
	{
		if (Require(UserPermissions.ScoreSubmit) is { } missing)
			return (Result<ScoreSubmissionOutcome>.Denied(missing), null);

		var checkedBeatmap = beatmap ??
		                     (_submitter.Acceptance.AcceptsUnknown(By.Record, client.BeatmapHash)
			                     ? new BeatmapChecksums(client.BeatmapHash, null)
			                     : null);
		if (checkedBeatmap is null) return (ScoreSubmissionOutcome.Rejected, ScoreRejection.UnknownBeatmap);

		var outcome = await _submitter.Recorder.ValidateAndRecordAsync(
			By.Record, By.Login.Client!, submission, checkedBeatmap, client, replay, cancellationToken);
		if (outcome.Rejection is { } rejection) return (ScoreSubmissionOutcome.Rejected, rejection);

		_submitter.Events.Publish(new ScoreSubmitted(By.Record, outcome.Score!, outcome.Stats!));
		return (ScoreSubmissionOutcome.Ok, null);
	}
}