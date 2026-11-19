using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class StopObservingRoomHandler : IRequestHandler<StopObservingRoom, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(StopObservingRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.ObserverLeaveAsync());
	}
}