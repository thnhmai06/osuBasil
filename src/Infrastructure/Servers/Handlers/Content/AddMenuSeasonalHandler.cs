using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="AddMenuSeasonal" /> with the privileges of the sending connection.</summary>
internal sealed class AddMenuSeasonalHandler(Server server)
	: IRequestHandler<AddMenuSeasonal, (ServerOutcome Result, ContentReference? Image)>
{
	/// <inheritdoc />
	public async ValueTask<(ServerOutcome Result, ContentReference? Image)> Handle(AddMenuSeasonal request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Seasonals.CreateAsync(request.Name, request.Image);
	}
}