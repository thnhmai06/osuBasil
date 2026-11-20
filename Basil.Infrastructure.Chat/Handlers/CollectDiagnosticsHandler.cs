using Basil.Application.Chat.Registry;
using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Chat.Handlers;

internal sealed class CollectDiagnosticsHandler(GeneralChannelRegistry channels)
	: INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		notification.Collector.Add(new DiagnosticsValue("application", "channels", channels.All.Count));
		return ValueTask.CompletedTask;
	}
}