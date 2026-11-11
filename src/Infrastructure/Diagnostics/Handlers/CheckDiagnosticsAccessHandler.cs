using Basil.Application.Common;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Handles <see cref="CheckDiagnosticsAccess" /> with the privileges of the sending connection.</summary>
internal sealed class CheckDiagnosticsAccessHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<CheckDiagnosticsAccess, DiagnosticsOutcome>
{
	/// <inheritdoc />
	public ValueTask<DiagnosticsOutcome> Handle(CheckDiagnosticsAccess request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).Access());
	}
}