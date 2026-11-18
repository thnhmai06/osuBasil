using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Objects;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class RecalculateScorePerformanceHandler(ScorePerformances performances)
	: IRequestHandler<RecalculateScorePerformance, ScorePerformanceOutcome>
{
	public ValueTask<ScorePerformanceOutcome> Handle(RecalculateScorePerformance request, CancellationToken ct)
	{
		return ValueTask.FromResult(performances.ScopeTo(request.Actor).Recalculate(request.Scope));
	}
}
