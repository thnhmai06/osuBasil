using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class NotifyEveryoneHandler(Server server)
	: IRequestHandler<NotifyEveryone, (ServerActionResult Result, int Sent)>
{
	public ValueTask<(ServerActionResult Result, int Sent)> Handle(NotifyEveryone request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).NotifyAll(request.Text));
	}
}