using Basil.Application.Common;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Handles <see cref="ClearCaches" /> with the privileges of the sending connection.</summary>
internal sealed class ClearCachesHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<ClearCaches, (Result<DiagnosticsOutcome> Result, int Deleted)>
{
	/// <inheritdoc />
	public ValueTask<(Result<DiagnosticsOutcome> Result, int Deleted)> Handle(ClearCaches request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).ClearCaches());
	}
}