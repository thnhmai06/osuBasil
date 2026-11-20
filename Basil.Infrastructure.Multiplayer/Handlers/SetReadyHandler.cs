using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetReadyHandler : IRequestHandler<SetReady, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(SetReady request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Slots.SetReadyAsync(request.Ready));
	}
}