using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Registry;
using Basil.Infrastructure.Shared.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Sessions.Handlers;

internal sealed class ConnectionHandler(SessionRegistry registry, ILogger<ConnectionHandler> logger)
	: INotificationHandler<SessionClosed>
{
	public ValueTask Handle(SessionClosed @event, CancellationToken ct)
	{
		var connection = @event.Session;
		if (registry.FindSpectating(connection) is { } host)
			HandlerStep.Run(logger, "stop spectating", "the closed connection of user", connection.Record.Id,
				() => host.ScopeTo(connection).StopSpectating(), ct);
		return ValueTask.CompletedTask;
	}
}