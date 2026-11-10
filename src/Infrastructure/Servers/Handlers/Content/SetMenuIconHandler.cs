using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="SetMenuIcon" /> with the rights of the sending connection.</summary>
internal sealed class SetMenuIconHandler(Server server) : IRequestHandler<SetMenuIcon, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(SetMenuIcon request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Icon.ReplaceAsync(request.Name, request.Image, request.Url);
	}
}