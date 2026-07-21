namespace Basil.Application.Services.Scores;

/// <summary>Ported from app/api/domains/osu.py's build_score_submission_response/build_score_submission_error_response.</summary>
public static class ScoreSubmissionResponseBuilder
{
    public static string BuildSuccess(SubmittedScoreResult result, string domain)
    {
        return result.Score.IsPassed
            ? ScoreSubmissionChartsFormatter.Format(result.Score, result.Beatmap, result.ScoreId, result.Rank, domain)
            : "error: no";
    }

    public static string BuildError(ScoreSubmissionResultCode code)
    {
        return code switch
        {
            ScoreSubmissionResultCode.BeatmapNotFound => "error: beatmap",
            // Player isn't online — return nothing so the client retries submission once they log in.
            ScoreSubmissionResultCode.PlayerNotFound => "",
            ScoreSubmissionResultCode.DuplicateSubmission => "error: no",
            // Not in a multiplayer round — never a transient condition, so no need to invite a retry.
            ScoreSubmissionResultCode.NotInMultiplayer => "error: no",
            ScoreSubmissionResultCode.Success => throw new ArgumentException("Success is not an error code.",
                nameof(code)),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unexpected score submission result code.")
        };
    }
}