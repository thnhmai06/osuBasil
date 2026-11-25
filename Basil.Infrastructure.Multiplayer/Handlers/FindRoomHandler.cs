using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Mediator;
using Room = Basil.Application.Multiplayer.Objects.Rooms.Room;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class FindRoomHandler(Lobby lobby) : IRequestHandler<FindRoom, Room?>
{
	public async ValueTask<Room?> Handle(FindRoom request, CancellationToken ct)
	{
		return lobby.Find(request.RoomId) is { } room && await lobby.ScopeTo(request.Actor).CanViewAsync(room.Match, ct)
			? room
			: null;
	}
}
