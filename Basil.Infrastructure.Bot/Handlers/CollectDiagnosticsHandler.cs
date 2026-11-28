using Basil.Application.Shared.Diagnostics;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

internal sealed class CollectDiagnosticsHandler(RoomCountdowns countdowns) : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "countdowns", countdowns.Count));
		return ValueTask.CompletedTask;
	}
}