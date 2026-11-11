using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Handles <see cref="ClearCaches" /> with the privileges of the sending connection.</summary>
internal sealed class ClearCachesHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<ClearCaches, (DiagnosticsOutcome Result, int Deleted)>
{
	/// <inheritdoc />
	public ValueTask<(DiagnosticsOutcome Result, int Deleted)> Handle(ClearCaches request,
		CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).ClearCaches());
	}
}