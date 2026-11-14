using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.ArchitectureTests.Handlers;

/// <summary>Stand-ins in a <c>Handlers</c> namespace for the positive controls.</summary>
internal sealed class OnlyAHandlerOfSomethingElse;

/// <summary>A stand-in handler that answers the diagnostics question.</summary>
internal sealed class Answering : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}