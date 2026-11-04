using Basil.Application.Common;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Handles <see cref="ResetExceptions" /> with the rights of the sending connection.</summary>
internal sealed class ResetExceptionsHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<ResetExceptions, Result<DiagnosticsOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<DiagnosticsOutcome>> Handle(ResetExceptions request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).ResetExceptions());
	}
}
