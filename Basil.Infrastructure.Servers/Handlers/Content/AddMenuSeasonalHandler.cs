using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class AddMenuSeasonalHandler(Server server)
	: IRequestHandler<AddMenuSeasonal, (ServerOutcome Result, ContentReference? Image)>
{
	public async ValueTask<(ServerOutcome Result, ContentReference? Image)> Handle(AddMenuSeasonal request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Seasonals.CreateAsync(request.Name, request.Image);
	}
}