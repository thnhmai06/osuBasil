using Basil.Application.Servers.Messages.Content;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers.Content;

internal sealed class NotifyUsersHandler(Server server)
	: IRequestHandler<NotifyUsers, (ServerOutcome Result, int Sent)>
{
	public ValueTask<(ServerOutcome Result, int Sent)> Handle(NotifyUsers request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).Notify(request.Text, request.To));
	}
}