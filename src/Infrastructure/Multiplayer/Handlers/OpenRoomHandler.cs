using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

/// <summary>Opens a room in the lobby for the connection that asked.</summary>
internal sealed class OpenRoomHandler(Lobby lobby) : IRequestHandler<OpenRoom, OpenedRoom>
{
	/// <inheritdoc />
	public async ValueTask<OpenedRoom> Handle(OpenRoom request, CancellationToken cancellationToken)
	{
		var (room, result) = await lobby.ScopeTo(request.Actor).OpenRoomAsync(request.Name, request.Password,
			request.Source, request.IsPrivate, request.Settings, cancellationToken);
		return new OpenedRoom(room, result);
	}
}