using Basil.Application.Common;
using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Objects;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Records a submitted score with the rights of the submitting game client.</summary>
internal sealed class SubmitScoreHandler(ScoreSubmitter submitter)
	: IRequestHandler<SubmitScore, (Result<ScoreSubmissionOutcome> Result, ScoreRejectionReason? Rejection)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<ScoreSubmissionOutcome> Result, ScoreRejectionReason? Rejection)> Handle(
		SubmitScore request, CancellationToken cancellationToken)
	{
		return await submitter.ScopeTo(request.Actor).SubmitAsync(request.Submission, request.Beatmap, request.Client,
			request.Replay, cancellationToken);
	}
}
