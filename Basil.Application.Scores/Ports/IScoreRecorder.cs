using Basil.Application.Scores.Persistence;
using Basil.Domain.Client;
using Basil.Domain.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Ports;

internal interface IScoreRecorder
{
	SubmissionRejectionReason? Validate(UserRecord player, OsuClient loginClient, Submission submission,
		BeatmapChecksums checkedBeatmap, SubmittedClient client);

	Task<ScoreOutcome> RecordAsync(UserRecord player, OsuClient loginClient, Submission submission, double? pp,
		Stream? replay, CancellationToken ct = default);
}