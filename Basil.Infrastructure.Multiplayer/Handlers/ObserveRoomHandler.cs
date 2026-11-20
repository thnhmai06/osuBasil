using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ObserveRoomHandler : IRequestHandler<ObserveRoom, RoomJoinResult>
{
	public ValueTask<RoomJoinResult> Handle(ObserveRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomJoinResult>(
			request.Room.ScopeTo(request.Actor).Members.ObserverJoinAsync());
	}
}