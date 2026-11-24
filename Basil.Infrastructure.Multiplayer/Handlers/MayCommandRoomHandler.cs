using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class MayCommandRoomHandler : IRequestHandler<MayCommandRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(MayCommandRoom request, CancellationToken ct)
	{
		return ValueTask.FromResult(request.Room.ScopeTo(request.Actor).MayAct(request.Roles));
	}
}
