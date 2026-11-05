using Basil.Application.Common;
using Basil.Application.Servers.Messages;
using Basil.Application.Servers.Objects;
using Mediator;

namespace Basil.Infrastructure.Servers.Handlers;

/// <summary>Handles <see cref="NotifyUsers" /> with the rights of the sending connection.</summary>
internal sealed class NotifyUsersHandler(Server server)
	: IRequestHandler<NotifyUsers, (Result<ServerOutcome> Result, int Sent)>
{
	/// <inheritdoc />
	public ValueTask<(Result<ServerOutcome> Result, int Sent)> Handle(NotifyUsers request, CancellationToken ct)
	{
		return ValueTask.FromResult(server.ScopeTo(request.Actor).Notify(request.Text, request.To));
	}
}