using Basil.Application.Services.Contracts.Chat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Infrastructure.Runtime.Events;

namespace Basil.Infrastructure.Runtime.Sessions;

internal sealed class ConnectionHandler(
	IChannelService channels,
	IRoomService rooms,
	ILobbyService lobby) : IEventHandler<UserEvent>
{
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
				await rooms.Members.ReleaseAsync(connection, cancellationToken);
				channels.Spectators.StopSpectating(connection);
				if (connection is BanchoConnection bancho) lobby.Unwatch(bancho);
				channels.PartAll(connection);
				break;
		}
	}
}
