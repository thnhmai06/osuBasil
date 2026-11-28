using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Registry;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class OpenRoomHandler(Lobby lobby) : IRequestHandler<OpenRoom, OpenedRoom>
{
	public async ValueTask<OpenedRoom> Handle(OpenRoom request, CancellationToken ct)
	{
		var (room, result) = await lobby.ScopeTo(request.Actor).OpenRoomAsync(request.Name, request.Password,
			request.Source, request.IsPrivate, request.Settings);
		if (room is not null && request.Beatmap is not null)
			await room.ScopeTo(request.Actor).Settings.ConfigureAsync(new RoomSettingsChange(Beatmap: request.Beatmap));

		return new OpenedRoom(room, result);
	}
}