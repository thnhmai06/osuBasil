using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Sessions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Runtime.Multiplayer;

/// <summary>Takes a closed connection out of its room and out of the lobby's watchers.</summary>
internal sealed class ConnectionLobbyHandler(Lobby lobby, ILogger<ConnectionLobbyHandler> logger)
	: INotificationHandler<UserConnectionClosed>
{
	private const string Subject = "the closed connection of user";

	/// <inheritdoc />
	public async ValueTask Handle(UserConnectionClosed @event, CancellationToken cancellationToken)
	{
		var connection = @event.Connection;
		await HandlerStep.RunAsync(logger, "release its room", Subject, connection.Record.Id,
			() => lobby.ReleaseAsync(connection, cancellationToken), cancellationToken);
		if (connection is BanchoConnection bancho)
			HandlerStep.Run(logger, "leave the lobby", Subject, connection.Record.Id, () => lobby.Unwatch(bancho),
				cancellationToken);
	}
}