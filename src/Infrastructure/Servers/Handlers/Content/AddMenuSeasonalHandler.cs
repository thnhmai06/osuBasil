using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="AddMenuSeasonal" /> with the rights of the sending connection.</summary>
internal sealed class AddMenuSeasonalHandler(Server server)
	: IRequestHandler<AddMenuSeasonal, (Result<ServerOutcome> Result, ContentReference? Image)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<ServerOutcome> Result, ContentReference? Image)> Handle(AddMenuSeasonal request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Content.AddSeasonalAsync(request.Name, request.Image, ct);
	}
}