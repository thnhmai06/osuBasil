namespace Basil.Application.Services.Scores;

/// <summary>
///     Builds the plain-text body the osu! client receives after a score submission.
/// </summary>
/// <remarks>
///     A successful submission returns the chart payload from
///     <see cref="ScoreSubmissionChartsFormatter" /> for a passed play, or the literal
///     <c>error: no</c> for a failed play. Rejections map to the short response strings the client
///     understands, with one exception: a player that is not online produces an empty body so the
///     client retries the submission once the player logs in.
/// </remarks>
public static class ScoreSubmissionResponseBuilder
{
	/// <summary>
	///     Builds the response body for an accepted score submission.
	/// </summary>
	/// <param name="result">The accepted submission and its resolved beatmap and rank.</param>
	/// <param name="domain">The server's domain, used to build chart URLs.</param>
	/// <returns>The chart payload for a passed play, or <c>error: no</c> for a failed play.</returns>
	public static string BuildSuccess(SubmittedScoreResult result, string domain)
	{
		return result.Score.IsPassed
			? ScoreSubmissionChartsFormatter.Format(result.Score, result.Beatmap, result.ScoreId, result.Rank, domain)
			: "error: no";
	}

	/// <summary>
	///     Builds the response body for a rejected score submission.
	/// </summary>
	/// <param name="code">The rejection reason.</param>
	/// <returns>The response string for the given code.</returns>
	/// <exception cref="ArgumentException"><paramref name="code" /> is <see cref="ScoreSubmissionResultCode.Success" />.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="code" /> is not a recognized rejection reason.</exception>
	/// <remarks>
	///     <see cref="ScoreSubmissionResultCode.PlayerNotFound" /> returns an empty body on purpose:
	///     the client treats that as "not online now" and retries the submission once the player
	///     logs in.
	/// </remarks>
	public static string BuildError(ScoreSubmissionResultCode code)
	{
		return code switch
		{
			ScoreSubmissionResultCode.BeatmapNotFound => "error: beatmap",
			ScoreSubmissionResultCode.PlayerNotFound => "",
			ScoreSubmissionResultCode.DuplicateSubmission => "error: no",
			ScoreSubmissionResultCode.Success => throw new ArgumentException("Success is not an error code.",
				nameof(code)),
			_ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unexpected score submission result code.")
		};
	}
}