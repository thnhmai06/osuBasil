using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="CheckDiagnosticsAccess" /> with the rights of the sending connection.</summary>
internal sealed class CheckDiagnosticsAccessHandler(Server server) : IRequestHandler<CheckDiagnosticsAccess, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public ValueTask<Result<ServerOutcome>> Handle(CheckDiagnosticsAccess request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).Diagnostics.Access());
	}
}
