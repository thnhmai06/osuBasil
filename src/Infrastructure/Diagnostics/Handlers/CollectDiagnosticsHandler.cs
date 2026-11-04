using Basil.Application.Common.Diagnostics;
using Basil.Infrastructure.Diagnostics.Services;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Reports how many watchers follow the diagnostics feed.</summary>
internal sealed class CollectDiagnosticsHandler(DiagnosticsFeed feed) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken cancellationToken)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "diagnosticsWatchers", feed.Watchers));
		return ValueTask.CompletedTask;
	}
}
