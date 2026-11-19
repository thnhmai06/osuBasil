using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class ConfigureRoomHandler : IRequestHandler<ConfigureRoom, RoomOutcome>
{
	public ValueTask<RoomOutcome> Handle(ConfigureRoom request, CancellationToken ct)
	{
		return new ValueTask<RoomOutcome>(
			request.Room.ScopeTo(request.Actor).Settings.ConfigureAsync(request.Change));
	}
}