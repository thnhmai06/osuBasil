using Basil.Application.Common;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Objects;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Records a submitted score with the privileges of the submitting game client.</summary>
internal sealed class SubmitScoreHandler(ScoreSubmitter submitter)
	: IRequestHandler<SubmitScore, (ScoreSubmissionOutcome Result, ScoreRejectionReason? Rejection)>
{
	/// <inheritdoc />
	public async ValueTask<(ScoreSubmissionOutcome Result, ScoreRejectionReason? Rejection)> Handle(
		SubmitScore request, CancellationToken ct)
	{
		return await submitter.ScopeTo(request.Actor).SubmitAsync(request.Submission, request.Beatmap, request.Client,
			request.Replay, ct);
	}
}