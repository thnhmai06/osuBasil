using Basil.Application.Beatmaps.Messages;
using Basil.Domain.Beatmaps;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetLowestBeatmapHandler(ISender sender)
	: IRequestHandler<GetLowestBeatmap, (bool SetFound, BeatmapRecord? Beatmap)>
{
	public async ValueTask<(bool SetFound, BeatmapRecord? Beatmap)> Handle(GetLowestBeatmap request,
		CancellationToken ct)
	{
		if (await sender.Send(new GetBeatmapset(request.Actor, request.SetId), ct) is not { } detail)
			return (false, null);

		return (true, detail.Beatmaps.OrderBy(map => map.Id).FirstOrDefault());
	}
}
