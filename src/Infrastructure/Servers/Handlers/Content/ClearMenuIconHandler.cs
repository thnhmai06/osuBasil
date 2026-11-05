using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="ClearMenuIcon" /> with the rights of the sending connection.</summary>
internal sealed class ClearMenuIconHandler(Server server) : IRequestHandler<ClearMenuIcon, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(ClearMenuIcon request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Content.ClearIconAsync(ct);
	}
}