using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="NotifyUsers" /> with the privileges of the sending connection.</summary>
internal sealed class NotifyUsersHandler(Server server)
	: IRequestHandler<NotifyUsers, (ServerOutcome Result, int Sent)>
{
	/// <inheritdoc />
	public ValueTask<(ServerOutcome Result, int Sent)> Handle(NotifyUsers request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).Notify(request.Text, request.To));
	}
}