using Basil.Application.Common.Diagnostics;
using Mediator;

namespace Basil.Host.Rest.Sse;

/// <summary>Reports how many live HTTP streams are open.</summary>
internal sealed class OpenStreamsDiagnostics(SseHub hub) : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken cancellationToken)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "openStreams", hub.OpenStreams));
		return ValueTask.CompletedTask;
	}
}
