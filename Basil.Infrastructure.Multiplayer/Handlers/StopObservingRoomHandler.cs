using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class StopObservingRoomHandler : IRequestHandler<StopObservingRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(StopObservingRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Members.ObserverLeaveAsync());
	}
}