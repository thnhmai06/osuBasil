using Basil.Application.Common.Diagnostics;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

/// <summary>Answers the diagnostics question for Beatmaps, which keeps no live state of its own to count.</summary>
internal sealed class CollectDiagnosticsHandler : INotificationHandler<CollectDiagnostics>
{
	/// <inheritdoc />
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}