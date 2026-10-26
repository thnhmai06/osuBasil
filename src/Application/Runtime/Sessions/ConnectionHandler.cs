using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Users;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Runtime.Sessions;

/// <summary>Stops a closed connection spectating.</summary>
internal sealed class ConnectionHandler(UserRegistry registry, ILogger<ConnectionHandler> logger)
	: INotificationHandler<UserConnectionClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken cancellationToken)
	{
		var connection = @event.Connection;
		if (registry.FindSpectating(connection) is { } host)
			HandlerStep.Run(logger, "stop spectating", "the closed connection of user", connection.Record.Id,
				() => host.ScopeTo(connection).StopSpectating(), cancellationToken);
		return ValueTask.CompletedTask;
	}
}