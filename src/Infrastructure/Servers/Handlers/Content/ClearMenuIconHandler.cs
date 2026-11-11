using Basil.Application.Shared;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="ClearMenuIcon" /> with the privileges of the sending connection.</summary>
internal sealed class ClearMenuIconHandler(Server server) : IRequestHandler<ClearMenuIcon, ServerOutcome>
{
	/// <inheritdoc />
	public async ValueTask<ServerOutcome> Handle(ClearMenuIcon request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Icon.DeleteAsync();
	}
}