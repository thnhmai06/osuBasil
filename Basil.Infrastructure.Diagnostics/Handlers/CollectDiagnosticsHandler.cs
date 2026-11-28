using Basil.Application.Shared.Diagnostics;
using Basil.Infrastructure.Diagnostics.Services;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

internal sealed class CollectDiagnosticsHandler(DiagnosticsFeed feed) : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "diagnosticsWatchers", feed.Watchers));
		return ValueTask.CompletedTask;
	}
}