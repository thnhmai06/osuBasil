using Basil.Application.Rulesets.Objects.Events;
using Basil.Application.Scores.Objects;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class ScorePerformanceCalculatedHandler(ScorePerformanceRequests requests)
	: INotificationHandler<ScorePerformanceCalculated>
{
	public async ValueTask Handle(ScorePerformanceCalculated notification, CancellationToken ct)
	{
		await requests.CalculatedAsync(notification.Key, notification.Pp);
	}
}