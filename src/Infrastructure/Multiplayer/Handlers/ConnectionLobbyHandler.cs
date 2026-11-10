using Basil.Application.Common.Events;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Takes a closed connection out of its room and out of the lobby's watchers.</summary>
internal sealed class ConnectionLobbyHandler(Lobby lobby, ILogger<ConnectionLobbyHandler> logger)
	: INotificationHandler<UserConnectionClosed>
{
	private const string Subject = "the closed connection of user";

	/// <inheritdoc />
	public async ValueTask Handle(UserConnectionClosed @event, CancellationToken ct)
	{
		var connection = @event.Connection;
		await HandlerStep.RunAsync(logger, "release its room", Subject, connection.Record.Id,
			() => lobby.ReleaseAsync(connection), ct);
		if (connection is StableConnection stable)
			HandlerStep.Run(logger, "leave the lobby", Subject, connection.Record.Id, () => lobby.Unwatch(stable),
				ct);
	}
}