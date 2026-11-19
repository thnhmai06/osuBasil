using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ConfigureRoomHandler : IRequestHandler<ConfigureRoom, RoomActionResult>
{
	public ValueTask<RoomActionResult> Handle(ConfigureRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomActionResult>(
			request.Room.ScopeTo(request.Actor).Settings.ConfigureAsync(request.Change));
	}
}