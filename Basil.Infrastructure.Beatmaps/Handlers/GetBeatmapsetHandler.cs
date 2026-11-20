using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapsetHandler(IBeatmapsetRepository sets, IBeatmapRepository beatmaps, TimeProvider clock)
	: IRequestHandler<GetBeatmapset, BeatmapsetDetail?>
{
	public async ValueTask<BeatmapsetDetail?> Handle(GetBeatmapset request, CancellationToken ct)
	{
		var mayViewHidden = BeatmapVisibility.MayViewHidden(request.Actor, clock);
		if (await sets.GetAsync(request.SetId, ct) is not { } set || !BeatmapVisibility.CanView(set, mayViewHidden))
			return null;

		var list = await beatmaps.ListAsync(set, ct);
		return new BeatmapsetDetail(set,
			[.. list.Where(map => BeatmapVisibility.CanView(map, mayViewHidden)).OrderBy(map => map.Id)]);
	}
}