using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Basil.Application.Multiplayer.Registry;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class InspectRoomHandler(Lobby lobby) : IRequestHandler<InspectRoom, RoomJoinResult>
{
	public async ValueTask<RoomJoinResult> Handle(InspectRoom request, CancellationToken ct)
	{
		var observer = request.Room.ScopeTo(request.Actor).Members.CheckObserver();
		if (observer is not RoomJoinResult.Ok) return observer;

		return await lobby.ScopeTo(request.Actor).CanViewAsync(request.Room.Match, ct)
			? RoomJoinResult.Ok
			: RoomJoinResult.Denied;
	}
}
