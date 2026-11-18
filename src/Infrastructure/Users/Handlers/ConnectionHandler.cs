using Basil.Application.Shared.Events;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class ConnectionHandler(UserRegistry registry, ILogger<ConnectionHandler> logger)
	: INotificationHandler<UserSessionClosed>
{
	public ValueTask Handle(UserSessionClosed @event, CancellationToken ct)
	{
		var connection = @event.Session;
		if (registry.FindSpectating(connection) is { } host)
			HandlerStep.Run(logger, "stop spectating", "the closed connection of user", connection.Record.Id,
				() => host.ScopeTo(connection).StopSpectating(), ct);
		return ValueTask.CompletedTask;
	}
}