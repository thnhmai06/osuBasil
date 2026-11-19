using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class DeleteMenuSeasonalHandler(Server server)
	: IRequestHandler<DeleteMenuSeasonal, ServerOutcome>
{
	public async ValueTask<ServerOutcome> Handle(DeleteMenuSeasonal request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Seasonals.DeleteAsync(request.Image);
	}
}