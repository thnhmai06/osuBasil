using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Messages;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class JoinRoomHandler : IRequestHandler<JoinRoom, RoomJoinResult>
{
	public ValueTask<RoomJoinResult> Handle(JoinRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomJoinResult>(
			request.Room.ScopeTo(request.Actor).Members.JoinAsync(request.Password));
	}
}