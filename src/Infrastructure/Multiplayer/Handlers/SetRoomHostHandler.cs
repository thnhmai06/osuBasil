using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;
using Basil.Domain.Multiplayer;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetRoomHostHandler : IRequestHandler<SetRoomHost, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SetRoomHost request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Authority.SetHostAsync(request.Host));
	}
}