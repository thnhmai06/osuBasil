using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ObserveRoomHandler : IRequestHandler<ObserveRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(ObserveRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Members.ObserverJoinAsync());
	}
}