using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

internal sealed class ResetExceptionsHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<ResetExceptions, DiagnosticsCommandResult>
{
	public ValueTask<DiagnosticsCommandResult> Handle(ResetExceptions request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).ResetExceptions());
	}
}