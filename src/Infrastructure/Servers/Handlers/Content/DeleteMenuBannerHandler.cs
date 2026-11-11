using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="DeleteMenuBanner" /> with the privileges of the sending connection.</summary>
internal sealed class DeleteMenuBannerHandler(Server server) : IRequestHandler<DeleteMenuBanner, ServerOutcome>
{
	/// <inheritdoc />
	public async ValueTask<ServerOutcome> Handle(DeleteMenuBanner request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Banners.DeleteAsync(request.Banner);
	}
}