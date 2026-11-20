using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Objects;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Scores;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class SubmitScoreHandler(ScoreSubmitter submitter)
	: IRequestHandler<SubmitScore, (ScoreSubmissionResult Result, SubmissionRejectionReason? Rejection, ScoreReceipt?
		Receipt)>
{
	public async ValueTask<(ScoreSubmissionResult Result, SubmissionRejectionReason? Rejection, ScoreReceipt? Receipt)>
		Handle(
			SubmitScore request, CancellationToken ct)
	{
		return await submitter.ScopeTo(request.Actor).SubmitAsync(request.Submission, request.Beatmap, request.Client,
			request.Replay, ct);
	}
}