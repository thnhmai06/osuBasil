using Basil.Application.Scores.Objects.Events;
using Basil.Infrastructure.Scores.Workers;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Queues a stored score for the calculation of its performance points without waiting for it.</summary>
internal sealed class ScorePerformanceQueuer(ScorePerformanceQueue queue) : INotificationHandler<ScoreSubmitted>
{
	/// <inheritdoc />
	public ValueTask Handle(ScoreSubmitted @event, CancellationToken ct)
	{
		queue.Enqueue(@event.Score.Id);
		return ValueTask.CompletedTask;
	}
}
