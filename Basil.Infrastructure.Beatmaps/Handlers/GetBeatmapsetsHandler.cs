using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Persistence;
using Basil.Infrastructure.Beatmaps.Logic;
using Mediator;

namespace Basil.Infrastructure.Beatmaps.Handlers;

internal sealed class GetBeatmapsetsHandler(IBeatmapsetRepository sets, IBeatmapRepository beatmaps, TimeProvider clock)
	: IRequestHandler<GetBeatmapsets, IReadOnlyList<BeatmapsetDetail>>
{
	public async ValueTask<IReadOnlyList<BeatmapsetDetail>> Handle(GetBeatmapsets request, CancellationToken ct)
	{
		var mayViewHidden = BeatmapVisibility.MayViewHidden(request.Actor, clock);
		var found = (await sets.ListAsync(request.SetIds, ct)).ToDictionary(set => set.Id);
		var visible = request.SetIds.Distinct().Where(id => found.TryGetValue(id, out var set) &&
		                                                    BeatmapVisibility.CanView(set, mayViewHidden))
			.Select(id => found[id]).ToList();
		if (visible.Count == 0) return [];

		var byId = (await beatmaps.ListAsync(visible, ct)).ToLookup(map => map.Beatmapset.Id);
		return [.. visible.Select(set => new BeatmapsetDetail(set,
			[.. byId[set.Id].Where(map => BeatmapVisibility.CanView(map, mayViewHidden)).OrderBy(map => map.Id)]))];
	}
}
