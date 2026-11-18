using Basil.Application.Scores.Persistence;
using Basil.Domain.Game.Client;
using Basil.Domain.Game.Scores;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Ports;

internal interface IScoreRecorder
{
	Task<ScoreOutcome> ValidateAndRecordAsync(UserRecord player, OsuClient loginClient, Submission submission,
		BeatmapChecksums checkedBeatmap, SubmittedClient client, Stream? replay,
		CancellationToken ct = default);
}