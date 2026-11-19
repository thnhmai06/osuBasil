using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ObserveRoomHandler : IRequestHandler<ObserveRoom, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(ObserveRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.ObserverJoinAsync());
	}
}