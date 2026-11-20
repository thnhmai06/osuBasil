using Basil.Application.Rulesets.Messages;
using Basil.Infrastructure.Rulesets.Workers;
using Mediator;

namespace Basil.Infrastructure.Rulesets.Handlers;

internal sealed class CalculateScorePerformanceHandler(CalculationQueue queue)
	: IRequestHandler<CalculateScorePerformance>
{
	public ValueTask<Unit> Handle(CalculateScorePerformance request, CancellationToken ct)
	{
		queue.Enqueue(request);
		return Unit.ValueTask;
	}
}