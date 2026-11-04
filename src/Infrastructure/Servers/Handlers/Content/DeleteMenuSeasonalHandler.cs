using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="DeleteMenuSeasonal" /> with the rights of the sending connection.</summary>
internal sealed class DeleteMenuSeasonalHandler(Server server) : IRequestHandler<DeleteMenuSeasonal, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(DeleteMenuSeasonal request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Content.DeleteSeasonalAsync(request.Image, ct);
	}
}
