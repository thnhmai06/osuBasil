using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetHasMapHandler : IRequestHandler<SetHasMap, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SetHasMap request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Slots.SetHasMapAsync(request.Has));
	}
}