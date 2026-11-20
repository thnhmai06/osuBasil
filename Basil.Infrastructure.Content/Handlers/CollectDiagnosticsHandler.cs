using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

internal sealed class CollectDiagnosticsHandler : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}