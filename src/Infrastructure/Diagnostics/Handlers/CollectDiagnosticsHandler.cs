using Basil.Application.Shared.Diagnostics;
using Basil.Infrastructure.Diagnostics.Services;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Reports how many watchers follow the diagnostics feed.</summary>
internal sealed class CollectDiagnosticsHandler(DiagnosticsFeed feed) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "diagnosticsWatchers", feed.Watchers));
		return ValueTask.CompletedTask;
	}
}