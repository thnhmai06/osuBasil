using Basil.Application.Chat.Registry;
using Basil.Application.Common.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

/// <summary>Reports the general channels that are open.</summary>
internal sealed class CollectDiagnosticsHandler(GeneralChannelRegistry channels)
	: INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "channels", channels.All.Count));
		return ValueTask.CompletedTask;
	}
}