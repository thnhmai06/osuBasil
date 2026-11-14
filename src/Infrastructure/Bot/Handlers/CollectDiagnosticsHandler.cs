using Basil.Application.Shared.Diagnostics;
using Basil.Infrastructure.Bot.Workers;
using Mediator;

namespace Basil.Infrastructure.Bot.Handlers;

/// <summary>Reports the room countdowns that are running.</summary>
internal sealed class CollectDiagnosticsHandler(RoomCountdowns countdowns) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "countdowns", countdowns.Count));
		return ValueTask.CompletedTask;
	}
}