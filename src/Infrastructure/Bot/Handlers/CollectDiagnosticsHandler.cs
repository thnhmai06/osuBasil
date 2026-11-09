using Basil.Infrastructure.Bot.Workers;
using Basil.Application.Common.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Reports the room countdowns that are running.</summary>
internal sealed class CollectDiagnosticsHandler(RoomCountdowns countdowns) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken cancellationToken)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "countdowns", countdowns.Count));
		return ValueTask.CompletedTask;
	}
}