using Basil.Application.Rulesets.Messages;
using Basil.Infrastructure.Rulesets.Workers;
using Mediator;

namespace Basil.Infrastructure.Rulesets.Handlers;

internal sealed class CalculateBeatmapHandler(CalculationQueue queue) : IRequestHandler<CalculateBeatmap>
{
	public ValueTask<Unit> Handle(CalculateBeatmap request, CancellationToken ct)
	{
		queue.Enqueue(request);
		return Unit.ValueTask;
	}
}