using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ChangeRoomSettingsHandler : IRequestHandler<ChangeRoomSettings, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(ChangeRoomSettings request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Settings.ChangeAsync(request.Wanted));
	}
}
