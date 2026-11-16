using Basil.Application.Shared.Diagnostics;
using Basil.Application.Shared.Events;
using Basil.Infrastructure.Shared.Persistence;
using Mediator;

namespace Basil.Infrastructure.Shared.Handlers;

/// <summary>
///     Reports the <c>database</c> category (commands, failures, latency of the last 256 commands, connections when the
///     engine tells) and the <c>events</c> category (published, pending, failed handlers).
/// </summary>
internal sealed class CollectDiagnosticsHandler(Database database, EventQueue queue)
	: INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		var collector = notification.Collector;
		var metrics = database.Metrics;
		collector.Add(new DiagnosticsValue("database", "commandsTotal", metrics.Commands));
		collector.Add(new DiagnosticsValue("database", "commandsFailedTotal", metrics.Failed));
		collector.Add(new DiagnosticsValue("database", "recentCommandP50Microseconds",
			(long)(metrics.Percentile(0.5) * 1000)));
		collector.Add(new DiagnosticsValue("database", "recentCommandP95Microseconds",
			(long)(metrics.Percentile(0.95) * 1000)));
		var connections = database.Connections;
		collector.Add(new DiagnosticsValue("database", "connectionsMeasured", connections is null ? 0 : 1));
		if (connections is { } pool)
		{
			collector.Add(new DiagnosticsValue("database", "connectionsInUse", pool.InUse));
			collector.Add(new DiagnosticsValue("database", "connectionsIdle", pool.Idle));
		}

		collector.Add(new DiagnosticsValue("events", "publishedTotal", queue.Published));
		collector.Add(new DiagnosticsValue("events", "pending", queue.Outstanding));
		collector.Add(new DiagnosticsValue("events", "handlerFailuresTotal", queue.Failures));
		return ValueTask.CompletedTask;
	}
}
