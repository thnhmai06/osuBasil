using Basil.Application.Shared.Diagnostics;
using Mediator;

namespace Basil.ArchitectureTests.Handlers;

internal sealed class OnlyAHandlerOfSomethingElse;

internal sealed class Answering : INotificationHandler<CollectDiagnostics>
{
	public ValueTask Handle(CollectDiagnostics notification, CancellationToken ct)
	{
		return ValueTask.CompletedTask;
	}
}