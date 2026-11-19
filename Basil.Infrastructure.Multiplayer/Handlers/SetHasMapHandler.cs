using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetHasMapHandler : IRequestHandler<SetHasMap, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(SetHasMap request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Slots.SetHasMapAsync(request.Has));
	}
}