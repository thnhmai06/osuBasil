using Basil.Application.Common;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Handles <see cref="ResetExceptions" /> with the privileges of the sending connection.</summary>
internal sealed class ResetExceptionsHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<ResetExceptions, DiagnosticsOutcome>
{
	/// <inheritdoc />
	public ValueTask<DiagnosticsOutcome> Handle(ResetExceptions request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).ResetExceptions());
	}
}