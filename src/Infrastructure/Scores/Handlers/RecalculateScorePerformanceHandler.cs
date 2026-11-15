using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Objects;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Handles <see cref="RecalculateScorePerformance" /> with the privileges of the sending actor.</summary>
internal sealed class RecalculateScorePerformanceHandler(ScorePerformances performances)
	: IRequestHandler<RecalculateScorePerformance, ScorePerformanceOutcome>
{
	/// <inheritdoc />
	public ValueTask<ScorePerformanceOutcome> Handle(RecalculateScorePerformance request, CancellationToken ct)
	{
		return ValueTask.FromResult(performances.ScopeTo(request.Actor).Recalculate(request.Scope));
	}
}
