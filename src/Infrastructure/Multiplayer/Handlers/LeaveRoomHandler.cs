using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class LeaveRoomHandler : IRequestHandler<LeaveRoom, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(LeaveRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Members.LeaveAsync());
	}
}