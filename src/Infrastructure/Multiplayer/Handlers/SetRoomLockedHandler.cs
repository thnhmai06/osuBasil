using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Objects;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetRoomLockedHandler : IRequestHandler<SetRoomLocked, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(SetRoomLocked request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Settings.SetLockedAsync(request.Locked));
	}
}