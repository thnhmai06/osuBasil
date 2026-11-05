using Basil.Application.Common.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Answers the diagnostics question for Servers, which keeps no live state of its own to count.</summary>
internal sealed class CollectDiagnosticsHandler : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken cancellationToken)
	{
		return ValueTask.CompletedTask;
	}
}