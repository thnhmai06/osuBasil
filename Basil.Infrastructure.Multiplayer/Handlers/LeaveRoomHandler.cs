using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class LeaveRoomHandler : IRequestHandler<LeaveRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(LeaveRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Members.LeaveAsync());
	}
}