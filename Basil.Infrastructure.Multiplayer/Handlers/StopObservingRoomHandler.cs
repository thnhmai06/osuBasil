using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class StopObservingRoomHandler : IRequestHandler<StopObservingRoom, RoomJoinResult>
{
	public ValueTask<RoomJoinResult> Handle(StopObservingRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomJoinResult>(
			request.Room.ScopeTo(request.Actor).Members.ObserverLeaveAsync());
	}
}