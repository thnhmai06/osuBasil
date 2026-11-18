using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

internal sealed class CheckDiagnosticsAccessHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<CheckDiagnosticsAccess, DiagnosticsOutcome>
{
	public ValueTask<DiagnosticsOutcome> Handle(CheckDiagnosticsAccess request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).Access());
	}
}