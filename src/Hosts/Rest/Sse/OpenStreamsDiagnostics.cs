using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.Host.Rest.Sse;

/// <summary>Reports how many live HTTP streams are open.</summary>
internal sealed class OpenStreamsDiagnostics(SseHub hub) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "openStreams", hub.OpenStreams));
		return ValueTask.CompletedTask;
	}
}