using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class CloseRoomHandler : IRequestHandler<CloseRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(CloseRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).CloseAsync());
	}
}