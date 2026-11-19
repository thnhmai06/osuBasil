using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class JoinRoomHandler : IRequestHandler<JoinRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(JoinRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Members.JoinAsync(request.Password));
	}
}