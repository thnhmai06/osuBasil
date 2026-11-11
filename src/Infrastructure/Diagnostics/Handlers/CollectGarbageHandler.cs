using Basil.Application.Common;
using Basil.Application.Diagnostics.Messages;
using Basil.Application.Diagnostics.Objects;
using Mediator;

namespace Basil.Infrastructure.Diagnostics.Handlers;

/// <summary>Handles <see cref="CollectGarbage" /> with the privileges of the sending connection.</summary>
internal sealed class CollectGarbageHandler(ServerDiagnostics diagnostics)
	: IRequestHandler<CollectGarbage, Result<DiagnosticsOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<DiagnosticsOutcome>> Handle(CollectGarbage request, CancellationToken ct)
	{
		return ValueTask.FromResult(diagnostics.ScopeTo(request.Actor).CollectGarbage());
	}
}