using Basil.Application.Multiplayer.Registry;
using Basil.Application.Sessions.Objects.Events;
using Basil.Infrastructure.Shared.Events;
using Basil.Application.Sessions.Objects;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ConnectionLobbyHandler(Lobby lobby, ILogger<ConnectionLobbyHandler> logger)
	: INotificationHandler<SessionClosed>
{
	private const string Subject = "the closed connection of user";

	public async ValueTask Handle(SessionClosed @event, CancellationToken ct)
	{
		var connection = @event.Session;
		await HandlerStep.RunAsync(logger, "release its room", Subject, connection.Record.Id,
			() => lobby.ReleaseAsync(connection), ct);
		if (connection is StableSession stable)
			HandlerStep.Run(logger, "leave the lobby", Subject, connection.Record.Id, () => lobby.Unwatch(stable),
				ct);
	}
}