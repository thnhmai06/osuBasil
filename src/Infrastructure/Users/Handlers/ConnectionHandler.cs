using Basil.Application.Common.Events;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Stops a closed connection spectating.</summary>
internal sealed class ConnectionHandler(UserRegistry registry, ILogger<ConnectionHandler> logger)
	: INotificationHandler<UserConnectionClosed>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		var connection = @event.Connection;
		if (registry.FindSpectating(connection) is { } host)
			HandlerStep.Run(logger, "stop spectating", "the closed connection of user", connection.Record.Id,
				() => host.ScopeTo(connection).StopSpectating(), ct);
		return ValueTask.CompletedTask;
	}
}