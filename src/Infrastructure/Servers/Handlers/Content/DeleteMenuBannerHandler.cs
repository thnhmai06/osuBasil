using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="DeleteMenuBanner" /> with the rights of the sending connection.</summary>
internal sealed class DeleteMenuBannerHandler(Server server) : IRequestHandler<DeleteMenuBanner, Result<ServerOutcome>>
{
	/// <inheritdoc />
	public async ValueTask<Result<ServerOutcome>> Handle(DeleteMenuBanner request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Banners.DeleteAsync(request.Banner);
	}
}