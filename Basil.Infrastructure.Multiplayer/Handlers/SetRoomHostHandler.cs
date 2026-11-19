using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetRoomHostHandler : IRequestHandler<SetRoomHost, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SetRoomHost request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.SetHostAsync(request.Host));
	}
}