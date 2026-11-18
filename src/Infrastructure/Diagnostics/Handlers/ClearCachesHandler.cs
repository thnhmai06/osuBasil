using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

internal sealed class ClearCachesHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<ClearCaches, (DiagnosticsOutcome Result, int Deleted)>
{
	public ValueTask<(DiagnosticsOutcome Result, int Deleted)> Handle(ClearCaches request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).ClearCaches());
	}
}