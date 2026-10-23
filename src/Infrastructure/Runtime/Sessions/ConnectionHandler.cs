using Basil.Application.Runtime.Chat;
using Basil.Application.Runtime.Events;
using Basil.Application.Runtime.Multiplayer;
using Basil.Application.Runtime.Sessions;
using Basil.Application.Runtime.Users;
using Basil.Infrastructure.Runtime.Events;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Runtime.Sessions;

/// <summary>
///     Cleans up after connections: joins auto-join channels when one opens, and releases its room, spectating, lobby
///     watch and channels when one closes.
/// </summary>
internal sealed class ConnectionHandler(
	GeneralChannelRegistry channels,
	Lobby lobby,
	UserRegistry registry,
	ILogger<ConnectionHandler> logger) : IEventHandler<UserEvent>
{
	private const string Subject = "the closed connection of user";

	/// <inheritdoc />
	public async ValueTask HandleAsync(UserEvent @event, CancellationToken cancellationToken)
	{
		switch (@event)
		{
			case UserConnectionOpened opened:
				channels.JoinAutoChannels(opened.Connection);
				break;
			case UserConnectionClosed closed:
				var connection = closed.Connection;
				await HandlerStep.RunAsync(logger, "release its room", Subject, connection.Record.Id,
					() => lobby.ReleaseAsync(connection, cancellationToken), cancellationToken);
				if (registry.FindSpectating(connection) is { } host)
					HandlerStep.Run(logger, "stop spectating", Subject, connection.Record.Id,
						() => host.ScopeTo(connection).StopSpectating(), cancellationToken);
				if (connection is BanchoConnection bancho)
					HandlerStep.Run(logger, "leave the lobby", Subject, connection.Record.Id, () => lobby.Unwatch(bancho),
						cancellationToken);
				HandlerStep.Run(logger, "part its channels", Subject, connection.Record.Id,
					() => channels.PartAll(connection), cancellationToken);
				break;
		}
	}
}