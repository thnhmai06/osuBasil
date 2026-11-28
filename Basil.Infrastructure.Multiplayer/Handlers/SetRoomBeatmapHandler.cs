using Basil.Application.Beatmaps.Messages;
using Basil.Application.Multiplayer.Messages;
using Basil.Domain.Multiplayer.Room;
using Mediator;

namespace Basil.Infrastructure.Multiplayer.Handlers;

internal sealed class SetRoomBeatmapHandler(ISender sender) : IRequestHandler<SetRoomBeatmap, RoomActionResult>
{
	public async ValueTask<RoomActionResult> Handle(SetRoomBeatmap request, CancellationToken ct)
	{
		var settings = request.Room.ScopeTo(request.Actor).Settings;
		if (settings.RequireConfigure(new RoomSettingsChange()) is { } refusal) return refusal;

		if (await sender.Send(new FindBeatmap(request.BeatmapId), ct) is not { } beatmap)
			return RoomActionResult.NoBeatmap;

		return await settings.ConfigureAsync(new RoomSettingsChange(Beatmap: beatmap.Reference,
			Mode: request.Mode ?? beatmap.Metadata.Mode));
	}
}
