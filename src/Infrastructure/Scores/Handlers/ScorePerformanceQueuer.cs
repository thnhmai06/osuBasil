using Basil.Application.Scores.Objects.Events;
using Basil.Infrastructure.Scores.Workers;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class ScorePerformanceQueuer(ScorePerformanceQueue queue) : INotificationHandler<ScoreSubmitted>
{
	public ValueTask Handle(ScoreSubmitted @event, CancellationToken ct)
	{
		queue.Enqueue(@event.Score.Id);
		return ValueTask.CompletedTask;
	}
}