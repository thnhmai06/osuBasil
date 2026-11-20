using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

internal sealed class CollectGarbageHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<CollectGarbage, DiagnosticsCommandResult>
{
	public ValueTask<DiagnosticsCommandResult> Handle(CollectGarbage request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).CollectGarbage());
	}
}