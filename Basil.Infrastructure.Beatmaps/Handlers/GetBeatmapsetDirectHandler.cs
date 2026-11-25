using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapsetDirectHandler(ISender sender, TimeProvider clock)
	: IRequestHandler<GetBeatmapsetDirect, (BeatmapDirectResult Result, BeatmapsetDetail? Detail)>
{
	public async ValueTask<(BeatmapDirectResult Result, BeatmapsetDetail? Detail)> Handle(GetBeatmapsetDirect request,
		CancellationToken ct)
	{
		return BeatmapVisibility.MayUseDirect(request.Actor, clock)
			? (BeatmapDirectResult.Ok, await sender.Send(new GetBeatmapset(request.Actor, request.SetId), ct))
			: (BeatmapDirectResult.Denied, null);
	}
}