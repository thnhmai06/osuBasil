using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="AddMenuBanner" /> with the rights of the sending connection.</summary>
internal sealed class AddMenuBannerHandler(Server server)
	: IRequestHandler<AddMenuBanner, (Result<ServerOutcome> Result, MenuBanner? Banner)>
{
	/// <inheritdoc />
	public async ValueTask<(Result<ServerOutcome> Result, MenuBanner? Banner)> Handle(AddMenuBanner request,
		CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Content.AddBannerAsync(request.Name, request.Image, request.Url,
			request.StartsAt, request.EndsAt, ct);
	}
}