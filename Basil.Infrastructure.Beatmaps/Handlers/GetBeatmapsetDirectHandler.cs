using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapsetDirectHandler(ISender sender, Beatmapsets beatmapsets)
	: IRequestHandler<GetBeatmapsetDirect, (BeatmapDirectResult Result, BeatmapsetDetail? Detail)>
{
	public async ValueTask<(BeatmapDirectResult Result, BeatmapsetDetail? Detail)> Handle(GetBeatmapsetDirect request,
		CancellationToken ct)
	{
		return beatmapsets.ScopeTo(request.Actor).MayUseDirect()
			? (BeatmapDirectResult.Ok, await sender.Send(new GetBeatmapset(request.Actor, request.SetId), ct))
			: (BeatmapDirectResult.Denied, null);
	}
}