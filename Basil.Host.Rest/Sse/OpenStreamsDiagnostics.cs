using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.Host.Rest.Sse;

internal sealed class OpenStreamsDiagnostics(SseHub hub) : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "openStreams", hub.OpenStreams));
		return ValueTask.CompletedTask;
	}
}