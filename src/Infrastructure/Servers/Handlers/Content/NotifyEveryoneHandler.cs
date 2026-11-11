using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="NotifyEveryone" /> with the privileges of the sending connection.</summary>
internal sealed class NotifyEveryoneHandler(Server server)
	: IRequestHandler<NotifyEveryone, (ServerOutcome Result, int Sent)>
{
	/// <inheritdoc />
	public ValueTask<(ServerOutcome Result, int Sent)> Handle(NotifyEveryone request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).NotifyAll(request.Text));
	}
}