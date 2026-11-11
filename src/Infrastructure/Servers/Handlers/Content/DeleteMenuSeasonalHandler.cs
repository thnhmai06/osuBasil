using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="DeleteMenuSeasonal" /> with the privileges of the sending connection.</summary>
internal sealed class DeleteMenuSeasonalHandler(Server server)
	: IRequestHandler<DeleteMenuSeasonal, ServerOutcome>
{
	/// <inheritdoc />
	public async ValueTask<ServerOutcome> Handle(DeleteMenuSeasonal request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Seasonals.DeleteAsync(request.Image);
	}
}