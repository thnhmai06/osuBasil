using Basil.Application.Shared;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="ListScreenshots" /> with the privileges of the sending connection.</summary>
internal sealed class ListScreenshotsHandler(Server server)
	: IRequestHandler<ListScreenshots, (ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)>
{
	/// <inheritdoc />
	public async ValueTask<(ServerOutcome Result, IReadOnlyList<ContentReference> Screenshots)> Handle(
		ListScreenshots request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).Screenshots.ListAsync(ct);
	}
}