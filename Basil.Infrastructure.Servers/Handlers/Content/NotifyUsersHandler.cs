using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class NotifyUsersHandler(Server server)
	: IRequestHandler<NotifyUsers, (ServerActionResult Result, int Sent, IReadOnlyList<int> Missing)>
{
	public async ValueTask<(ServerActionResult Result, int Sent, IReadOnlyList<int> Missing)> Handle(
		NotifyUsers request, CancellationToken ct)
	{
		return await server.ScopeTo(request.Actor).NotifyAsync(request.Text, request.To, ct);
	}
}