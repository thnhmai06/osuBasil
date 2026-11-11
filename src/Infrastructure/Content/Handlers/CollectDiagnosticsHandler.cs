using Basil.Application.Common.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Answers the diagnostics question for Content, which keeps no live state of its own to count.</summary>
internal sealed class CollectDiagnosticsHandler : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}